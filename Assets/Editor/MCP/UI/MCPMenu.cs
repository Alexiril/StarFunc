using UnityEditor;
using UnityEngine;

namespace MCP.UI
{
    public static class MCPMenu
    {
        const string Root = "Window/MCP Server/";

        [MenuItem(Root + "Start", priority = 100)]
        static void Start() => MCPServer.Start();
        [MenuItem(Root + "Start", validate = true)]
        static bool StartValidate() => !MCPServer.IsRunning;

        [MenuItem(Root + "Stop", priority = 101)]
        static void Stop() => MCPServer.Stop();
        [MenuItem(Root + "Stop", validate = true)]
        static bool StopValidate() => MCPServer.IsRunning;

        [MenuItem(Root + "Status", priority = 102)]
        static void Status()
        {
            Debug.Log(MCPServer.IsRunning
                ? $"[MCP] Running on {MCPServer.Endpoint}"
                : "[MCP] Stopped");
        }

        [MenuItem(Root + "Open Window", priority = 200)]
        static void OpenWindow() => MCPServerWindow.OpenWindow();

        [MenuItem(Root + "Select Settings", priority = 201)]
        static void SelectSettings()
        {
            var s = MCPServerSettings.GetOrCreate();
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
        }
    }
}
