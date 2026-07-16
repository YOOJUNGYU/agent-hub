using System.Collections.Generic;
namespace AgentHub.Common.Models {
  /// <summary>아직 답변되지 않은 AskUserQuestion(첫 질문).</summary>
  public class PendingAsk {
    public string Question { get; set; }
    public string Header { get; set; }
    public bool MultiSelect { get; set; }
    public List<string> Options { get; set; }
    public int QuestionCount { get; set; }  // 이 AskUserQuestion의 총 문항 수(1이면 단일, >1이면 다문항)
  }
}
