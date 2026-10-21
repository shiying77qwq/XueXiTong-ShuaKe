using System;
using System.Diagnostics;
using System.Threading;
using StudyVideoHelper;

internal static class BoundedReadTests
{
    private static int passed;
    private static int failed;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Case(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine("PASS bounded read: " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL bounded read: " + name + " - " + error); }
    }

    private static Thread CancelAfterEntry(ManualResetEventSlim entered, ManualResetEventSlim stop)
    {
        Thread cancel = new Thread(delegate() {
            if (entered.Wait(2000)) stop.Set();
        });
        cancel.IsBackground = true;
        cancel.Start();
        return cancel;
    }

    public static int Run()
    {
        passed = 0; failed = 0;
        Case("returns a normal result from a background MTA reader", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            {
                int caller = Thread.CurrentThread.ManagedThreadId;
                int value = BoundedRead.Run(delegate {
                    Check(Thread.CurrentThread.ManagedThreadId != caller, "Read ran on the caller's thread.");
                    Check(Thread.CurrentThread.IsBackground, "A blocked reader could keep the process alive.");
                    Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA, "Reader is not MTA.");
                    return 42;
                }, stop, TimeSpan.FromSeconds(2));
                Check(value == 42 && !stop.IsSet, "A normal result was lost or set stop.");
            }
        });

        Case("preserves the original provider exception", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            {
                InvalidOperationException expected = new InvalidOperationException("provider failure");
                Exception received = null;
                try { BoundedRead.Run<int>(delegate { throw expected; }, stop, TimeSpan.FromSeconds(2)); }
                catch (Exception error) { received = error; }
                Check(object.ReferenceEquals(received, expected), "Task's exception wrapper escaped.");
                Check(!stop.IsSet, "A provider error unexpectedly set stop.");
            }
        });

        Case("pre-cancellation never starts the reader", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(true))
            {
                int calls = 0;
                bool cancelled = false;
                try { BoundedRead.Run(delegate { Interlocked.Increment(ref calls); return 1; }, stop, TimeSpan.FromSeconds(2)); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && calls == 0, "Pre-cancellation invoked the read callback.");
            }
        });

        Case("cancellation promptly returns while the provider remains blocked", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            using (ManualResetEventSlim entered = new ManualResetEventSlim(false))
            using (ManualResetEventSlim release = new ManualResetEventSlim(false))
            using (ManualResetEventSlim exited = new ManualResetEventSlim(false))
            {
                Thread cancel = CancelAfterEntry(entered, stop);
                bool cancelled = false;
                Stopwatch elapsed = Stopwatch.StartNew();
                try
                {
                    try { BoundedRead.Run(delegate {
                        entered.Set();
                        try { release.Wait(); return 1; } finally { exited.Set(); }
                    }, stop, TimeSpan.FromSeconds(5)); }
                    catch (OperationCanceledException) { cancelled = true; }
                    Check(cancelled && elapsed.Elapsed.TotalSeconds < 1.5, "Cancellation waited for the blocked provider.");
                    Check(!exited.IsSet, "The provider was not blocked when cancellation returned.");
                }
                finally { release.Set(); Check(exited.Wait(2000), "Blocked provider did not finish after release."); cancel.Join(2000); }
            }
        });

        Case("timeout promptly sets stop while the provider remains blocked", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            using (ManualResetEventSlim entered = new ManualResetEventSlim(false))
            using (ManualResetEventSlim release = new ManualResetEventSlim(false))
            using (ManualResetEventSlim exited = new ManualResetEventSlim(false))
            {
                bool timedOut = false;
                Stopwatch elapsed = Stopwatch.StartNew();
                try
                {
                    try { BoundedRead.Run(delegate {
                        entered.Set();
                        try { release.Wait(); return 1; } finally { exited.Set(); }
                    }, stop, TimeSpan.FromMilliseconds(180)); }
                    catch (TimeoutException error) { timedOut = error.Message.Contains("已停止"); }
                    Check(timedOut && stop.IsSet, "Timeout did not stop the session with an actionable reason.");
                    Check(entered.IsSet && !exited.IsSet, "The timeout did not bound a blocked provider.");
                    Check(elapsed.Elapsed.TotalSeconds < 1.5, "Timeout waited too long for the provider.");
                }
                finally { release.Set(); Check(exited.Wait(2000), "Timed out provider did not finish after release."); }
            }
        });

        Case("cancellation raised by the completing reader takes priority over its result", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            {
                bool cancelled = false;
                try { BoundedRead.Run(delegate { stop.Set(); return 42; }, stop, TimeSpan.FromSeconds(2)); }
                catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "The completed value bypassed cancellation.");
            }
        });

        Case("invalid timeout is rejected before invoking the reader", delegate {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            {
                int calls = 0;
                bool rejected = false;
                try { BoundedRead.Run(delegate { calls++; return 1; }, stop, TimeSpan.Zero); }
                catch (ArgumentOutOfRangeException) { rejected = true; }
                Check(rejected && calls == 0, "An invalid deadline started the provider.");
            }
        });

        Console.WriteLine("BOUNDED READ RESULT " + passed + " passed, " + failed + " failed");
        return failed;
    }
}
