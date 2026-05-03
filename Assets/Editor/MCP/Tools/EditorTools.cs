using System;
using System.Threading.Tasks;
using MCP.Protocol;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class EditorTools
    {
        static EditorTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "editor_wait_until_idle",
                Description = "Block until the editor finishes any compile or AssetDatabase update. Returns when both EditorApplication.isCompiling and isUpdating are false. Useful after asset_refresh, large imports, or lightmap bakes to avoid hitting Unity mid-update. timeoutMs default 30000, capped at 60000. NOTE: a domain reload kills the HTTP listener mid-call — for post-recompile sync, the client should poll scripts_status separately. This tool is for non-reload waits.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["timeoutMs"] = new JObject { ["type"] = "integer", ["default"] = 30000, ["minimum"] = 100, ["maximum"] = 60000 }
                    }
                },
                Handler = WaitUntilIdle
            });
        }

        static async Task<JToken> WaitUntilIdle(JObject args)
        {
            int timeoutMs = args["timeoutMs"] != null ? (int)args["timeoutMs"] : 30000;
            if (timeoutMs < 100) timeoutMs = 100;
            if (timeoutMs > 60000) timeoutMs = 60000;
            var start = DateTime.UtcNow;

            try
            {
                return await MainThreadDispatcher.WaitUntil(
                    ready: IsIdle,
                    result: () => MakeResult(true, start),
                    timeout: TimeSpan.FromMilliseconds(timeoutMs));
            }
            catch (TimeoutException)
            {
                return await MainThreadDispatcher.Run(() => MakeResult(false, start));
            }
        }

        static bool IsIdle() => !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        static JToken MakeResult(bool wasIdle, DateTime start)
        {
            return new JObject
            {
                ["ok"] = wasIdle,
                ["timedOut"] = !wasIdle,
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["elapsedMs"] = (int)(DateTime.UtcNow - start).TotalMilliseconds
            };
        }
    }
}
