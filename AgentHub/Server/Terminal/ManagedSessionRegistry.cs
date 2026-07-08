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
        // 상관 완료 여부와 무관하게 생성된 모든 Entry를 추적한다(DisposeAll이 미상관 세션도 정리하도록).
        private static readonly ConcurrentDictionary<Entry, byte> _all = new ConcurrentDictionary<Entry, byte>();

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
            _all[e] = 0;
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
            if (string.IsNullOrEmpty(sessionId) || text == null) return false;
            // 관리 세션: 우리가 소유한 PTY stdin에 직접 기록.
            if (ById.TryGetValue(sessionId, out var e))
            {
                try { e.Pty.Write(Encoding.UTF8.GetBytes(text + "\r")); return true; }
                catch (Exception ex) { LogService.Instance.Error(ex); return false; }
            }
            // 비관리(외부) 세션: headless resume로 프롬프트 이어붙이기 시도.
            // 주의: 그 세션이 다른 곳에서 라이브면 트랜스크립트가 뒤섞일 수 있음(사용자 선택).
            return ResumePrompt(sessionId, text);
        }

        private static bool ResumePrompt(string sessionId, string text)
        {
            try
            {
                var cwd = AgentHub.Server.Agents.ClaudeSessionReader.CwdOf(sessionId);
                if (string.IsNullOrEmpty(cwd) || !Directory.Exists(cwd)) return false;
                // claude -p --resume <id> "<text>" — 해당 cwd에서 headless 실행 후 종료(대화에 추가됨).
                var args = "/c claude -p --resume " + sessionId + " \"" + text.Replace("\"", "\\\"") + "\"";
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", args)
                {
                    WorkingDirectory = cwd,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var p = System.Diagnostics.Process.Start(psi);
                if (p != null)
                {
                    // fire-and-forget: 출력 버퍼가 막히지 않도록 비동기로 흘려보냄.
                    p.OutputDataReceived += (s, ev) => { };
                    p.ErrorDataReceived += (s, ev) => { };
                    try { p.BeginOutputReadLine(); p.BeginErrorReadLine(); } catch { }
                }
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static bool Answer(string sessionId, int optionIndex)
        {
            if (!ById.TryGetValue(sessionId, out var e)) return false;
            try
            {
                e.Pty.Write(Encoding.UTF8.GetBytes(EngineSpec.AnswerKeystrokes(optionIndex)));
                return true;
            }
            catch (Exception ex) { LogService.Instance.Error(ex); return false; }
        }

        public static void DisposeAll()
        {
            foreach (var kv in _all) { try { kv.Key.Pty.Dispose(); } catch { } }
            _all.Clear();
            ById.Clear();
        }
    }
}
