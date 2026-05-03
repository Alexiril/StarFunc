using MCP.Protocol;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Compilation;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class ScriptTools
    {
        static ScriptTools()
        {
            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scripts_recompile",
                Description = "Request a script recompilation. Returns immediately; the actual compile + domain reload happens shortly after. IMPORTANT: the HTTP listener will be UNREACHABLE for ~30-60s (longer with cleanBuild=true or many scripts) during the domain reload — connection failures during that window are EXPECTED, not crashes or network problems. Wait at least 15s before retrying, treat ~90s with no response as the threshold for assuming a real problem. The session id persists across the reload. Once the listener responds again, poll scripts_status (≥5s spacing) until isCompiling=false, then continue with the same session. Use cleanBuild=true to force a full rebuild even when nothing has changed.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["cleanBuild"] = new JObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Clear the script build cache to force a full rebuild." },
                        ["reason"] = new JObject { ["type"] = "string", ["description"] = "Optional reason string (shown in Unity's compile log)." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => Recompile(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "scripts_status",
                Description = "Report current script compilation state: whether Unity is currently compiling, and the assemblies known to the compilation pipeline.",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = args => MainThreadDispatcher.Run(() => Status())
            });
        }

        static JToken Recompile(JObject args)
        {
            bool cleanBuild = args["cleanBuild"] != null && (bool)args["cleanBuild"];
            var reason = (string)args["reason"] ?? "MCP scripts_recompile";
            var options = cleanBuild
                ? RequestScriptCompilationOptions.CleanBuildCache
                : RequestScriptCompilationOptions.None;

            CompilationPipeline.RequestScriptCompilation(options);

            return new JObject
            {
                ["ok"] = true,
                ["queued"] = true,
                ["cleanBuild"] = cleanBuild,
                ["reason"] = reason,
                ["expectedReconnectAfterMs"] = new JObject
                {
                    ["min"] = 15000,
                    ["typical"] = 45000,
                    ["max"] = 90000
                },
                ["note"] = "Listener will be unreachable for ~30-60s during domain reload. Connection failures in that window are EXPECTED — wait ≥15s before retrying, treat ~90s as the assume-problem threshold. Session id persists across the reload."
            };
        }

        static JToken Status()
        {
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var arr = new JArray();
            foreach (var a in assemblies)
            {
                arr.Add(new JObject
                {
                    ["name"] = a.name,
                    ["outputPath"] = a.outputPath,
                    ["sourceFileCount"] = a.sourceFiles?.Length ?? 0
                });
            }
            return new JObject
            {
                ["isCompiling"] = EditorApplication.isCompiling,
                ["isUpdating"] = EditorApplication.isUpdating,
                ["assemblies"] = arr
            };
        }
    }
}
