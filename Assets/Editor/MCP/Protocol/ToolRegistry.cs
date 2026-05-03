using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MCP.Protocol
{
    public class ToolDescriptor
    {
        public string Name;
        public string Description;
        public JObject InputSchema;
        public Func<JObject, Task<JToken>> Handler;
    }

    public static class ToolRegistry
    {
        static readonly Dictionary<string, ToolDescriptor> _tools = new Dictionary<string, ToolDescriptor>();

        public static void Register(ToolDescriptor tool)
        {
            _tools[tool.Name] = tool;
        }

        public static bool TryGet(string name, out ToolDescriptor tool)
        {
            return _tools.TryGetValue(name, out tool);
        }

        public static IEnumerable<ToolDescriptor> All => _tools.Values;

        public static int Count => _tools.Count;
    }
}
