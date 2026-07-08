using System;
using System.IO;
using System.Linq;

namespace AgentHub.Server.Terminal
{
    public abstract class EngineSpec
    {
        public abstract string Key { get; }
        public abstract string LaunchCommand();
        public abstract string ProjectDir(string cwd);

        // AskUserQuestion 메뉴 선택(커서 최상단 가정): Down×i + Enter. best-effort.
        public static string AnswerKeystrokes(int optionIndex)
            => string.Concat(Enumerable.Repeat("[B", Math.Max(0, optionIndex))) + "\r";

        public static EngineSpec For(string key)
        {
            switch ((key ?? "claude").ToLowerInvariant())
            {
                case "claude": default: return new ClaudeEngine();
            }
        }
    }

    public sealed class ClaudeEngine : EngineSpec
    {
        public override string Key => "claude";
        public override string LaunchCommand() => "cmd.exe /c claude";
        public override string ProjectDir(string cwd)
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var enc = (cwd ?? "").Replace(':', '-').Replace('\\', '-').Replace('/', '-');
            return Path.Combine(home, ".claude", "projects", enc);
        }
    }
}
