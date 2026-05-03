using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MCP.Protocol
{
    public static class McpHandler
    {
        public const string ProtocolVersion = "2025-03-26";
        public const string ServerName = "unity-editor-mcp";
        public const string ServerVersion = "0.1.0";

        public static async Task<JsonRpcMessage> Handle(JsonRpcMessage req)
        {
            try
            {
                switch (req.Method)
                {
                    case "initialize":
                        return JsonRpcMessage.Success(req.Id, BuildInitializeResult());

                    case "ping":
                        return JsonRpcMessage.Success(req.Id, new JObject());

                    case "tools/list":
                        return JsonRpcMessage.Success(req.Id, BuildToolsList());

                    case "tools/call":
                        return await HandleToolsCall(req);

                    default:
                        return JsonRpcMessage.Failure(req.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown method: {req.Method}");
                }
            }
            catch (Exception e)
            {
                return JsonRpcMessage.Failure(req.Id, JsonRpcErrorCodes.InternalError, e.Message, JToken.FromObject(e.ToString()));
            }
        }

        static JObject BuildInitializeResult()
        {
            return new JObject
            {
                ["protocolVersion"] = ProtocolVersion,
                ["capabilities"] = new JObject
                {
                    ["tools"] = new JObject { ["listChanged"] = false }
                },
                ["serverInfo"] = new JObject
                {
                    ["name"] = ServerName,
                    ["version"] = ServerVersion
                }
            };
        }

        static JObject BuildToolsList()
        {
            var arr = new JArray();
            foreach (var t in ToolRegistry.All)
            {
                arr.Add(new JObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description ?? "",
                    ["inputSchema"] = t.InputSchema ?? new JObject { ["type"] = "object" }
                });
            }
            return new JObject { ["tools"] = arr };
        }

        static async Task<JsonRpcMessage> HandleToolsCall(JsonRpcMessage req)
        {
            var p = req.Params as JObject ?? new JObject();
            var name = (string)p["name"];
            var args = p["arguments"] as JObject ?? new JObject();

            if (string.IsNullOrEmpty(name))
                return JsonRpcMessage.Failure(req.Id, JsonRpcErrorCodes.InvalidParams, "Missing tool name");

            if (!ToolRegistry.TryGet(name, out var tool))
                return JsonRpcMessage.Failure(req.Id, JsonRpcErrorCodes.MethodNotFound, $"Unknown tool: {name}");

            try
            {
                var result = await tool.Handler(args);
                var content = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = result?.ToString(Newtonsoft.Json.Formatting.None) ?? "null"
                    }
                };
                return JsonRpcMessage.Success(req.Id, new JObject
                {
                    ["content"] = content,
                    ["structuredContent"] = result ?? JValue.CreateNull(),
                    ["isError"] = false
                });
            }
            catch (Exception e)
            {
                var content = new JArray
                {
                    new JObject { ["type"] = "text", ["text"] = e.Message }
                };
                return JsonRpcMessage.Success(req.Id, new JObject
                {
                    ["content"] = content,
                    ["isError"] = true
                });
            }
        }
    }
}
