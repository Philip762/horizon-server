using DotNetty.Common.Internal.Logging;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Server.Dme
{
    /// <summary>
    /// Minimal HTTP server exposing operational metrics (currently just the active
    /// user count) for local monitoring/health-check scripts. Deliberately bound to
    /// 127.0.0.1 only so it is never reachable off-box, even if misconfigured.
    /// </summary>
    public class MetricsServer
    {
        static readonly IInternalLogger Logger = InternalLoggerFactory.GetInstance<MetricsServer>();

        private readonly HttpListener _listener = new HttpListener();
        private readonly Func<int> _getActiveUserCount;
        private volatile bool _running = false;

        public MetricsServer(int port, Func<int> getActiveUserCount)
        {
            _getActiveUserCount = getActiveUserCount;
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _running = true;
                _ = Task.Run(ListenLoopAsync);
                Logger.Info($"Metrics server listening on {string.Join(", ", _listener.Prefixes)}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to start metrics server: {ex}");
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener.Stop(); } catch { }
        }

        private async Task ListenLoopAsync()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch (Exception)
                {
                    if (!_running)
                        break;
                    continue;
                }

                _ = Task.Run(() => HandleRequest(ctx));
            }
        }

        private void HandleRequest(HttpListenerContext ctx)
        {
            try
            {
                string responseBody;
                int statusCode;

                if (ctx.Request.Url.AbsolutePath.Equals("/active-users", StringComparison.OrdinalIgnoreCase))
                {
                    statusCode = 200;
                    responseBody = JsonConvert.SerializeObject(new
                    {
                        activeUsers = _getActiveUserCount(),
                        timestampUtc = DateTime.UtcNow.ToString("o")
                    });
                }
                else
                {
                    statusCode = 404;
                    responseBody = JsonConvert.SerializeObject(new { error = "not found" });
                }

                var buffer = Encoding.UTF8.GetBytes(responseBody);
                ctx.Response.StatusCode = statusCode;
                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentLength64 = buffer.Length;
                ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error handling metrics request: {ex}");
            }
            finally
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }
}
