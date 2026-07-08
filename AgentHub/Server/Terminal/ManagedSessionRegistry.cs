using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using AgentHub.Common.Util;

namespace AgentHub.Server.Terminal
{
    /// <summary>Agent Hub가 실행·소유한 엔진 세션(PTY) 레지스트리. sessionId↔ConPtySession 상관.</summary>
    public static class ManagedSessionRegistry
    {
        private class Entry { public ConPtySession Pty; public string Cwd; public EngineSpec Engine; public volatile string SessionId; }
        private static readonly ConcurrentDictionary<string, Entry> ById = new ConcurrentDictionary<string, Entry>();
        private static readonly ConcurrentBag<Entry> Pending = new ConcurrentBag<Entry>();

        public static bool IsManaged(string sessionId)
            => !string.IsNullOrEmpty(sessionId) && ById.ContainsKey(sessionId);

        public static string Start(string engineKey, string cwd)
        {
            var engine = EngineSpec.For(engineKey);
            if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd))
                throw new InvalidOperationException("유효한 폴더가 아닙니다: " + cwd);
            var launchedAt = DateTime.UtcNow;
            var e = new Entry { Cwd = cwd, Engine = engine };
            e.Pty = new ConPtySession(engine.LaunchCommand(), cwd, (short)120, (short)40, (buf, n) => { /* 출력은 트랜스크립트 tail로 표시 */ });
            Pending.Add(e);
            // 상관: 새 트랜스크립트의 sessionId 확정 (백그라운드, 최대 ~30초로 제한)
            var t = new Thread(() => Correlate(e, launchedAt)) { IsBackground = true, Name = "ManagedSession-correlate" };
            t.Start();
            return "starting"; // sessionId는 상관 완료 후 IsManaged로 노출
        }

        private static void Correlate(Entry e, DateTime after)
        {
            try
            {
                var dir = e.Engine.ProjectDir(e.Cwd);
                for (int i = 0; i < 60 && e.SessionId == null; i++) // 최대 ~30초
                {
                    Thread.Sleep(500);
                    if (!Directory.Exists(dir)) continue;
                    var f = new DirectoryInfo(dir).GetFiles("*.jsonl")
                        .Where(x => x.LastWriteTimeUtc >= after.AddSeconds(-2))
                        .OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                    if (f != null)
                    {
                        var id = Path.GetFileNameWithoutExtension(f.Name);
                        e.SessionId = id; ById[id] = e;
                    }
                }
            }
            catch (Exception ex) { LogService.Instance.Error(ex); }
        }

        public static bool Prompt(string sessionId, string text)
        {
            if (!ById.TryGetValue(sessionId, out var e) || text == null) return false;
            try { e.Pty.Write(Encoding.UTF8.GetBytes(text + "\r")); return true; }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Answer(string sessionId, int optionIndex)
        {
            if (!ById.TryGetValue(sessionId, out var e)) return false;
            try
            {
                // EngineSpec.AnswerKeystrokes는 ESC(0x1B) 없이 "[B"(Down)만 반환한다.
                // 실제 터미널 이스케이프 시퀀스(ESC [ B = Down 방향키)로 변환해 전송한다.
                var body = EngineSpec.AnswerKeystrokes(optionIndex).Replace("[B", "\x1b[B");
                e.Pty.Write(Encoding.UTF8.GetBytes(body));
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static void DisposeAll()
        {
            foreach (var kv in ById) { try { kv.Value.Pty.Dispose(); } catch { } }
            ById.Clear();
        }
    }
}
