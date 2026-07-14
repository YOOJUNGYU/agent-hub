using System;
using System.IO;
using System.Linq;

namespace AgentHub.Server.Terminal
{
    public abstract class EngineSpec
    {
        public abstract string Key { get; }
        public abstract string LaunchCommand();
        /// <summary>기존 세션 대화를 이어받아 대화형으로 실행하는 명령(모바일 터미널 attach용).</summary>
        public abstract string ResumeCommand(string sessionId);
        public abstract string ProjectDir(string cwd);

        // AskUserQuestion 메뉴 선택(커서 최상단 가정): Down×i + Enter. best-effort.
        // ESC [ B = Down 방향키의 실제 터미널 이스케이프 시퀀스.
        public static string AnswerKeystrokes(int optionIndex)
            => string.Concat(Enumerable.Repeat("\x1b[B", Math.Max(0, optionIndex))) + "\r";

        public static EngineSpec For(string key)
        {
            switch ((key ?? "claude").ToLowerInvariant())
            {
                case "codex": return new CodexEngine();
                case "claude": default: return new ClaudeEngine();
            }
        }
    }

    public sealed class ClaudeEngine : EngineSpec
    {
        public override string Key => "claude";
        public override string LaunchCommand() => "cmd.exe /c claude";
        // sessionId는 UUID(안전 문자)라 별도 이스케이프 불필요.
        public override string ResumeCommand(string sessionId) => "cmd.exe /c claude --resume " + sessionId;
        public override string ProjectDir(string cwd)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var enc = (cwd ?? "").Replace(':', '-').Replace('\\', '-').Replace('/', '-');
            return Path.Combine(home, ".claude", "projects", enc);
        }
    }

    public sealed class CodexEngine : EngineSpec
    {
        public override string Key => "codex";
        public override string LaunchCommand() => "cmd.exe /c codex";
        // sessionId는 UUID(안전 문자)라 별도 이스케이프 불필요. codex resume <UUID>로 그 세션을 이어받는다.
        public override string ResumeCommand(string sessionId) => "cmd.exe /c codex resume " + sessionId;
        // Codex 세션은 cwd별 폴더가 아니라 날짜 폴더에 저장된다(rollout-*.jsonl). 조회 루트만 반환.
        public override string ProjectDir(string cwd)
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
    }
}
