using System.Collections.Generic;
namespace AgentHub.Common.Models {
  /// <summary>아직 답변되지 않은 AskUserQuestion(첫 질문).</summary>
  public class PendingAsk {
    public string Question { get; set; }
    public string Header { get; set; }
    public bool MultiSelect { get; set; }
    public List<string> Options { get; set; }
  }
}
