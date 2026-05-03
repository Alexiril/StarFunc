using System;
using System.Collections.Generic;
using MCP.Protocol;
using MCP.Server;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCP.Tools
{
    [InitializeOnLoad]
    public static class LogTools
    {
        class Entry
        {
            public DateTime Time;
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        const int MaxEntries = 2000;
        static readonly Queue<Entry> _buffer = new Queue<Entry>(MaxEntries);
        static readonly object _lock = new object();

        static LogTools()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "logs_get",
                Description = "Get Unity console logs captured since editor start (or last domain reload). Buffer holds up to 2000 most recent entries. Filters: levels, substring, time. clearAfter empties the buffer after reading. Response telemetry: totalCaptured (buffer size), matched (count after filters, before limit), returned (entries actually included after limit truncation), bufferLimit.",
                InputSchema = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject
                    {
                        ["levels"] = new JObject
                        {
                            ["type"] = "array",
                            ["items"] = new JObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JArray { "Log", "Warning", "Error", "Exception", "Assert" }
                            },
                            ["description"] = "Filter by log type. Default: all."
                        },
                        ["contains"] = new JObject { ["type"] = "string", ["description"] = "Case-insensitive substring filter on message." },
                        ["sinceUtc"] = new JObject { ["type"] = "string", ["description"] = "ISO-8601 UTC timestamp; only entries at or after this time are returned." },
                        ["limit"] = new JObject { ["type"] = "integer", ["default"] = 100 },
                        ["mostRecentFirst"] = new JObject { ["type"] = "boolean", ["default"] = true },
                        ["includeStackTrace"] = new JObject { ["type"] = "boolean", ["default"] = false },
                        ["clearAfter"] = new JObject { ["type"] = "boolean", ["default"] = false, ["description"] = "Clear the entire buffer after reading." }
                    }
                },
                Handler = args => MainThreadDispatcher.Run(() => GetLogs(args))
            });

            ToolRegistry.Register(new ToolDescriptor
            {
                Name = "logs_clear",
                Description = "Clear the in-memory log buffer (does not affect Unity's console window).",
                InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject() },
                Handler = args => MainThreadDispatcher.Run(() => ClearLogs())
            });
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            var entry = new Entry { Time = DateTime.UtcNow, Type = type, Message = message, StackTrace = stackTrace };
            lock (_lock)
            {
                _buffer.Enqueue(entry);
                while (_buffer.Count > MaxEntries) _buffer.Dequeue();
            }
        }

        static JToken GetLogs(JObject args)
        {
            HashSet<LogType> levelFilter = null;
            if (args["levels"] is JArray levelsArr && levelsArr.Count > 0)
            {
                levelFilter = new HashSet<LogType>();
                foreach (var t in levelsArr)
                {
                    var s = (string)t;
                    if (string.IsNullOrEmpty(s)) continue;
                    if (Enum.TryParse<LogType>(s, ignoreCase: true, out var lt)) levelFilter.Add(lt);
                    else throw new ArgumentException($"Unknown log level: {s}");
                }
            }

            var contains = (string)args["contains"];
            DateTime? since = null;
            if (args["sinceUtc"] is JValue sv && sv.Type == JTokenType.String)
            {
                if (!DateTime.TryParse((string)sv, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                    throw new ArgumentException("sinceUtc not parseable as ISO-8601");
                since = parsed.ToUniversalTime();
            }
            int limit = args["limit"] != null ? (int)args["limit"] : 100;
            bool mostRecentFirst = args["mostRecentFirst"] == null || (bool)args["mostRecentFirst"];
            bool includeStack = args["includeStackTrace"] != null && (bool)args["includeStackTrace"];
            bool clearAfter = args["clearAfter"] != null && (bool)args["clearAfter"];

            List<Entry> snapshot;
            int totalCaptured;
            lock (_lock)
            {
                snapshot = new List<Entry>(_buffer);
                totalCaptured = snapshot.Count;
                if (clearAfter) _buffer.Clear();
            }

            var matched = new List<Entry>();
            foreach (var e in snapshot)
            {
                if (levelFilter != null && !levelFilter.Contains(e.Type)) continue;
                if (since.HasValue && e.Time < since.Value) continue;
                if (!string.IsNullOrEmpty(contains) &&
                    e.Message.IndexOf(contains, StringComparison.OrdinalIgnoreCase) < 0) continue;
                matched.Add(e);
            }

            int matchedCount = matched.Count; // After filters, before limit truncation.
            if (mostRecentFirst) matched.Reverse();
            if (limit > 0 && matched.Count > limit) matched = matched.GetRange(0, limit);

            var arr = new JArray();
            foreach (var e in matched)
            {
                var o = new JObject
                {
                    ["time"] = e.Time.ToString("o"),
                    ["level"] = e.Type.ToString(),
                    ["message"] = e.Message
                };
                if (includeStack && !string.IsNullOrEmpty(e.StackTrace)) o["stackTrace"] = e.StackTrace;
                arr.Add(o);
            }

            return new JObject
            {
                ["totalCaptured"] = totalCaptured,
                ["matched"] = matchedCount,
                ["returned"] = matched.Count,
                ["bufferLimit"] = MaxEntries,
                ["entries"] = arr
            };
        }

        static JToken ClearLogs()
        {
            int cleared;
            lock (_lock) { cleared = _buffer.Count; _buffer.Clear(); }
            return new JObject { ["ok"] = true, ["cleared"] = cleared };
        }
    }
}
