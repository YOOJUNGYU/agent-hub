using System.Collections.Generic;

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
        public string Lang { get; set; } // 앱(agent-hub.exe) 표시 언어("ko"|"en"). PWA가 폰 로케일 대신 이 값을 따라간다.
        // 콘솔 상단에 label과 함께 표시할 접속 경로 목록(사설망 LAN + NetBird/Tailscale 등 VPN). 첫 항목이 Url과 동일.
        public List<EndpointInfo> Endpoints { get; set; }
    }

    /// <summary>콘솔에 표시할 접속 경로 하나. Kind로 클라이언트가 label을 i18n 매핑한다("lan"|"netbird"|"tailscale"|"vpn").</summary>
    public class EndpointInfo
    {
        public string Url { get; set; }
        public string Kind { get; set; }
    }
}
