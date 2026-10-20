using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using AgentHub.Common.Models;
using AgentHub.Common.Util;

namespace AgentHub.Server.Agents
{
    /// <summary>
    /// ~/.claude/projects 의 세션 트랜스크립트(JSONL)를 읽어 요약/상세를 제공하고,
    /// FileSystemWatcher로 변경을 감지해 콜백을 알린다. 파싱 로직은 TranscriptParser에 위임.
    /// </summary>
    public static class ClaudeSessionReader
    {
        private static readonly TimeSpan Window = TimeSpan.FromHours(24);
        private const int MaxSessions = 30;

        private static FileSystemWatcher _watcher;
        private static Action _onChanged;
        private static Timer _debounce;
        private static readonly object _debounceLock = new object();
        private static Timer _poll;

        // sessionId -> 파일 경로 (최근 스캔 캐시)
        // 트랜스크립트별 증분 요약: 읽은 바이트 위치까지 접어 둔 상태. 파일이 자라면 새 줄만 읽는다 —
        // 수십 MB 트랜스크립트를 9p(\\wsl.localhost)로 변경마다 통째로 읽으면 목록 한 번에 수십 초가 걸린다.
        private sealed class Parsed
        {
            public long Offset;
            public TranscriptParser.SummaryFold Fold = new TranscriptParser.SummaryFold();
        }

        private static readonly ConcurrentDictionary<string, Parsed> _parsed = new ConcurrentDictionary<string, Parsed>();

        private static readonly ConcurrentDictionary<string, string> _paths =
            new ConcurrentDictionary<string, string>();

        private static string ProjectsRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        /// <summary>
        /// 스캔할 트랜스크립트 루트: Windows 사용자 홈 + 실행 중인 WSL 배포판의 홈.
        /// WSL에서 실행된 Claude는 배포판 안에 기록하므로 이 루트를 함께 봐야 세션이 목록에 나온다.
        /// </summary>
        private static IEnumerable<string> ProjectsRoots()
        {
            yield return ProjectsRoot;
            foreach (var home in Wsl.ClaudeHomes())
                yield return Path.Combine(home.ClaudeDir, "projects");
        }

        public static List<SessionSummary> ListSessions()
        {
            var result = new List<SessionSummary>();

            var now = DateTime.UtcNow;
            var cutoff = now - Window;

            var files = new List<FileInfo>();
            foreach (var root in ProjectsRoots())
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                        // DirectoryInfo 열거는 목록 조회 결과에 시각·크기가 실려 온다 — 파일마다 9p stat을 따로 하지 않는다.
                        foreach (var fi in new DirectoryInfo(dir).EnumerateFiles("*.jsonl"))
                            if (fi.LastWriteTimeUtc >= cutoff) files.Add(fi);
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }

            foreach (var fi in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxSessions))
            {
                try
                {
                    var id = Path.GetFileNameWithoutExtension(fi.Name);
                    _paths[id] = fi.FullName;
                    result.Add(SummaryOf(id, fi, now));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return result;
        }

        /// <param name="tailBytes">0이면 파일 전체, 아니면 끝에서 이만큼만 읽는다(오피스 실시간 갱신용 — 큰 트랜스크립트를 매번 통째로 읽지 않게).</param>
        public static List<ActivityEvent> GetActivity(string sessionId, int max = 200, int tailBytes = 0)
        {
            if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
            {
                path = FindSessionFile(sessionId);
                if (path == null) return new List<ActivityEvent>();
                _paths[sessionId] = path;
            }
            try
            {
                var lines = tailBytes > 0 ? ReadTailLines(path, tailBytes) : ReadAllLinesShared(path);
                return TranscriptParser.ParseEvents(lines, max);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return new List<ActivityEvent>(); }
        }

        /// <summary>세션의 작업 디렉터리(cwd)를 트랜스크립트에서 조회. resume 실행용. 실패 시 null.</summary>
        public static string CwdOf(string sessionId)
        {
            try
            {
                if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
                {
                    path = FindSessionFile(sessionId);
                    if (path == null) return null;
                    _paths[sessionId] = path;
                }
                var lines = ReadAllLinesShared(path);
                return TranscriptParser.Summarize(sessionId, lines, DateTime.UtcNow).Cwd;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }

        /// <summary>세션 제목을 트랜스크립트에서 조회(알림 표시용). 실제 제목이 없으면(sessionId 폴백) null.</summary>
        public static string TitleOf(string sessionId)
        {
            try
            {
                if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
                {
                    path = FindSessionFile(sessionId);
                    if (path == null) return null;
                    _paths[sessionId] = path;
                }
                var lines = ReadAllLinesShared(path);
                var s = TranscriptParser.Summarize(sessionId, lines, DateTime.UtcNow);
                // Summarize는 제목이 없으면 sessionId로 폴백 → 그 경우 '제목 없음'으로 취급.
                return s.Title == sessionId ? null : s.Title;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }

        /// <summary>세션의 마지막 어시스턴트 텍스트(알림·답장 카드 표시용). 실패 시 null.</summary>
        public static string LastAssistantTextOf(string sessionId)
        {
            try
            {
                if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
                {
                    path = FindSessionFile(sessionId);
                    if (path == null) return null;
                    _paths[sessionId] = path;
                }
                return TranscriptParser.LastAssistantText(ReadAllLinesShared(path));
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return null; }
        }

        private static string FindSessionFile(string sessionId)
        {
            foreach (var root in ProjectsRoots())
            {
                if (!Directory.Exists(root)) continue;
                try
                {
                    foreach (var dir in Directory.EnumerateDirectories(root))
                    {
                        var candidate = Path.Combine(dir, sessionId + ".jsonl");
                        if (File.Exists(candidate)) return candidate;
                    }
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return null;
        }

        // 잠긴(쓰기 중) 파일도 읽도록 FileShare.ReadWrite.
        /// <summary>오피스: 세션이 띄운 서브에이전트 1건(세션폴더/subagents/agent-*.jsonl + .meta.json).</summary>
        public class SubagentInfo
        {
            public string ToolUseId;    // 부모 세션의 Task/Agent tool_use id
            public string Description;
            public string AgentType;
            public List<ActivityEvent> Events;
        }

        private static readonly TimeSpan SubagentStale = TimeSpan.FromMinutes(10);

        /// <summary>아직 일하는 서브에이전트들과 각자의 최근 활동. 끝났거나(meta.finished) 한동안 조용한 것은 뺀다.</summary>
        public static List<SubagentInfo> GetSubagents(string sessionId, int max, int tailBytes)
        {
            var list = new List<SubagentInfo>();
            try
            {
                if (!_paths.TryGetValue(sessionId, out var path)) path = FindSessionFile(sessionId);
                if (path == null) return list;
                var dir = Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path), "subagents");
                if (!Directory.Exists(dir)) return list;
                foreach (var metaPath in Directory.GetFiles(dir, "*.meta.json"))
                {
                    var jsonl = metaPath.Substring(0, metaPath.Length - ".meta.json".Length) + ".jsonl";
                    if (!File.Exists(jsonl) || DateTime.UtcNow - File.GetLastWriteTimeUtc(jsonl) > SubagentStale) continue;
                    var meta = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(metaPath));
                    if (meta.Value<bool?>("finished") == true) continue;
                    list.Add(new SubagentInfo
                    {
                        ToolUseId = meta.Value<string>("toolUseId"),
                        Description = meta.Value<string>("description"),
                        AgentType = meta.Value<string>("agentType"),
                        Events = TranscriptParser.ParseEvents(ReadTailLines(jsonl, tailBytes), max)
                    });
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return list;
        }

        /// <summary>파일 끝 maxBytes 안의 완전한 줄들(첫 줄은 중간부터 잘렸을 수 있어 버린다). 쓰기 중인 파일도 공유 읽기.</summary>
        public static List<string> ReadTailLines(string path, int maxBytes)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var start = Math.Max(0, fs.Length - maxBytes);
                fs.Seek(start, SeekOrigin.Begin);
                var lines = new List<string>();
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null) lines.Add(line);
                }
                if (start > 0 && lines.Count > 0) lines.RemoveAt(0);
                return lines;
            }
        }

        private static SessionSummary SummaryOf(string id, FileInfo fi, DateTime now)
        {
            var p = _parsed.GetOrAdd(fi.FullName, _ => new Parsed());
            lock (p)
            {
                if (fi.Length < p.Offset) { p.Offset = 0; p.Fold = new TranscriptParser.SummaryFold(); } // 새로 쓰인 파일
                if (fi.Length > p.Offset) p.Offset = FeedNewLines(fi.FullName, p.Offset, p.Fold);
                var s = p.Fold.Build(id, now);
                s.Engine = "claude";
                s.PendingAsk = p.Fold.PendingAsk();
                return s;
            }
        }

        /// <summary>offset 이후의 완성된 줄(개행으로 끝난)만 접고, 다음에 읽을 위치를 돌려준다.</summary>
        private static long FeedNewLines(string path, long offset, TranscriptParser.SummaryFold fold)
        {
            byte[] buf;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (fs.Length <= offset) return offset;
                fs.Seek(offset, SeekOrigin.Begin);
                buf = new byte[fs.Length - offset];
                int read = 0, n;
                while (read < buf.Length && (n = fs.Read(buf, read, buf.Length - read)) > 0) read += n;
                if (read < buf.Length) Array.Resize(ref buf, read);
            }
            var end = Array.LastIndexOf(buf, (byte)'\n');
            if (end < 0) return offset; // 아직 쓰는 중인 줄
            foreach (var line in System.Text.Encoding.UTF8.GetString(buf, 0, end + 1).Split('\n'))
                fold.Feed(line.TrimEnd('\r'));
            return offset + end + 1;
        }

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
            var root = ProjectsRoot;
            try
            {
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                _watcher = new FileSystemWatcher(root, "*.jsonl")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                _watcher.Changed += OnFsEvent;
                _watcher.Created += OnFsEvent;
                _watcher.Renamed += OnFsEvent;
                _watcher.Error += OnWatcherError;

                // watcher는 Windows 홈만 감시한다(WSL의 \\wsl.localhost 경로는 FileSystemWatcher가 못 붙음).
                // WSL 세션 갱신은 아래 폴링(5초)이 담당한다.
                // watcher 이벤트 유실 대비 저빈도 폴링 폴백(5초)
                _poll = new Timer(_ =>
                {
                    try { _onChanged?.Invoke(); }
                    catch (Exception ex) { LogService.Instance.Error(ex); }
                }, null, 5000, 5000);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        // watcher가 죽거나 버퍼 오버플로 시 재무장.
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
            // 300ms 디바운스 — 연속 쓰기 폭주 완화
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
