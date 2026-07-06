namespace AgentHub.Common.Models
{
    /// <summary>EmbedIO 서버 활성 상태 및 접속 정보.</summary>
    public class ServerStatusInfo
    {
        public bool Active { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Url { get; set; }
    }
}
