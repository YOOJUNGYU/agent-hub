namespace AgentHub.Common.Models
{
    /// <summary>기기 상태 문자열(프론트엔드와 일치).</summary>
    public static class DeviceStatus
    {
        public const string None = "none";
        public const string Pending = "pending";
        public const string Approved = "approved";
        public const string Revoked = "revoked";
    }

    /// <summary>등록/승인 대상 기기(영속). TokenHash는 비밀 — 클라이언트에 전송하지 않는다.</summary>
    public class Device
    {
        public string Id { get; set; }         // 공개 GUID (승인/삭제 대상 지정)
        public string TokenHash { get; set; }  // 토큰 SHA-256 (비밀)
        public string Name { get; set; }
        public string Ip { get; set; }
        public string UserAgent { get; set; }
        public string Status { get; set; }     // none/pending/approved/revoked
        public string RequestedAt { get; set; }
        public string ApprovedAt { get; set; }
        public string LastSeenAt { get; set; }
    }

    /// <summary>콘솔/응답에 노출하는 안전한 투영(토큰/해시 제외).</summary>
    public class DeviceView
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Ip { get; set; }
        public string UserAgent { get; set; }
        public string Status { get; set; }
        public string RequestedAt { get; set; }
        public string ApprovedAt { get; set; }
        public string LastSeenAt { get; set; }
    }

    /// <summary>POST /api/devices/request 요청 본문.</summary>
    public class DeviceRequestBody
    {
        public string Name { get; set; }
    }
}
