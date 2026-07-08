using AgentHub.Server.Hook;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AgentHub.Tests
{
    public class HookConfigMergerTests
    {
        private static JObject Entry() => new JObject
        {
            ["matcher"] = "",
            ["hooks"] = new JArray { new JObject
            {
                ["type"] = "command",
                ["command"] = "C:/n/node.exe",
                ["args"] = new JArray { "C:/app/hook/agenthub-hook.js" },
                ["async"] = true,
                ["timeout"] = 5
            }}
        };

        // clawd 항목이 이미 있는 기존 settings
        private const string Existing = "{\"hooks\":{\"Notification\":[{\"matcher\":\"\",\"hooks\":[{\"type\":\"command\",\"command\":\"clawd-hook.js\"}]}]}}";

        [Fact]
        public void Add_is_idempotent_and_preserves_existing()
        {
            var once = HookConfigMerger.AddNotificationHook(Existing, Entry(), "agenthub-hook.js");
            Assert.Contains("clawd-hook.js", once);            // 기존 보존
            Assert.Contains("agenthub-hook.js", once);          // 우리 것 추가
            var twice = HookConfigMerger.AddNotificationHook(once, Entry(), "agenthub-hook.js");
            var arr = (JArray)JObject.Parse(twice)["hooks"]["Notification"];
            Assert.Equal(2, arr.Count);                         // 중복 추가 안 됨(clawd 1 + 우리 1)
        }

        [Fact]
        public void Add_creates_structure_from_empty()
        {
            var res = HookConfigMerger.AddNotificationHook("{}", Entry(), "agenthub-hook.js");
            Assert.True(HookConfigMerger.IsInstalled(res, "agenthub-hook.js"));
        }

        [Fact]
        public void Remove_removes_only_ours()
        {
            var added = HookConfigMerger.AddNotificationHook(Existing, Entry(), "agenthub-hook.js");
            var removed = HookConfigMerger.RemoveNotificationHook(added, "agenthub-hook.js");
            Assert.DoesNotContain("agenthub-hook.js", removed);
            Assert.Contains("clawd-hook.js", removed);          // clawd 보존
        }

        [Fact]
        public void IsInstalled_false_on_empty_or_broken()
        {
            Assert.False(HookConfigMerger.IsInstalled("", "agenthub-hook.js"));
            Assert.False(HookConfigMerger.IsInstalled("not json", "agenthub-hook.js"));
            Assert.False(HookConfigMerger.IsInstalled("{}", "agenthub-hook.js"));
        }

        [Fact]
        public void PreToolUse_and_Notification_coexist_and_are_idempotent()
        {
            var withNotify = HookConfigMerger.AddHook("{}", "Notification", Entry(), "agenthub-hook.js");
            var both = HookConfigMerger.AddHook(withNotify, "PreToolUse", Entry(), "agenthub-hook.js");
            Assert.True(HookConfigMerger.IsInstalled(both, "Notification", "agenthub-hook.js"));
            Assert.True(HookConfigMerger.IsInstalled(both, "PreToolUse", "agenthub-hook.js"));

            var again = HookConfigMerger.AddHook(both, "PreToolUse", Entry(), "agenthub-hook.js");
            var arr = (JArray)JObject.Parse(again)["hooks"]["PreToolUse"];
            Assert.Single(arr); // 중복 추가 안 됨

            var removed = HookConfigMerger.RemoveHook(again, "PreToolUse", "agenthub-hook.js");
            Assert.False(HookConfigMerger.IsInstalled(removed, "PreToolUse", "agenthub-hook.js"));
            Assert.True(HookConfigMerger.IsInstalled(removed, "Notification", "agenthub-hook.js")); // Notification 보존
        }
    }
}
