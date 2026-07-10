namespace AgentHub.Common.Models
{
    /// <summary>EmbedIO 서버 활성 상태 및 접속 정보.</summary>
    public class ServerStatusInfo
    {
        public bool Active { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Url { get; set; }
        public int CertHttpPort { get; set; } // 인증서(.crt) 평문 HTTP 부트스트랩 포트(삭제/만료 후 재설치용). 0=비활성.
    }
}
