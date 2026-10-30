using System;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Threading;
using ValheimCompanion.Core.Ipc;

namespace ValheimCompanion.Ipc
{
    /// <summary>
    /// Loopback-only HTTP bridge for the Python voice module.
    /// HTTP thread only accepts/parses and enqueues; Unity API runs in Plugin.Update (main thread).
    /// </summary>
    internal sealed class IpcServer : IDisposable
    {
        private sealed class PendingCommand
        {
            public IpcCommandKind Kind;
            public ManualResetEventSlim Done = new ManualResetEventSlim(false);
            public IpcCommandResult Result;
        }

        private readonly string _prefix;
        private readonly Func<IpcCommandKind, IpcCommandResult> _executeOnMainThread;
        private readonly Func<string> _healthJson;

        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;
        private int _nextJobId = 1;
        private int _busy; // 0 = free, 1 = busy
        private readonly ConcurrentQueue<PendingCommand> _queue = new ConcurrentQueue<PendingCommand>();

        public IpcServer(string host, int port,
            Func<IpcCommandKind, IpcCommandResult> executeOnMainThread,
            Func<string> healthJson)
        {
            // Hard rule: never bind a public interface.
            _prefix = $"http://127.0.0.1:{port}/";
            _executeOnMainThread = executeOnMainThread;
            _healthJson = healthJson;
        }

        public string Prefix => _prefix;

        public bool Start()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add(_prefix);
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "VC-IPC" };
                _thread.Start();
                Plugin.Log.LogMessage($"[IPC] listening on {_prefix} (loopback only)");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[IPC] failed to start on {_prefix}: {ex.Message}");
                _running = false;
                return false;
            }
        }

        /// <summary>Called from Unity main thread (Plugin.Update). Executes at most one command per frame.</summary>
        public void DrainMainThread()
        {
            if (!_queue.TryDequeue(out var pending)) return;
            try
            {
                pending.Result = _executeOnMainThread(pending.Kind)
                                 ?? IpcCommandResult.Error("null result");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[IPC] execute {pending.Kind}: {ex}");
                pending.Result = IpcCommandResult.Error(ex.Message);
            }

            pending.Done.Set();
        }

        public void Dispose()
        {
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch
            {
                // ignore
            }
        }

        private void Loop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();
                }
                catch (Exception)
                {
                    if (!_running) return;
                    continue;
                }

                try
                {
                    Handle(ctx);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[IPC] handler: {ex.Message}");
                    TryWrite(ctx, 500, IpcJson.CommandRejected("error", ex.Message));
                }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
            if (path == "") path = "/";

            if (ctx.Request.HttpMethod == "GET" && (path == "/health" || path == "/"))
            {
                TryWrite(ctx, 200, _healthJson());
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path == "/job")
            {
                TryWrite(ctx, 200, IpcJson.CommandRejected("error",
                    "job polling not used; /command returns terminal status after main-thread execution"));
                return;
            }

            if (ctx.Request.HttpMethod == "POST" && path == "/command")
            {
                HandleCommand(ctx);
                return;
            }

            TryWrite(ctx, 404, IpcJson.CommandRejected("error", "unknown endpoint"));
        }

        private void HandleCommand(HttpListenerContext ctx)
        {
            string body;
            using (var reader = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding))
            {
                body = reader.ReadToEnd();
            }

            string cmdRaw;
            if (!IpcJson.TryGetString(body, "cmd", out cmdRaw) ||
                !IpcCommandParser.TryParse(cmdRaw, out IpcCommandKind kind))
            {
                TryWrite(ctx, 400, IpcJson.CommandRejected("rejected",
                    "cmd must be one of FOLLOW_ME|COME_HERE|STOP|STATUS"));
                return;
            }

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                TryWrite(ctx, 409, IpcJson.CommandRejected("rejected", "another command is in flight"));
                return;
            }

            var pending = new PendingCommand { Kind = kind };
            _queue.Enqueue(pending);

            // Wait for the Unity main thread (Plugin.Update). Actions themselves are quick;
            // long-running movement is verified by the client polling /health.
            bool signaled = pending.Done.Wait(TimeSpan.FromSeconds(5));
            Interlocked.Exchange(ref _busy, 0);

            if (!signaled)
            {
                pending.Result = IpcCommandResult.Error("main thread did not execute command within 5s");
                pending.Result.Status = IpcJobStatus.Timeout;
                pending.Result.Accepted = true;
            }

            int jobId = Interlocked.Increment(ref _nextJobId);
            pending.Result.JobId = jobId;
            int code = pending.Result.Accepted &&
                       pending.Result.Status != IpcJobStatus.Error &&
                       pending.Result.Status != IpcJobStatus.Rejected
                ? 200
                : 400;
            TryWrite(ctx, code, pending.Result.ToJson());
        }

        private static void TryWrite(HttpListenerContext ctx, int status, string body)
        {
            try
            {
                byte[] buf = Encoding.UTF8.GetBytes(body ?? "{}");
                ctx.Response.StatusCode = status;
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                ctx.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[IPC] write: {ex.Message}");
            }
        }
    }

    /// <summary>Result of one IPC command (built on the main thread).</summary>
    internal sealed class IpcCommandResult
    {
        public int JobId;
        public bool Accepted = true;
        public IpcJobStatus Status = IpcJobStatus.Succeeded;
        public string Message = "";
        public string StateJson = "";

        public string ToJson()
        {
            return IpcJson.Job(JobId, IpcJobStatusName.Of(Status), Message, StateJson);
        }

        public static IpcCommandResult Ok(string message, string stateJson = "")
        {
            return new IpcCommandResult
            {
                Accepted = true,
                Status = IpcJobStatus.Succeeded,
                Message = message,
                StateJson = stateJson
            };
        }

        public static IpcCommandResult Fail(string message)
        {
            return new IpcCommandResult
            {
                Accepted = true,
                Status = IpcJobStatus.Failed,
                Message = message
            };
        }

        public static IpcCommandResult Error(string message)
        {
            return new IpcCommandResult
            {
                Accepted = false,
                Status = IpcJobStatus.Error,
                Message = message
            };
        }
    }
}
