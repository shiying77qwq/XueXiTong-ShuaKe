using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace StudyVideoHelper
{
    /// <summary>
    /// Bounds a read-only provider call whose native implementation may not support cancellation.
    /// Cancellation returns promptly; it cannot abort a native call already running. The callback
    /// must therefore only read state and must never click, navigate, or perform other writes.
    /// </summary>
    public static class BoundedRead
    {
        public static T Run<T>(Func<T> read, ManualResetEventSlim stop, TimeSpan timeout)
        {
            if (read == null) throw new ArgumentNullException("read");
            if (stop == null) throw new ArgumentNullException("stop");
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            ThrowIfStopped(stop);

            Stopwatch elapsed = Stopwatch.StartNew();
            Task<T> task = Task.Factory.StartNew(delegate {
                ThrowIfStopped(stop);
                return read();
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // A provider may finish with an exception after the caller has cancelled or timed out.
            // Observe that late fault without continuing the caller's session or acting on the UI.
            task.ContinueWith(delegate(Task<T> failed) { GC.KeepAlive(failed.Exception); },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            while (true)
            {
                ThrowIfStopped(stop);
                if (task.IsCompleted) break;
                double remaining = (timeout - elapsed.Elapsed).TotalMilliseconds;
                if (remaining <= 0)
                {
                    // Give a simultaneous cancellation or completed result priority over timeout.
                    ThrowIfStopped(stop);
                    if (task.IsCompleted) break;
                    stop.Set();
                    throw new TimeoutException("读取学习通控件超过" + timeout.TotalSeconds.ToString("0.##") +
                        "秒，已停止。请确认学习通响应正常后重新开始。");
                }
                stop.Wait((int)Math.Max(1, Math.Min(25, Math.Ceiling(remaining))));
            }

            ThrowIfStopped(stop);
            T result;
            try { result = task.Result; }
            catch (AggregateException failure)
            {
                ThrowIfStopped(stop);
                // Remove Task's wrapper, preserving the provider's original exception type.
                throw failure.InnerException ?? failure;
            }
            ThrowIfStopped(stop);
            return result;
        }

        private static void ThrowIfStopped(ManualResetEventSlim stop)
        {
            if (stop.IsSet) throw new OperationCanceledException("读取已取消，不再执行后续操作。");
        }
    }
}
