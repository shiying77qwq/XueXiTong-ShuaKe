using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace StudyVideoHelper
{
    internal static class GuiTests
    {
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr word, IntPtr parameter);

        private sealed class Driver : IStudyDriver
        {
            internal readonly ManualResetEventSlim Stop;
            internal readonly ManualResetEventSlim ReadEntered = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim ReadRelease = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim ReadExited = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim WaitEntered = new ManualResetEventSlim(false);
            internal readonly ManualResetEventSlim WaitExited = new ManualResetEventSlim(false);
            private readonly bool slowRead;
            private readonly bool completedStart;
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private int waits;
            internal int Actions;
            public bool Cancelled { get { return Stop.IsSet; } }
            public double Seconds { get { return clock.Elapsed.TotalSeconds; } }

            internal Driver(ManualResetEventSlim stop, bool slowRead, bool completedStart)
            { Stop = stop; this.slowRead = slowRead; this.completedStart = completedStart; }

            public void Guard() { if (Stop.IsSet) throw new OperationCanceledException(); }
            public bool Wait(double seconds)
            {
                Guard();
                // HelperForm's initial startup delay is not the session wait under test.
                if (Interlocked.Increment(ref waits) == 1) return !Stop.IsSet;
                WaitEntered.Set();
                try
                {
                    if (!Stop.Wait(10000)) throw new TimeoutException("GUI test did not stop the waiting session.");
                    return false;
                }
                finally { WaitExited.Set(); }
            }

            public CourseSnapshot Capture(bool full)
            {
                Guard();
                if (slowRead)
                    return BoundedRead.Run(delegate {
                        Check(Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA, "Slow read is not MTA.");
                        ReadEntered.Set();
                        try { ReadRelease.Wait(); return Page(); }
                        finally { ReadExited.Set(); }
                    }, Stop, TimeSpan.FromSeconds(20));
                return Page();
            }

            private CourseSnapshot Page()
            {
                CourseSnapshot page = new CourseSnapshot { CurrentTitle = "测试视频" };
                page.Chapters.Add(new Chapter { Number = "1.1", Title = "测试视频", Pending = true,
                    Node = new UiNode { Name = "测试视频", Role = "ListItem" } });
                page.Playback = new Playback { Mark = completedStart ? TaskMark.Complete : TaskMark.Incomplete, TaskCount = 1, PlayerCount = 1,
                    Position = 10, Duration = 600, Playing = true, CardIdentity = "test-card" };
                return page;
            }

            public void Press(UiNode node)
            { Interlocked.Increment(ref Actions); throw new Exception("GUI cancellation tests must not click a player or chapter."); }
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        private static IntPtr ButtonHandle(HelperForm form, string name)
        { return (IntPtr)form.Invoke(new Func<IntPtr>(delegate { return form.Controls[name].Handle; })); }

        private static void Click(HelperForm form, string name)
        { SendMessage(ButtonHandle(form, name), 0x00F5, IntPtr.Zero, IntPtr.Zero); }

        private static bool IsReady(HelperForm form)
        {
            return (bool)form.Invoke(new Func<bool>(delegate {
                return form.Controls["start"].Enabled && form.Controls["diagnose"].Enabled && form.Controls["verify"].Enabled && !form.Controls["stop"].Enabled;
            }));
        }

        private static void ExpectBusy(HelperForm form)
        {
            bool busy = (bool)form.Invoke(new Func<bool>(delegate {
                return !form.Controls["start"].Enabled && !form.Controls["diagnose"].Enabled && !form.Controls["verify"].Enabled && form.Controls["stop"].Enabled;
            }));
            Check(busy, "Starting a session did not put the toolbar into its busy state.");
        }

        private static void WaitReady(HelperForm form)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (!IsReady(form) && elapsed.Elapsed.TotalSeconds < 1.5) Thread.Sleep(10);
            Check(IsReady(form), "The toolbar did not return to ready promptly after cancellation.");
        }

        private static void Cleanup(Driver driver)
        {
            driver.Stop.Set();
            driver.ReadRelease.Set();
            if (driver.ReadEntered.IsSet) Check(driver.ReadExited.Wait(2000), "A released test reader did not exit.");
        }

        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            int passed = 0;
            int failed = 0;
            bool requestedSlowRead = false;
            bool requestedCompletedStart = false;
            Driver active = null;
            List<Driver> drivers = new List<Driver>();
            string logs = Path.Combine(Path.GetTempPath(), "study-helper-gui-tests-" + Guid.NewGuid().ToString("N"));
            Thread worker = null;
            using (HelperForm form = new HelperForm(delegate(ManualResetEventSlim stop) {
                active = new Driver(stop, requestedSlowRead, requestedCompletedStart);
                drivers.Add(active);
                return active;
            }, logs))
            {
                // Exercise the real WinForms window/message loop without taking the user's foreground.
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new System.Drawing.Point(-30000, -30000);
                form.Shown += delegate {
                    worker = new Thread(delegate() {
                        try
                        {
                            Click(form, "start");
                            Driver waiting = active;
                            ExpectBusy(form);
                            Check(waiting.WaitEntered.Wait(2000), "The session did not reach its playback wait.");
                            Click(form, "stop");
                            WaitReady(form);
                            Check(waiting.Stop.IsSet && waiting.WaitExited.Wait(1000) && waiting.Actions == 0,
                                "Stop button did not cancel the waiting session without actions.");
                            passed++; Console.WriteLine("PASS GUI: start/stop button messages control a waiting session and restore toolbar state");

                            form.Invoke(new Action(delegate { requestedSlowRead = true; }));
                            Click(form, "start");
                            Driver slowButton = active;
                            Check(!object.ReferenceEquals(slowButton, waiting), "The ready form could not start a new session.");
                            ExpectBusy(form);
                            Check(slowButton.ReadEntered.Wait(2000), "The restarted session did not enter a slow read.");
                            Stopwatch stopButtonTime = Stopwatch.StartNew();
                            Click(form, "stop");
                            WaitReady(form);
                            Check(stopButtonTime.Elapsed.TotalSeconds < 1.5 && !slowButton.ReadExited.IsSet && slowButton.Actions == 0,
                                "Stop button waited for the provider or allowed a later action.");
                            Cleanup(slowButton);
                            passed++; Console.WriteLine("PASS GUI: stop button promptly cancels a blocked capture and permits restart");

                            Click(form, "start");
                            Driver slowHotkey = active;
                            Check(!object.ReferenceEquals(slowHotkey, slowButton), "The form could not restart after a cancelled read.");
                            ExpectBusy(form);
                            Check(slowHotkey.ReadEntered.Wait(2000), "The hotkey test did not enter a slow read.");
                            Stopwatch hotkeyTime = Stopwatch.StartNew();
                            SendMessage(form.Handle, 0x0312, new IntPtr(120), new IntPtr((0x7B << 16) | 0x0003));
                            WaitReady(form);
                            Check(hotkeyTime.Elapsed.TotalSeconds < 1.5 && slowHotkey.Stop.IsSet && !slowHotkey.ReadExited.IsSet && slowHotkey.Actions == 0,
                                "F12 hotkey routing did not cancel the blocked reader promptly.");
                            Cleanup(slowHotkey);
                            passed++; Console.WriteLine("PASS GUI: WM_HOTKEY F12 route cancels a blocked capture and restores ready state");

                            form.Invoke(new Action(delegate { requestedSlowRead = false; requestedCompletedStart = true; }));
                            Click(form, "verify");
                            Driver rejectedVerification = active;
                            Check(!object.ReferenceEquals(rejectedVerification, slowHotkey), "The verification button did not create its session.");
                            WaitReady(form);
                            string log = (string)form.Invoke(new Func<string>(delegate { return form.Controls["log"].Text; }));
                            Check(log.Contains("实测起点必须是明确未完成的视频") && rejectedVerification.Actions == 0 &&
                                !rejectedVerification.WaitEntered.IsSet,
                                "Verification button did not route to the bounded verification entry or acted on a completed start.");
                            passed++; Console.WriteLine("PASS GUI: verification button routes to the finite verifier and rejects a completed start without actions");

                            form.Invoke(new Action(delegate { requestedCompletedStart = false; }));
                            Click(form, "start");
                            Driver closing = active;
                            Check(!object.ReferenceEquals(closing, rejectedVerification), "The form could not restart after verification rejection.");
                            Check(closing.WaitEntered.Wait(2000), "The closing test did not reach its playback wait.");
                            form.Invoke(new Action(form.Close));
                            Check(closing.Stop.IsSet && closing.WaitExited.Wait(1000) && closing.Actions == 0,
                                "Closing the form did not cancel its worker without further actions.");
                            passed++; Console.WriteLine("PASS GUI: closing the restarted form cancels its active session");
                        }
                        catch (Exception error) { failed++; Console.WriteLine("FAIL GUI: " + error); }
                        finally
                        {
                            foreach (Driver driver in drivers) Cleanup(driver);
                            if (!form.IsDisposed && form.IsHandleCreated)
                                try { form.BeginInvoke(new Action(form.Close)); } catch (InvalidOperationException) { }
                        }
                    });
                    worker.IsBackground = true;
                    worker.SetApartmentState(ApartmentState.MTA);
                    worker.Start();
                };
                Application.Run(form);
                if (worker != null && !worker.Join(5000)) { failed++; Console.WriteLine("FAIL GUI: test worker did not finish."); }
            }
            Console.WriteLine("GUI RESULT " + passed + " passed, " + failed + " failed");
            return failed == 0 && passed == 5 ? 0 : 1;
        }
    }
}
