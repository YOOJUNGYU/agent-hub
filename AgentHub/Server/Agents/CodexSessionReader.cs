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
        private const int MaxSessions = 30;

        private static FileSystemWatcher _watcher;
        private static Action _onChanged;
        private static Timer _debounce;
        private static readonly object _debounceLock = new object();
        private static Timer _poll;

        // sessionId -> 파일 경로 (최근 스캔 캐시)
        private static readonly ConcurrentDictionary<string, string> _paths =
            new ConcurrentDictionary<string, string>();

        private static string CodexHome =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

        private static string SessionsRoot => Path.Combine(CodexHome, "sessions");

        /// <summary>Codex가 설치돼 세션 폴더가 존재하는지(없으면 조용히 비활성).</summary>
        public static bool Available => Directory.Exists(SessionsRoot);

        /// <summary>이 sessionId가 Codex 세션인지(엔진 라우팅용).</summary>
        public static bool Has(string sessionId) => FindSessionFile(sessionId) != null;

        public static List<SessionSummary> ListSessions()
        {
            var root = SessionsRoot;
            var result = new List<SessionSummary>();
            if (!Directory.Exists(root)) return result;

            var now = DateTime.UtcNow;
            var cutoff = now - Window;
            var titles = LoadTitleIndex();

            var files = new List<FileInfo>();
            try
            {
                foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(f);
                    if (fi.LastWriteTimeUtc >= cutoff) files.Add(fi);
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }

            foreach (var fi in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxSessions))
            {
                try
                {
                    var lines = ReadAllLinesShared(fi.FullName);
                    var id = SessionIdOf(lines) ?? Path.GetFileNameWithoutExtension(fi.Name);
                    _paths[id] = fi.FullName;
                    var s = CodexTranscriptParser.Summarize(id, lines, now);
                    if (titles.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t)) s.Title = t;
                    result.Add(s);
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return result;
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
                var titles = LoadTitleIndex();
                if (titles.TryGetValue(sessionId, out var t) && !string.IsNullOrWhiteSpace(t)) return t;
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
            var root = SessionsRoot;
            if (!Directory.Exists(root)) return null;
            try
            {
                var suffix = sessionId + ".jsonl";
                foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories))
                    if (f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return f;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
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
        private static Dictionary<string, string> LoadTitleIndex()
        {
            var map = new Dictionary<string, string>();
            var path = Path.Combine(CodexHome, "session_index.jsonl");
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
            var root = SessionsRoot;
            if (!Directory.Exists(root)) return; // Codex 미설치 → 조용히 비활성(폴백 없음)
            try
            {
                _watcher = new FileSystemWatcher(root, "*.jsonl")
                {
                    IncludeSubdirectories = true, // 날짜 폴더 중첩
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFsEvent;
                _watcher.Created += OnFsEvent;
                _watcher.Renamed += OnFsEvent;
                _watcher.Error += OnWatcherError;

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
