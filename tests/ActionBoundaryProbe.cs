using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    // Explicit one-action diagnostic. It never retries Press or touches a media button.
    internal static class ActionBoundaryProbe
    {
        private const string CurrentTitle = "统筹发展和安全的科学内涵";
        private const string NextTitle = "更好统筹发展和安全的途径和方法";
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetThreadDpiAwarenessContext();
        [DllImport("user32.dll")] private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr value);

        private static string Safe(string value)
        {
            string text = Regex.Replace(value ?? "", @"(?i)\b(?:https?|file)://[^\s]+", "<URL omitted>")
                .Replace("\r", "\\r").Replace("\n", "\\n");
            return text.Length <= 220 ? text : text.Substring(0, 220) + "...";
        }

        private static void Report(string line)
        {
            Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [thread=" +
                Thread.CurrentThread.ManagedThreadId + ",apartment=" + Thread.CurrentThread.GetApartmentState() +
                "] " + Safe(line));
        }

        private static string RuntimeId(AutomationElement element)
        {
            int[] values = element.GetRuntimeId();
            return values == null ? "<none>" :
                string.Join(",", values.Take(24).Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray());
        }

        private static void DescribeBoundary(string phase, UiNode node, IntPtr app, ManualResetEventSlim stop)
        {
            BoundedRead.Run(delegate {
                AutomationElement target = node.Native as AutomationElement;
                AutomationElement.AutomationElementInformation current = target.Current;
                Rect bounds = current.BoundingRectangle;
                Report(phase + " TARGET Name=" + current.Name + "; Role=" + current.ControlType.ProgrammaticName);
                Report(phase + " TARGET Bounds=" + bounds.ToString(CultureInfo.InvariantCulture) +
                    "; Enabled=" + current.IsEnabled + "; Offscreen=" + current.IsOffscreen +
                    "; RuntimeId=" + RuntimeId(target));
                Point point = new Point(Math.Floor(bounds.Left + bounds.Width / 2),
                    Math.Floor(bounds.Top + bounds.Height / 2));
                Rect appBounds = AutomationElement.FromHandle(app).Current.BoundingRectangle;
                Report(phase + " AppBounds=" + appBounds.ToString(CultureInfo.InvariantCulture) +
                    "; ExpectedProductionPoint=" + point.ToString(CultureInfo.InvariantCulture));
                if (current.IsOffscreen || bounds.IsEmpty || !appBounds.Contains(point))
                {
                    Report(phase + " SKIP POINT: target offscreen or center outside application.");
                    return false;
                }
                Native.POINT nativePoint = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
                Report(phase + " WindowFromPointRoot=" +
                    Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2).ToInt64() +
                    "; ExpectedApp=" + app.ToInt64());
                AutomationElement hit = AutomationElement.FromPoint(point);
                bool strictMatch = false;
                for (int depth = 0; hit != null && depth < 30; depth++)
                {
                    if (stop.IsSet) throw new OperationCanceledException();
                    AutomationElement.AutomationElementInformation info = hit.Current;
                    bool matches = Automation.Compare(hit, target);
                    Report(phase + " HIT[" + depth + "] Name=" + info.Name + "; Role=" +
                        info.ControlType.ProgrammaticName);
                    Report(phase + " HIT[" + depth + "] Bounds=" + info.BoundingRectangle.ToString(CultureInfo.InvariantCulture) +
                        "; RuntimeId=" + RuntimeId(hit) + "; MatchesTarget=" + matches);
                    strictMatch |= matches;
                    hit = TreeWalker.RawViewWalker.GetParent(hit);
                }
                Report(phase + " STRICT_MATCH=" + strictMatch);
                return strictMatch;
            }, stop, TimeSpan.FromSeconds(15));
        }

        private static void Snapshot(string label, CourseSnapshot snapshot)
        {
            Report(label + " CurrentTitle=" + snapshot.CurrentTitle + "; Error=" + snapshot.Error);
            Report(label + " Mark=" + snapshot.Playback.Mark + "; Playing=" + snapshot.Playback.Playing +
                "; Tasks=" + snapshot.Playback.TaskCount + "; Players=" + snapshot.Playback.PlayerCount +
                "; Clock=" + VideoPolicy.Clock(snapshot.Playback.Position) + "/" + VideoPolicy.Clock(snapshot.Playback.Duration));
        }

        private static int Run()
        {
            using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
            {
                IntPtr app = WindowsStudyDriver.FindWindow();
                WindowsStudyDriver driver = new WindowsStudyDriver(app, stop);
                driver.Trace = Report;
                Report("ACTIVATE: explicit one-action probe; no media action or retry.");
                driver.Activate();
                driver.Guard();
                try
                {
                    Report("DPI Window=" + GetDpiForWindow(app) + "; ThreadAwareness=" +
                        GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()));
                }
                catch (EntryPointNotFoundException) { Report("DPI APIs unavailable."); }
                CourseSnapshot before = driver.Capture(true);
                driver.Guard();
                Snapshot("BEFORE", before);
                if (before.Error != "" || before.CurrentTitle != CurrentTitle ||
                    before.Playback.Mark != TaskMark.Complete || before.Playback.Playing ||
                    before.Playback.TaskCount != 1 || before.Playback.PlayerCount != 1)
                    throw new InvalidOperationException("Expected the unique completed, paused 国家安全教育3.2 page; no Press.");
                Chapter[] current = before.Chapters.Where(c => c.Number == "3.2" && c.Title == CurrentTitle && !c.IsQuiz).ToArray();
                if (current.Length != 1 ||
                    !before.Chapters.Any(c => c.Number == "1.1" && c.Title == "总体国家安全观的创立"))
                    throw new InvalidOperationException("Expected safety course directory; no Press.");
                Chapter next = before.Chapters.Skip(before.Chapters.IndexOf(current[0]) + 1).FirstOrDefault(c => c.Pending && !c.IsQuiz);
                if (next == null || next.Number != "3.3" || next.Title != NextTitle || next.Node == null ||
                    next.Node.Role != "Text" || !(next.Node.Native is AutomationElement))
                    throw new InvalidOperationException("The immediate pending non-quiz target is not exact chapter3.3; no Press.");
                // Do not pre-hit-test here: doing so could warm the provider or hide
                // the transient failure this immediate production action must reproduce.
                Report("BEFORE_PRESS CachedName=" + next.Node.Name + "; CachedRole=" + next.Node.Role);
                driver.Guard();
                Exception pressFailure = null;
                Report("PRESS_ATTEMPT=1: production WindowsStudyDriver.Press on exact chapter3.3; no further Press will occur.");
                try { driver.Press(next.Node); Report("PRESS_RETURNED=True"); }
                catch (Exception error) { pressFailure = error; Report("PRESS_RETURNED=False; " + error.GetType().Name + ":" + error.Message); }
                if (stop.IsSet) { Report("STOPPED: no further reads or actions."); return 1; }
                driver.Guard();
                try { DescribeBoundary("AFTER_PRESS", next.Node, app, stop); }
                catch (Exception error)
                {
                    Report("AFTER_PRESS boundary read failed: " + error.GetType().Name + ":" + error.Message);
                    if (stop.IsSet) return 1;
                }
                if (!driver.Wait(0.8)) throw new OperationCanceledException();
                driver.Guard();
                CourseSnapshot after = driver.Capture(true);
                driver.Guard();
                Snapshot("AFTER", after);
                Report("COMPLETE: one Press attempt, zero media button actions, zero retries.");
                return pressFailure == null && after.Error == "" && after.Fingerprint == before.Fingerprint &&
                    after.CurrentTitle == NextTitle ? 0 : 4;
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length != 1 || args[0] != "--press-next-3.3-once")
            {
                Console.WriteLine("Usage: ActionBoundaryProbe.exe --press-next-3.3-once");
                Console.WriteLine("Explicitly activates the app, requires completed paused chapter3.2, and calls production Press on chapter3.3 exactly once.");
                return args.Length == 1 && args[0] == "--help" ? 0 : 2;
            }
            try
            {
                Native.SetProcessDPIAware();
                Report("START: STA entrypoint and LongRunning MTA worker mirror the production GUI session.");
                return Task.Factory.StartNew<int>(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Result;
            }
            catch (Exception error)
            {
                Exception cause = error.GetBaseException();
                Report("FAILED " + cause.GetType().Name + ":" + cause.Message);
                return 1;
            }
        }
    }
}
