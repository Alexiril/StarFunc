using System.IO;
using UnityEditor;
using UnityEngine;

namespace MCP
{
    public class MCPServerSettings : ScriptableObject
    {
        [Tooltip("TCP port for the MCP HTTP listener. Bound to 127.0.0.1.")]
        public int port = 17932;

        [Tooltip("Start the server automatically when the Editor loads or after a domain reload.")]
        public bool autoStart = true;

        [Tooltip("Stop the server when entering Play mode and resume when leaving it.")]
        public bool stopOnPlayMode = true;

        [Tooltip("Optional bearer token clients must send in the Authorization header. Empty disables auth.")]
        public string optionalBearerToken = "";

        [Tooltip("Append every request to the in-memory ring buffer shown in the MCP Server window.")]
        public bool logRequests = true;

        [Tooltip("Idle timeout for an MCP session (Mcp-Session-Id) before eviction.")]
        public int sessionIdleTimeoutSeconds = 600;

        const string ResourcePath = "MCPServerSettings";
        const string AssetPath = "Assets/Editor/MCP/Resources/MCPServerSettings.asset";

        static MCPServerSettings _cached;

        public static MCPServerSettings GetOrCreate()
        {
            if (_cached != null) return _cached;

            _cached = Resources.Load<MCPServerSettings>(ResourcePath);
            if (_cached != null) return _cached;

            var dir = Path.GetDirectoryName(AssetPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            _cached = CreateInstance<MCPServerSettings>();
            AssetDatabase.CreateAsset(_cached, AssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return _cached;
        }
    }
}
