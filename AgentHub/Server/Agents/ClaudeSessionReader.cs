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

        // sessionId -> 파일 경로 (최근 스캔 캐시)
        private static readonly ConcurrentDictionary<string, string> _paths =
            new ConcurrentDictionary<string, string>();

        private static string ProjectsRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");

        public static List<SessionSummary> ListSessions()
        {
            var root = ProjectsRoot;
            var result = new List<SessionSummary>();
            if (!Directory.Exists(root)) return result;

            var now = DateTime.UtcNow;
            var cutoff = now - Window;

            var files = new List<FileInfo>();
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                    foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl"))
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
                    var id = Path.GetFileNameWithoutExtension(fi.Name);
                    _paths[id] = fi.FullName;
                    var lines = ReadAllLinesShared(fi.FullName);
                    result.Add(TranscriptParser.Summarize(id, lines, now));
                }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }
            return result;
        }

        public static List<ActivityEvent> GetActivity(string sessionId, int max = 200)
        {
            if (!_paths.TryGetValue(sessionId, out var path) || !File.Exists(path))
            {
                path = FindSessionFile(sessionId);
                if (path == null) return new List<ActivityEvent>();
                _paths[sessionId] = path;
            }
            try
            {
                var lines = ReadAllLinesShared(path);
                return TranscriptParser.ParseEvents(lines, max);
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return new List<ActivityEvent>(); }
        }

        private static string FindSessionFile(string sessionId)
        {
            var root = ProjectsRoot;
            if (!Directory.Exists(root)) return null;
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var candidate = Path.Combine(dir, sessionId + ".jsonl");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
            return null;
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
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        private static void OnFsEvent(object sender, FileSystemEventArgs e)
        {
            // 300ms 디바운스 — 연속 쓰기 폭주 완화
            _debounce?.Dispose();
            _debounce = new Timer(_ =>
            {
                try { _onChanged?.Invoke(); }
                catch (Exception ex) { LogService.Instance.Error(ex); }
            }, null, 300, Timeout.Infinite);
        }

        public static void Stop()
        {
            try
            {
                if (_watcher != null) { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); _watcher = null; }
                _debounce?.Dispose(); _debounce = null;
                _onChanged = null;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }
    }
}
