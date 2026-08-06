using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using AgentHub.Common.Models;
using AgentHub.Common.Util;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// ~/.codex/sessions 의 rollout 트랜스크립트(JSONL)를 읽어 요약/상세를 제공하고,
    /// FileSystemWatcher로 변경을 감지해 콜백을 알린다. Claude용 <see cref="ClaudeSessionReader"/>의 대응물.
    /// 파싱 로직은 <see cref="CodexTranscriptParser"/>에 위임. 제목은 ~/.codex/session_index.jsonl(thread_name)을 우선 사용.
    /// </summary>
    public static class CodexSessionReader
    {
        private static readonly TimeSpan Window = TimeSpan.FromHours(24);
        // 파서의 ended 창(30분)보다 넉넉히 잡는다 — 이보다 오래 안 쓰인 파일은 요약이 더 변하지 않는다.
        private static readonly TimeSpan StaleWindow = TimeSpan.FromMinutes(35);
        private const int MaxSessions = 30;

        private static FileSystemWatcher _watcher;
        private static Action _onChanged;
        private static Timer _debounce;
        private static readonly object _debounceLock = new object();
        private static Timer _poll;

        // sessionId -> 파일 경로 (최근 스캔 캐시)
        private static readonly ConcurrentDictionary<string, string> _paths =
            new ConcurrentDictionary<string, string>();

        /// <summary>
        /// 파일 경로 -> 마지막 파싱 결과. WSL 홈은 \\wsl.localhost(9p) 네트워크 경로라 파일 하나를 여는 비용이
        /// 로컬의 20배 수준이다. 5초 폴링마다 24시간치를 전부 다시 읽으면 스냅샷 브로드캐스트가 초 단위로 밀리므로,
        /// 수정시각+크기가 그대로면 재파싱하지 않는다.
        /// </summary>
        private class ParsedSummary
        {
            public DateTime Stamp;
            public long Length;
            public string Title;   // 파서가 만든 제목(제목 인덱스 적용 전) — 재사용 시 되돌린다.
            public SessionSummary Summary;
        }

        private static readonly ConcurrentDictionary<string, ParsedSummary> _parsed =
            new ConcurrentDictionary<string, ParsedSummary>();

        private static string WindowsCodexHome =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        private static string WindowsSessionsRoot => Path.Combine(WindowsCodexHome, "sessions");

        private class CodexHomeSource
        {
            public string CodexDir;
            public string SessionsRoot => Path.Combine(CodexDir, "sessions");
        }

        private static IEnumerable<CodexHomeSource> CodexHomes()
        {
            yield return new CodexHomeSource { CodexDir = WindowsCodexHome };
            foreach (var home in Wsl.CodexHomes())
                yield return new CodexHomeSource { CodexDir = home.CodexDir };
        }

        /// <summary>Codex가 설치돼 세션 폴더가 존재하는지(Windows/WSL 모두 포함, 없으면 조용히 비활성).</summary>
        public static bool Available => CodexHomes().Any(h => Directory.Exists(h.SessionsRoot));

        /// <summary>
        /// 이 sessionId가 Codex 세션인지(엔진 라우팅용). 최근 스캔 캐시로만 답한다 —
        /// Claude 세션 id로 물어볼 때마다 WSL 9p 경로를 전부 열거하면 활동 push가 초 단위로 밀린다.
        /// 캐시는 <see cref="ListSessions"/>(5초 폴링)가 채운다.
        /// </summary>
        public static bool Has(string sessionId) =>
            !string.IsNullOrEmpty(sessionId) && _paths.ContainsKey(sessionId);

        public static List<SessionSummary> ListSessions()
        {
            var result = new List<SessionSummary>();

            var now = DateTime.UtcNow;
            var cutoff = now - Window;

            var files = new List<Tuple<FileInfo, string>>();
            var titlesByHome = new Dictionary<string, Dictionary<string, string>>();
            foreach (var home in CodexHomes())
            {
                var root = home.SessionsRoot;
                if (!Directory.Exists(root)) continue;
                try
                {
                    // 제목 인덱스는 홈당 1회만 읽는다(파일당 읽으면 9p 홈에서 최대 30배 비용).
                    titlesByHome[home.CodexDir] = LoadTitleIndex(home.CodexDir);
                    foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTimeUtc >= cutoff) files.Add(Tuple.Create(fi, home.CodexDir));
                    }
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }

            foreach (var item in files.OrderByDescending(f => f.Item1.LastWriteTimeUtc).Take(MaxSessions))
            {
                try
                {
                    var fi = item.Item1;
                    var s = SummaryOf(fi, now, out var id);
                    _paths[id] = fi.FullName;
                    if (titlesByHome.TryGetValue(item.Item2, out var titles)
                        && titles.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t)) s.Title = t;
                    result.Add(s);
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return result;
        }

        /// <summary>
        /// 파일이 그대로면 지난 파싱 결과를 재사용한다(9p 재읽기 방지). 제목은 파서 결과로 되돌려 준다.
        /// 단 요약의 status/working은 '지금'에 의존하므로, 파서의 ended 창(30분)을 넘겨 더는 변할 수 없는
        /// 파일만 캐시한다. 아직 쓰이는 중인 파일은 매번 다시 읽는다(그쪽은 몇 개뿐이라 싸다).
        /// </summary>
        private static SessionSummary SummaryOf(FileInfo fi, DateTime now, out string id)
        {
            var settled = now - fi.LastWriteTimeUtc > StaleWindow;
            if (settled && _parsed.TryGetValue(fi.FullName, out var hit)
                && hit.Stamp == fi.LastWriteTimeUtc && hit.Length == fi.Length)
            {
                id = hit.Summary.Id;
                hit.Summary.Title = hit.Title;
                return hit.Summary;
            }

            var lines = ReadAllLinesShared(fi.FullName);
            id = SessionIdOf(lines) ?? Path.GetFileNameWithoutExtension(fi.Name);
            var s = CodexTranscriptParser.Summarize(id, lines, now);
            s.PendingAsk = CodexTranscriptParser.ExtractPendingAsk(lines);
            if (settled)
                _parsed[fi.FullName] = new ParsedSummary
                {
                    Stamp = fi.LastWriteTimeUtc, Length = fi.Length, Title = s.Title, Summary = s
                };
            return s;
        }

        public static List<ActivityEvent> GetActivity(string sessionId, int max = 200)
        {
            var path = ResolvePath(sessionId);
            if (path == null) return new List<ActivityEvent>();
            try
            {
                var lines = ReadAllLinesShared(path);
                return CodexTranscriptParser.ParseEvents(lines, max);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return new List<ActivityEvent>(); }
        }

        /// <summary>세션의 작업 디렉터리(cwd)를 트랜스크립트에서 조회. resume 실행용. 실패 시 null.</summary>
        public static string CwdOf(string sessionId)
        {
            var path = ResolvePath(sessionId);
            if (path == null) return null;
            try
            {
                var lines = ReadAllLinesShared(path);
                return CodexTranscriptParser.Summarize(sessionId, lines, DateTime.UtcNow).Cwd;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }

        /// <summary>세션 제목(알림 표시용). session_index의 thread_name 우선. 없으면 null.</summary>
        public static string TitleOf(string sessionId)
        {
            try
            {
                foreach (var home in CodexHomes())
                {
                    var titles = LoadTitleIndex(home.CodexDir);
                    if (titles.TryGetValue(sessionId, out var t) && !string.IsNullOrWhiteSpace(t)) return t;
                }
                var path = ResolvePath(sessionId);
                if (path == null) return null;
                var s = CodexTranscriptParser.Summarize(sessionId, ReadAllLinesShared(path), DateTime.UtcNow);
                return s.Title == sessionId ? null : s.Title;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }

        /// <summary>세션의 마지막 어시스턴트 텍스트(알림·답장 카드 표시용). 실패 시 null.</summary>
        public static string LastAssistantTextOf(string sessionId)
        {
            var path = ResolvePath(sessionId);
            if (path == null) return null;
            try { return CodexTranscriptParser.LastAssistantText(ReadAllLinesShared(path)); }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }

        private static string ResolvePath(string sessionId)
        {
            if (_paths.TryGetValue(sessionId, out var path) && File.Exists(path)) return path;
            path = FindSessionFile(sessionId);
            if (path != null) _paths[sessionId] = path;
            return path;
        }

        // 파일명은 rollout-<시각>-<uuid>.jsonl 이라 uuid(sessionId)로 끝난다.
        private static string FindSessionFile(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return null;
            foreach (var home in CodexHomes())
            {
                var root = home.SessionsRoot;
                if (!Directory.Exists(root)) continue;
                try
                {
                    var suffix = sessionId + ".jsonl";
                    foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                        if (f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return f;
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return null;
        }

        // 첫 줄 session_meta.payload.id
        private static string SessionIdOf(IReadOnlyList<string> lines)
        {
            foreach (var line in lines)
            {
                try
                {
                    var o = JObject.Parse(line);
                    if ((string)o["type"] == "session_meta")
                        return (string)o["payload"]?["id"];
                }
                catch { }
                return null; // 첫 줄만 검사(session_meta는 항상 첫 줄)
            }
            return null;
        }

        // ~/.codex/session_index.jsonl → { id: thread_name }
        private static Dictionary<string, string> LoadTitleIndex(string codexHome)
        {
            var map = new Dictionary<string, string>();
            var path = Path.Combine(codexHome, "session_index.jsonl");
            if (!File.Exists(path)) return map;
            try
            {
                foreach (var line in ReadAllLinesShared(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var o = JObject.Parse(line);
                        var id = (string)o["id"];
                        var name = (string)o["thread_name"];
                        if (!string.IsNullOrEmpty(id)) map[id] = name;
                    }
                    catch { }
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return map;
        }

        // 잠긴(쓰기 중) 파일도 읽도록 FileShare.ReadWrite.
        private static List<string> ReadAllLinesShared(string path)
        {
            var lines = new List<string>();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs))
            {
                string line;
                while ((line = sr.ReadLine()) != null) lines.Add(line);
            }
            return lines;
        }

        public static void Start(Action onChanged)
        {
            Stop(); // 재호출 시 기존 watcher/timer 누수 방지
            _onChanged = onChanged;
            if (!Available) return; // Codex 미설치 → 조용히 비활성(폴백 없음)
            try
            {
                if (Directory.Exists(WindowsSessionsRoot))
                {
                    _watcher = new FileSystemWatcher(WindowsSessionsRoot, "*.jsonl")
                    {
                        IncludeSubdirectories = true, // 날짜 폴더 중첩
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        EnableRaisingEvents = true
                    };
                    _watcher.Changed += OnFsEvent;
                    _watcher.Created += OnFsEvent;
                    _watcher.Renamed += OnFsEvent;
                    _watcher.Error += OnWatcherError;
                }

                _poll = new Timer(_ =>
                {
                    try { _onChanged?.Invoke(); }
                    catch (Exception ex) { LogService.Instance.Error(ex); }
                }, null, 5000, 5000);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static void OnWatcherError(object sender, ErrorEventArgs e)
        {
            try
            {
                LogService.Instance.Error(e.GetException());
                var cb = _onChanged;
                if (cb != null) Start(cb);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            lock (_debounceLock)
            {
                _debounce?.Dispose();
                _debounce = new Timer(_ =>
                {
                    try { _onChanged?.Invoke(); }
                    catch (Exception ex) { LogService.Instance.Error(ex); }
                }, null, 300, Timeout.Infinite);
            }
        }

        public static void Stop()
        {
            try
            {
                if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
                lock (_debounceLock)
                {
                    _debounce?.Dispose(); _debounce = null;
                }
                _poll?.Dispose(); _poll = null;
                _onChanged = null;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
