using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace AgentHub.Common.Util
{
    /// <summary>camelCase JSON 직렬화 헬퍼 (WebSocket 메시지 · API 응답 공통).</summary>
    public static class Json
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json);
    }
}
