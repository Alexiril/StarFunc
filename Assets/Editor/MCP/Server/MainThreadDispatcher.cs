using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace MCP.Server
{
    public static class MainThreadDispatcher
    {
        static readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        static readonly ConcurrentQueue<Action> _deferred = new ConcurrentQueue<Action>();
        static int _mainThreadId = -1;
        static bool _hooked;

        public static void Initialize()
        {
            if (_hooked) return;
            _hooked = true;
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += Pump;
        }

        public static void Shutdown()
        {
            if (!_hooked) return;
            _hooked = false;
            EditorApplication.update -= Pump;
            while (_queue.TryDequeue(out _)) { }
            while (_deferred.TryDequeue(out _)) { }
        }

        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == _mainThreadId;

        public static Task<T> Run<T>(Func<T> func)
        {
            if (IsMainThread)
            {
                try { return Task.FromResult(func()); }
                catch (Exception e) { return Task.FromException<T>(e); }
            }

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Enqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        public static Task Run(Action action)
        {
            return Run<object>(() => { action(); return null; });
        }

        // Polls `ready` on the main thread once per editor tick; resolves with `result()` when ready,
        // or fails with TimeoutException after the deadline. The check re-enqueues to the deferred queue
        // so it runs at most once per Pump cycle (no main-thread spin).
        public static Task<T> WaitUntil<T>(Func<bool> ready, Func<T> result, TimeSpan timeout, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deadline = DateTime.UtcNow + timeout;
            Action check = null;
            check = () =>
            {
                if (ct.IsCancellationRequested) { tcs.TrySetCanceled(ct); return; }
                try
                {
                    if (ready()) { tcs.TrySetResult(result()); return; }
                    if (DateTime.UtcNow >= deadline)
                    {
                        tcs.TrySetException(new TimeoutException($"WaitUntil timed out after {timeout.TotalMilliseconds:F0}ms"));
                        return;
                    }
                }
                catch (Exception e) { tcs.TrySetException(e); return; }
                _deferred.Enqueue(check);
            };
            _queue.Enqueue(check);
            return tcs.Task;
        }

        static void Pump()
        {
            // Promote deferred actions for this tick (they were re-queued by polling tasks during the previous tick).
            while (_deferred.TryDequeue(out var d))
                _queue.Enqueue(d);

            int budget = 64;
            while (budget-- > 0 && _queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
            }
        }
    }
}
