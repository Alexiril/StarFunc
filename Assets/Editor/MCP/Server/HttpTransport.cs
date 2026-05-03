using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MCP.Protocol;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MCP.Server
{
    public class HttpTransport
    {
        const string SessionHeader = "Mcp-Session-Id";
        const string ProtocolHeader = "MCP-Protocol-Version";

        readonly int _port;
        readonly string _bearerToken;
        readonly Action<string, string> _logger;
        readonly Func<JsonRpcMessage, Task<JsonRpcMessage>> _handle;

        HttpListener _listener;
        CancellationTokenSource _cts;
        Thread _acceptThread;

        public HttpTransport(int port, string bearerToken, Func<JsonRpcMessage, Task<JsonRpcMessage>> handler, Action<string, string> logger)
        {
            _port = port;
            _bearerToken = bearerToken ?? "";
            _handle = handler;
            _logger = logger ?? ((_, __) => { });
        }

        public string Endpoint => $"http://127.0.0.1:{_port}/mcp/";

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(Endpoint);
            _listener.Start();
            _cts = new CancellationTokenSource();
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "MCP-Accept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            try { _listener?.Close(); } catch { }
            _listener = null;
            try { _acceptThread?.Join(500); } catch { }
            _acceptThread = null;
        }

        void AcceptLoop()
        {
            while (_listener != null && _listener.IsListening && !_cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = _listener.GetContext(); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }

                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var resp = ctx.Response;

                var origin = req.Headers["Origin"];
                if (!IsAllowedOrigin(origin))
                {
                    await Respond(resp, 403, "text/plain", "Forbidden origin");
                    return;
                }

                if (!IsAuthorized(req))
                {
                    await Respond(resp, 401, "text/plain", "Unauthorized");
                    return;
                }

                _logger(req.HttpMethod, req.Url?.AbsolutePath ?? "");

                switch (req.HttpMethod)
                {
                    case "POST": await HandlePost(req, resp); return;
                    case "GET": await HandleGet(req, resp); return;
                    case "DELETE": await HandleDelete(req, resp); return;
                    case "OPTIONS": await HandleOptions(resp); return;
                    default: await Respond(resp, 405, "text/plain", "Method not allowed"); return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            }
        }

        bool IsAllowedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return true;
            if (Uri.TryCreate(origin, UriKind.Absolute, out var u))
            {
                var h = u.Host;
                return h == "127.0.0.1" || h == "localhost" || h == "::1";
            }
            return false;
        }

        bool IsAuthorized(HttpListenerRequest req)
        {
            if (string.IsNullOrEmpty(_bearerToken)) return true;
            var auth = req.Headers["Authorization"];
            return auth == "Bearer " + _bearerToken;
        }

        async Task HandlePost(HttpListenerRequest req, HttpListenerResponse resp)
        {
            string body;
            using (var reader = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            JToken parsed;
            try { parsed = JsonRpcSerializer.ParseToken(body); }
            catch
            {
                var err = new JsonRpcMessage { Id = JValue.CreateNull(), Error = new JsonRpcError { Code = JsonRpcErrorCodes.ParseError, Message = "Parse error" } };
                await Respond(resp, 400, "application/json", JsonRpcSerializer.Serialize(err));
                return;
            }

            var messages = parsed is JArray arr
                ? arr.ToObject<List<JsonRpcMessage>>()
                : new List<JsonRpcMessage> { parsed.ToObject<JsonRpcMessage>() };

            var sessionId = req.Headers[SessionHeader];
            string newSessionId = null;

            bool hasInitialize = false;
            foreach (var m in messages) if (m.Method == "initialize") { hasInitialize = true; break; }

            if (hasInitialize)
            {
                newSessionId = SessionManager.Create().Id;
                resp.Headers[SessionHeader] = newSessionId;
            }
            else
            {
                if (!SessionManager.TryTouch(sessionId, out _))
                {
                    await Respond(resp, 404, "text/plain", "Bad or expired session");
                    return;
                }
            }

            var responses = new List<JsonRpcMessage>();
            bool anyRequest = false;
            foreach (var m in messages)
            {
                if (m.IsNotification || m.IsResponse) continue;
                anyRequest = true;
                var r = await _handle(m);
                if (r != null) responses.Add(r);
            }

            if (!anyRequest)
            {
                resp.StatusCode = 202;
                resp.Close();
                return;
            }

            var payload = responses.Count == 1
                ? JsonRpcSerializer.Serialize(responses[0])
                : JsonRpcSerializer.Serialize(responses);
            await Respond(resp, 200, "application/json", payload);
        }

        Task HandleGet(HttpListenerRequest req, HttpListenerResponse resp)
        {
            var sessionId = req.Headers[SessionHeader];
            if (!SessionManager.TryTouch(sessionId, out _))
                return Respond(resp, 404, "text/plain", "Bad or expired session");
            return Respond(resp, 405, "text/plain", "Server-initiated SSE not supported in v1");
        }

        Task HandleDelete(HttpListenerRequest req, HttpListenerResponse resp)
        {
            var sessionId = req.Headers[SessionHeader];
            SessionManager.Remove(sessionId);
            resp.StatusCode = 204;
            resp.Close();
            return Task.CompletedTask;
        }

        Task HandleOptions(HttpListenerResponse resp)
        {
            resp.Headers["Access-Control-Allow-Origin"] = "*";
            resp.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
            resp.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, " + SessionHeader + ", " + ProtocolHeader;
            resp.StatusCode = 204;
            resp.Close();
            return Task.CompletedTask;
        }

        static async Task Respond(HttpListenerResponse resp, int status, string contentType, string body)
        {
            resp.StatusCode = status;
            resp.ContentType = contentType;
            var bytes = Encoding.UTF8.GetBytes(body ?? "");
            resp.ContentLength64 = bytes.Length;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            resp.Close();
        }
    }
}
