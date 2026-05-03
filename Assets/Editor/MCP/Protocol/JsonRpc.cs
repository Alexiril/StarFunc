using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCP.Protocol
{
    public static class JsonRpcErrorCodes
    {
        public const int ParseError = -32700;
        public const int InvalidRequest = -32600;
        public const int MethodNotFound = -32601;
        public const int InvalidParams = -32602;
        public const int InternalError = -32603;
    }

    public class JsonRpcMessage
    {
        [JsonProperty("jsonrpc")] public string JsonRpc = "2.0";
        [JsonProperty("id", NullValueHandling = NullValueHandling.Include)] public JToken Id;
        [JsonProperty("method", NullValueHandling = NullValueHandling.Ignore)] public string Method;
        [JsonProperty("params", NullValueHandling = NullValueHandling.Ignore)] public JToken Params;
        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)] public JToken Result;
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public JsonRpcError Error;

        [JsonIgnore] public bool IsRequest => Method != null && Id != null && Id.Type != JTokenType.Null;
        [JsonIgnore] public bool IsNotification => Method != null && (Id == null || Id.Type == JTokenType.Null);
        [JsonIgnore] public bool IsResponse => Method == null && (Result != null || Error != null);

        public static JsonRpcMessage Success(JToken id, JToken result)
        {
            return new JsonRpcMessage { Id = id, Result = result ?? new JObject() };
        }

        public static JsonRpcMessage Failure(JToken id, int code, string message, JToken data = null)
        {
            return new JsonRpcMessage { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
        }
    }

    public class JsonRpcError
    {
        [JsonProperty("code")] public int Code;
        [JsonProperty("message")] public string Message;
        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)] public JToken Data;
    }

    public static class JsonRpcSerializer
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
        };

        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);
        public static JToken ParseToken(string s) => JToken.Parse(s);
    }
}
