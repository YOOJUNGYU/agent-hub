using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentHub.Server.Hook
{
    /// <summary>~/.claude/settings.json의 Notification 훅을 멱등 추가/제거/조회(순수, Newtonsoft만).</summary>
    public static class HookConfigMerger
    {
        public static bool IsInstalled(string json, string marker)
        {
            var arr = NotificationArray(Parse(json), create: false);
            return arr != null && arr.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string AddNotificationHook(string json, JObject hookEntry, string marker)
        {
            var root = Parse(json) ?? new JObject();
            var arr = NotificationArray(root, create: true);
            if (arr.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                arr.Add(hookEntry);
            return root.ToString(Formatting.Indented);
        }

        public static string RemoveNotificationHook(string json, string marker)
        {
            var root = Parse(json);
            var arr = NotificationArray(root, create: false);
            if (arr == null) return json;
            for (int i = arr.Count - 1; i >= 0; i--)
                if (arr[i].ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    arr.RemoveAt(i);
            return root.ToString(Formatting.Indented);
        }

        private static JArray NotificationArray(JObject root, bool create)
        {
            if (root == null) return null;
            var hooks = root["hooks"] as JObject;
            if (hooks == null) { if (!create) return null; hooks = new JObject(); root["hooks"] = hooks; }
            var arr = hooks["Notification"] as JArray;
            if (arr == null) { if (!create) return null; arr = new JArray(); hooks["Notification"] = arr; }
            return arr;
        }

        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); } catch { return null; }
        }
    }
}
