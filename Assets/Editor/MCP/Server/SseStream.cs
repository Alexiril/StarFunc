using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MCP.Server
{
    public class SseStream
    {
        readonly HttpListenerResponse _response;
        readonly Stream _stream;
        readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        long _eventId;

        public SseStream(HttpListenerResponse response)
        {
            _response = response;
            _response.StatusCode = 200;
            _response.ContentType = "text/event-stream";
            _response.Headers["Cache-Control"] = "no-store";
            _response.Headers["Connection"] = "keep-alive";
            _response.SendChunked = true;
            _stream = _response.OutputStream;
        }

        public async Task SendMessage(string jsonPayload)
        {
            await _writeLock.WaitAsync();
            try
            {
                var id = Interlocked.Increment(ref _eventId);
                var sb = new StringBuilder();
                sb.Append("id: ").Append(id).Append('\n');
                sb.Append("event: message\n");
                foreach (var line in jsonPayload.Split('\n'))
                    sb.Append("data: ").Append(line).Append('\n');
                sb.Append('\n');
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            finally { _writeLock.Release(); }
        }

        public async Task SendKeepalive()
        {
            await _writeLock.WaitAsync();
            try
            {
                var bytes = Encoding.UTF8.GetBytes(": keepalive\n\n");
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            finally { _writeLock.Release(); }
        }

        public void Close()
        {
            try { _stream.Close(); } catch { }
            try { _response.Close(); } catch { }
        }
    }
}
