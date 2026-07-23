namespace AgentHub.Common.Models {
  /// <summary>"ask"로 폴백돼 터미널 프롬프트에서 대기 중인 권한 요청(콘솔 주입 대상).</summary>
  public class PendingPermission {
    public string Tool { get; set; }
    public string Detail { get; set; }
  }
}
