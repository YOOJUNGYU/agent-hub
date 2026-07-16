using System.Collections.Generic;
using AgentHub.Server.Agents;
using Xunit;
namespace AgentHub.Tests {
  public class PendingAskTests {
    [Fact] public void Extracts_unanswered_ask() {
      var lines = new List<string>{
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[{\"question\":\"어디로?\",\"header\":\"방향\",\"multiSelect\":false,\"options\":[{\"label\":\"A\",\"description\":\"a\"},{\"label\":\"B\",\"description\":\"b\"}]}]}}]}}"
      };
      var p = TranscriptParser.ExtractPendingAsk(lines);
      Assert.NotNull(p); Assert.Equal("어디로?", p.Question);
      Assert.Equal(2, p.Options.Count); Assert.Equal("A", p.Options[0]);
    }
    [Fact] public void Null_when_answered() {
      var lines = new List<string>{
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[{\"question\":\"q\",\"options\":[{\"label\":\"A\"}]}]}}]}}",
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"tu1\",\"content\":\"answered\"}]}}"
      };
      Assert.Null(TranscriptParser.ExtractPendingAsk(lines));
    }
    [Fact] public void Null_when_no_ask() {
      Assert.Null(TranscriptParser.ExtractPendingAsk(new List<string>{"{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"text\",\"text\":\"hi\"}]}}"}));
    }
    [Fact] public void QuestionCount_is_one_for_single_question() {
      var lines = new List<string>{
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[{\"question\":\"q1\",\"options\":[{\"label\":\"A\"}]}]}}]}}"
      };
      var p = TranscriptParser.ExtractPendingAsk(lines);
      Assert.NotNull(p); Assert.Equal(1, p.QuestionCount);
    }
    [Fact] public void QuestionCount_reflects_multi_question_ask() {
      var lines = new List<string>{
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tu1\",\"name\":\"AskUserQuestion\",\"input\":{\"questions\":[{\"question\":\"q1\",\"options\":[{\"label\":\"A\"}]},{\"question\":\"q2\",\"options\":[{\"label\":\"B\"}]},{\"question\":\"q3\",\"options\":[{\"label\":\"C\"}]}]}}]}}"
      };
      var p = TranscriptParser.ExtractPendingAsk(lines);
      Assert.NotNull(p); Assert.Equal("q1", p.Question); Assert.Equal(3, p.QuestionCount);
    }
  }
}
