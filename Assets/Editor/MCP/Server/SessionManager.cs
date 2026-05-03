using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace MCP.Server
{
    public class Session
    {
        public string Id;
        public DateTime CreatedAt;
        public DateTime LastSeen;
        public bool Initialized;
    }

    [InitializeOnLoad]
    public static class SessionManager
    {
        const string StateKey = "MCP.Sessions";

        static readonly ConcurrentDictionary<string, Session> _sessions = new ConcurrentDictionary<string, Session>();

        static SessionManager()
        {
            Load();
            AssemblyReloadEvents.beforeAssemblyReload -= Save;
            AssemblyReloadEvents.beforeAssemblyReload += Save;
        }

        public static Session Create()
        {
            var s = new Session
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAt = DateTime.UtcNow,
                LastSeen = DateTime.UtcNow
            };
            _sessions[s.Id] = s;
            return s;
        }

        public static bool TryTouch(string id, out Session session)
        {
            if (string.IsNullOrEmpty(id)) { session = null; return false; }
            if (_sessions.TryGetValue(id, out session))
            {
                session.LastSeen = DateTime.UtcNow;
                return true;
            }
            return false;
        }

        public static bool Remove(string id) => _sessions.TryRemove(id, out _);

        public static void EvictIdle(TimeSpan idleAfter)
        {
            var cutoff = DateTime.UtcNow - idleAfter;
            foreach (var kv in _sessions)
                if (kv.Value.LastSeen < cutoff)
                    _sessions.TryRemove(kv.Key, out _);
        }

        public static void Clear()
        {
            _sessions.Clear();
            SessionState.EraseString(StateKey);
        }

        public static int Count => _sessions.Count;

        public static void Save()
        {
            try
            {
                var values = new List<Session>(_sessions.Values);
                SessionState.SetString(StateKey, JsonConvert.SerializeObject(values));
            }
            catch (Exception e) { Debug.LogWarning($"[MCP] SessionManager.Save failed: {e.Message}"); }
        }

        public static void Load()
        {
            var json = SessionState.GetString(StateKey, null);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<Session>>(json);
                if (list == null) return;
                foreach (var s in list)
                    if (!string.IsNullOrEmpty(s?.Id))
                        _sessions[s.Id] = s;
            }
            catch (Exception e) { Debug.LogWarning($"[MCP] SessionManager.Load failed: {e.Message}"); }
        }
    }
}
