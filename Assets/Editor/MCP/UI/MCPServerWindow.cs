using MCP.Server;
using UnityEditor;
using UnityEngine;

namespace MCP.UI
{
    public class MCPServerWindow : EditorWindow
    {
        Vector2 _scroll;

        public static void OpenWindow()
        {
            var w = GetWindow<MCPServerWindow>("MCP Server");
            w.minSize = new Vector2(420, 280);
            w.Show();
        }

        void OnEnable()
        {
            MCPServer.StateChanged += Repaint;
            EditorApplication.update += OnTick;
        }

        void OnDisable()
        {
            MCPServer.StateChanged -= Repaint;
            EditorApplication.update -= OnTick;
        }

        double _nextRepaint;
        void OnTick()
        {
            if (EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.5;
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("MCP Editor Server", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Status", GUILayout.Width(60));
                EditorGUILayout.LabelField(MCPServer.IsRunning ? "Running" : "Stopped");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Endpoint", GUILayout.Width(60));
                EditorGUILayout.SelectableLabel(MCPServer.Endpoint ?? "(not started)", EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Sessions", GUILayout.Width(60));
                EditorGUILayout.LabelField(SessionManager.Count.ToString());
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(MCPServer.IsRunning))
                    if (GUILayout.Button("Start")) MCPServer.Start();
                using (new EditorGUI.DisabledScope(!MCPServer.IsRunning))
                    if (GUILayout.Button("Stop")) MCPServer.Stop();
                if (GUILayout.Button("Settings")) Selection.activeObject = MCPServerSettings.GetOrCreate();
                if (GUILayout.Button("Tools (" + Protocol.ToolRegistry.Count + ")")) DumpTools();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Recent log", EditorStyles.boldLabel);
            using (var sv = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.ExpandHeight(true)))
            {
                _scroll = sv.scrollPosition;
                foreach (var line in MCPServer.RecentLog)
                    EditorGUILayout.LabelField(line);
            }
        }

        void DumpTools()
        {
            var sb = new System.Text.StringBuilder("[MCP] Registered tools:\n");
            foreach (var t in Protocol.ToolRegistry.All)
                sb.Append("  - ").Append(t.Name).Append(": ").Append(t.Description).Append('\n');
            Debug.Log(sb.ToString());
        }
    }
}
