using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AgentHub.Server.Hook
{
    /// <summary>~/.claude/settings.json의 훅(Notification·PreToolUse 등)을 이벤트별로 멱등 추가/제거/조회(순수, Newtonsoft만).</summary>
    public static class HookConfigMerger
    {
        // 이벤트 지정 오버로드 ------------------------------------------------
        public static bool IsInstalled(string json, string eventName, string marker)
        {
            var arr = EventArray(Parse(json), eventName, create: false);
            return arr != null && arr.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string AddHook(string json, string eventName, JObject hookEntry, string marker)
        {
            var root = Parse(json) ?? new JObject();
            var arr = EventArray(root, eventName, create: true);
            if (arr.ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
                arr.Add(hookEntry);
            return root.ToString(Formatting.Indented);
        }

        public static string RemoveHook(string json, string eventName, string marker)
        {
            var root = Parse(json);
            var arr = EventArray(root, eventName, create: false);
            if (arr == null) return json;
            for (int i = arr.Count - 1; i >= 0; i--)
                if (arr[i].ToString().IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    arr.RemoveAt(i);
            return root.ToString(Formatting.Indented);
        }

        // 하위호환 Notification 래퍼 -----------------------------------------
        public static bool IsInstalled(string json, string marker) => IsInstalled(json, "Notification", marker);
        public static string AddNotificationHook(string json, JObject hookEntry, string marker) => AddHook(json, "Notification", hookEntry, marker);
        public static string RemoveNotificationHook(string json, string marker) => RemoveHook(json, "Notification", marker);

        private static JArray EventArray(JObject root, string eventName, bool create)
        {
            if (root == null) return null;
            var hooks = root["hooks"] as JObject;
            if (hooks == null) { if (!create) return null; hooks = new JObject(); root["hooks"] = hooks; }
            var arr = hooks[eventName] as JArray;
            if (arr == null) { if (!create) return null; arr = new JArray(); hooks[eventName] = arr; }
            return arr;
        }

        private static JObject Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JObject.Parse(json); } catch { return null; }
        }
    }
}
