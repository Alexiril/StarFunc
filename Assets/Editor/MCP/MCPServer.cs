using System;
using System.Collections.Generic;
using MCP.Protocol;
using MCP.Server;
using UnityEditor;
using UnityEngine;

namespace MCP
{
    public static class MCPServer
    {
        const string SessionStateRunningKey = "MCP.Running";
        const string SessionStatePortKey = "MCP.Port";
        const int LogCapacity = 50;

        static HttpTransport _transport;
        static readonly Queue<string> _logRing = new Queue<string>();

        public static bool IsRunning => _transport != null;
        public static int Port { get; private set; }
        public static string Endpoint => _transport?.Endpoint;
        public static IEnumerable<string> RecentLog => _logRing;
        public static event Action StateChanged;

        public static void Start()
        {
            if (IsRunning) return;
            var settings = MCPServerSettings.GetOrCreate();
            try
            {
                MainThreadDispatcher.Initialize();
                _transport = new HttpTransport(settings.port, settings.optionalBearerToken, McpHandler.Handle, OnLog);
                _transport.Start();
                Port = settings.port;
                SessionState.SetBool(SessionStateRunningKey, true);
                SessionState.SetInt(SessionStatePortKey, settings.port);
                Log($"MCP server listening on {_transport.Endpoint}");
                StateChanged?.Invoke();
            }
            catch (Exception e)
            {
                _transport = null;
                Debug.LogError($"[MCP] Failed to start on port {settings.port}: {e.Message}");
                throw;
            }
        }

        public static void Stop()
        {
            if (_transport == null) return;
            try { _transport.Stop(); } catch (Exception e) { Debug.LogException(e); }
            _transport = null;
            SessionState.SetBool(SessionStateRunningKey, false);
            // Sessions intentionally NOT cleared here — they survive stop/start within an editor session
            // (and across domain reloads via SessionManager's SessionState persistence). Editor restart
            // wipes them naturally; clients can DELETE individually for explicit teardown.
            Log("MCP server stopped");
            StateChanged?.Invoke();
        }

        public static bool ShouldResumeAfterReload =>
            SessionState.GetBool(SessionStateRunningKey, false);

        static void OnLog(string method, string path)
        {
            var settings = MCPServerSettings.GetOrCreate();
            if (!settings.logRequests) return;
            Log($"{DateTime.Now:HH:mm:ss} {method} {path}");
        }

        static void Log(string line)
        {
            lock (_logRing)
            {
                _logRing.Enqueue(line);
                while (_logRing.Count > LogCapacity) _logRing.Dequeue();
            }
        }
    }
}
