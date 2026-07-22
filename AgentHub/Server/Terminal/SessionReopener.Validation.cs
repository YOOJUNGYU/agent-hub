using System.Text.RegularExpressions;

namespace AgentHub.Server.Terminal
{
    public static partial class SessionReopener
    {
        private static readonly Regex SessionIdPattern = new Regex("^[0-9a-fA-F-]{8,64}$", RegexOptions.Compiled);

        /// <summary>커맨드 주입 방지: 세션 id는 16진수/하이픈만 허용.</summary>
        public static bool IsValidSessionId(string id) => !string.IsNullOrEmpty(id) && SessionIdPattern.IsMatch(id);
    }
}
