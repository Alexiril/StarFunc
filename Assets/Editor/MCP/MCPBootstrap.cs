using UnityEditor;

namespace MCP
{
    [InitializeOnLoad]
    public static class MCPBootstrap
    {
        static MCPBootstrap()
        {
            EditorApplication.delayCall += BootstrapDelayed;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnQuit;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void BootstrapDelayed()
        {
            var settings = MCPServerSettings.GetOrCreate();
            if (settings.autoStart || MCPServer.ShouldResumeAfterReload)
                MCPServer.Start();
        }

        static void OnBeforeReload() => MCPServer.Stop();
        static void OnQuit() => MCPServer.Stop();

        static void OnPlayMode(PlayModeStateChange change)
        {
            var settings = MCPServerSettings.GetOrCreate();
            if (!settings.stopOnPlayMode) return;

            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    MCPServer.Stop();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    if (settings.autoStart) MCPServer.Start();
                    break;
            }
        }
    }
}
