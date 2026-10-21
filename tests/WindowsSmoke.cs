using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;

namespace StudyVideoHelper
{
    internal static class WindowsSmoke
    {
        private sealed class CandidateFixture : NativeWindow, IDisposable
        {
            internal CandidateFixture(int extendedStyle, int top)
            {
                CreateHandle(new CreateParams { Caption = "", X = 10, Y = top, Width = 182, Height = 32,
                    Style = unchecked((int)0x80000000), ExStyle = extendedStyle, Parent = IntPtr.Zero });
                Native.ShowWindow(Handle, 4); // Show the actual unowned native window without activating it.
            }
            public void Dispose() { DestroyHandle(); }
        }

        private static void CheckCandidateFixture(IntPtr handle, long expectedStyle)
        {
            if (!Native.IsWindowVisible(handle) || Native.GetWindow(handle, 4) != IntPtr.Zero ||
                (Native.GetWindowLongPtr(handle, -20).ToInt64() & 0x08000080L) != expectedStyle)
                throw new Exception("The auxiliary-window regression fixture has unexpected visibility, owner or extended style.");
        }

        private static void GeometryRequire(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        // Pure geometry checks can also run alone without creating a window or sending input.
        internal static int RunChapterScrollGeometryTests()
        {
            int failures = 0, passed = 0;
            Action<string, Action> check = delegate(string name, Action test) {
                try { test(); passed++; Console.WriteLine("PASS scroll geometry " + name); }
                catch (Exception error) { failures++; Console.WriteLine("FAIL scroll geometry " + name + ": " + error.Message); }
            };
            System.Windows.Rect capturedViewport = new System.Windows.Rect(2292, 418, 438, 765);
            check("clips captured tall directory to its parent and application", delegate {
                System.Windows.Rect viewport;
                bool valid = ChapterScrollGeometry.TryViewport(new System.Windows.Rect(2292, 418, 438, 2761),
                    new System.Windows.Rect(2292, 418, 450, 802), new System.Windows.Rect(955, 58, 1800, 1125), out viewport);
                GeometryRequire(valid && viewport == capturedViewport,
                    "The 2761px directory must be clipped to the independently captured 765px visible area.");
            });
            check("intersects all three rectangles on both axes", delegate {
                System.Windows.Rect viewport;
                bool valid = ChapterScrollGeometry.TryViewport(new System.Windows.Rect(0, 0, 1000, 2000),
                    new System.Windows.Rect(100, 200, 300, 400), new System.Windows.Rect(150, 250, 200, 500), out viewport);
                GeometryRequire(valid && viewport == new System.Windows.Rect(150, 250, 200, 350),
                    "Neither the raw parent nor the application bounds alone define the viewport.");
            });
            check("rejects disjoint or merely touching viewports", delegate {
                System.Windows.Rect viewport;
                GeometryRequire(!ChapterScrollGeometry.TryViewport(new System.Windows.Rect(0, 0, 100, 100),
                    new System.Windows.Rect(120, 0, 100, 100), new System.Windows.Rect(0, 0, 500, 500), out viewport),
                    "Disjoint directory and parent cannot produce a wheel target.");
                GeometryRequire(!ChapterScrollGeometry.TryViewport(new System.Windows.Rect(0, 0, 100, 100),
                    new System.Windows.Rect(100, 0, 100, 100), new System.Windows.Rect(0, 0, 500, 500), out viewport),
                    "A zero-width touching edge cannot produce a wheel target.");
            });
            check("rejects empty nonfinite and too-small viewports", delegate {
                System.Windows.Rect viewport;
                System.Windows.Rect bounds = new System.Windows.Rect(0, 0, 100, 100);
                GeometryRequire(!ChapterScrollGeometry.TryViewport(System.Windows.Rect.Empty, bounds, bounds, out viewport), "Empty bounds accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryViewport(new System.Windows.Rect(double.NaN, 0, 50, 50), bounds, bounds, out viewport), "NaN bounds accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryViewport(new System.Windows.Rect(0, 0, double.PositiveInfinity, 50), bounds, bounds, out viewport), "Infinite bounds accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryViewport(new System.Windows.Rect(0, 0, 15, 100), bounds, bounds, out viewport), "A 15px wheel viewport accepted.");
            });
            check("orders an earlier target upward", delegate {
                GeometryRequire(ChapterScrollGeometry.Direction(1, new[] { 3, 4, 5 }) == -1,
                    "An earlier chapter requires upward scrolling.");
            });
            check("orders a later target downward", delegate {
                GeometryRequire(ChapterScrollGeometry.Direction(8, new[] { 3, 4, 5 }) == 1,
                    "A later chapter requires downward scrolling.");
            });
            check("refuses unknown or inconsistent chapter ordering", delegate {
                GeometryRequire(ChapterScrollGeometry.Direction(-1, new[] { 3, 4, 5 }) == 0, "Unknown target has a direction.");
                GeometryRequire(ChapterScrollGeometry.Direction(8, null) == 0 && ChapterScrollGeometry.Direction(8, new int[0]) == 0, "Missing visible chapters have a direction.");
                GeometryRequire(ChapterScrollGeometry.Direction(8, new[] { -1, 4 }) == 0, "Invalid visible chapter has a direction.");
                GeometryRequire(ChapterScrollGeometry.Direction(8, new[] { 4, 3 }) == 0 && ChapterScrollGeometry.Direction(8, new[] { 3, 3 }) == 0,
                    "Unsorted or duplicate visible indices have a direction.");
            });
            check("does not guess a direction inside the visible range", delegate {
                GeometryRequire(ChapterScrollGeometry.Direction(3, new[] { 3, 4, 5 }) == 0 &&
                    ChapterScrollGeometry.Direction(5, new[] { 3, 4, 5 }) == 0 &&
                    ChapterScrollGeometry.Direction(4, new[] { 3, 5 }) == 0,
                    "Visible-range endpoints and gaps must not default to downward scrolling.");
            });
            check("accepts a fully visible title with an integer interior point", delegate {
                System.Windows.Point point;
                System.Windows.Rect title = new System.Windows.Rect(2310, 600, 200, 30);
                GeometryRequire(ChapterScrollGeometry.TryTargetPoint(title, capturedViewport, out point) &&
                    point == new System.Windows.Point(2410, 615) && title.Contains(point) && capturedViewport.Contains(point),
                    "The title click point must remain inside both live title and clipped viewport.");
            });
            check("rejects a top-clipped title even when its center is visible", delegate {
                System.Windows.Point point;
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(new System.Windows.Rect(2310, 410, 200, 30), capturedViewport, out point),
                    "A visible center is insufficient when the title extends above the viewport.");
            });
            check("rejects a bottom-clipped title even when its center is visible", delegate {
                System.Windows.Point point;
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(new System.Windows.Rect(2310, 1160, 200, 30), capturedViewport, out point),
                    "A visible center is insufficient when the title extends below the viewport.");
            });
            check("rejects horizontal clipping offscreen and invalid titles", delegate {
                System.Windows.Point point;
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(new System.Windows.Rect(2280, 600, 200, 30), capturedViewport, out point), "Left-clipped title accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(new System.Windows.Rect(2600, 600, 200, 30), capturedViewport, out point), "Right-clipped title accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(new System.Windows.Rect(2310, 1300, 200, 30), capturedViewport, out point), "Offscreen title accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(System.Windows.Rect.Empty, capturedViewport, out point), "Empty title accepted.");
                GeometryRequire(!ChapterScrollGeometry.TryTargetPoint(new System.Windows.Rect(double.NaN, 600, 200, 30), capturedViewport, out point), "Nonfinite title accepted.");
            });
            Console.WriteLine("SCROLL GEOMETRY RESULT " + passed + " passed, " + failures + " failed");
            return failures;
        }

        internal static int RunStrictHitGeometryTests()
        {
            int failures = 0, passed = 0;
            Action<string, Action> check = delegate(string name, Action test) {
                try { test(); passed++; Console.WriteLine("PASS strict hit geometry " + name); }
                catch (Exception error) { failures++; Console.WriteLine("FAIL strict hit geometry " + name + ": " + error.Message); }
            };
            System.Windows.Rect title = new System.Windows.Rect(120, 230, 320, 28);
            System.Windows.Rect viewport = new System.Windows.Rect(100, 200, 600, 300);
            check("finite unique physical candidates stay inside title and viewport", delegate {
                var points = WindowsStudyDriver.StrictTargetCandidates(title, viewport, null);
                GeometryRequire(points.Length > 1 && points.Length <= 16 && points.Distinct().Count() == points.Length,
                    "Candidate search must be nonempty, deduplicated and finitely bounded.");
                GeometryRequire(points.All(point => point.X == Math.Floor(point.X) && point.Y == Math.Floor(point.Y) &&
                    title.Contains(point) && viewport.Contains(point)), "A candidate left its live title/viewport or used fractional coordinates.");
            });
            check("retains provider evidence and a small left glyph candidate", delegate {
                System.Windows.Point provider = new System.Windows.Point(121, 231);
                var points = WindowsStudyDriver.StrictTargetCandidates(title, viewport, provider);
                GeometryRequire(points.Contains(provider), "A valid provider point was discarded.");
                GeometryRequire(points.Any(point => point.X > title.Left && point.X <= title.Left + 8 &&
                    point.Y >= title.Top && point.Y < title.Bottom), "The wide title has no candidate in its small left glyph area.");
            });
            check("does not trust out-of-bounds or nonfinite provider points", delegate {
                foreach (System.Windows.Point invalid in new[] { new System.Windows.Point(119, 240),
                    new System.Windows.Point(500, 240), new System.Windows.Point(double.NaN, 240),
                    new System.Windows.Point(130, double.PositiveInfinity) })
                {
                    var points = WindowsStudyDriver.StrictTargetCandidates(title, viewport, invalid);
                    GeometryRequire(points.Length > 0 && points.All(point => title.Contains(point) && viewport.Contains(point)),
                        "Invalid provider evidence contaminated the bounded candidate set.");
                }
            });
            check("refuses a title clipped by its allowed viewport", delegate {
                GeometryRequire(WindowsStudyDriver.StrictTargetCandidates(title,
                    new System.Windows.Rect(140, 200, 600, 300), null).Length == 0,
                    "Partially visible title supplied pointer candidates.");
            });
            check("refuses invalid rectangles and nonphysical coordinates", delegate {
                foreach (System.Windows.Rect invalid in new[] { System.Windows.Rect.Empty,
                    new System.Windows.Rect(double.NaN, 0, 100, 20), new System.Windows.Rect(0, 0, double.PositiveInfinity, 20),
                    new System.Windows.Rect((double)int.MaxValue + 100, 0, 100, 20) })
                    GeometryRequire(WindowsStudyDriver.StrictTargetCandidates(invalid, viewport, null).Length == 0,
                        "Invalid target bounds supplied candidates.");
                GeometryRequire(WindowsStudyDriver.StrictTargetCandidates(title, System.Windows.Rect.Empty, null).Length == 0,
                    "Invalid allowed bounds supplied candidates.");
            });
            check("deduplicates a small valid title without unbounded retries", delegate {
                var points = WindowsStudyDriver.StrictTargetCandidates(new System.Windows.Rect(120, 230, 2, 2), viewport,
                    new System.Windows.Point(121, 231));
                GeometryRequire(points.Length > 0 && points.Length <= 4 && points.Distinct().Count() == points.Length,
                    "A 2px title produced duplicate or excessive physical candidates.");
            });
            Console.WriteLine("STRICT HIT GEOMETRY RESULT " + passed + " passed, " + failures + " failed");
            return failures;
        }

        private sealed class HitInputCounter : IMessageFilter
        {
            internal IntPtr Root;
            internal int Buttons;
            public bool PreFilterMessage(ref Message message)
            {
                if ((message.Msg == 0x0201 || message.Msg == 0x0202 || message.Msg == 0x0203) &&
                    Native.GetAncestor(message.HWnd, 2) == Root) Interlocked.Increment(ref Buttons);
                return false;
            }
        }

        private sealed class StrictHitFixture : Form
        {
            internal readonly Panel Viewport = new Panel();
            internal readonly Label Title = new Label();
            internal readonly Panel CenterBlank = new Panel();
            internal readonly Panel Overlay = new Panel();
            internal int Clicks;
            internal int ReadClicks() { return Volatile.Read(ref Clicks); }
            internal StrictHitFixture()
            {
                Text = "Study helper native strict-hit fixture";
                Size = new Size(640, 270);
                StartPosition = FormStartPosition.CenterScreen;
                Viewport.Name = "fixtureDirectory";
                Viewport.AccessibleName = "fixture directory wrapper";
                Viewport.Bounds = new Rectangle(20, 20, 580, 190);
                Viewport.BackColor = Color.WhiteSmoke;
                Title.Name = "fixtureChapterTitle";
                Title.AccessibleName = "1.2 unique fixture chapter title";
                Title.AccessibleRole = AccessibleRole.StaticText;
                Title.Text = "1.2 title";
                Title.AutoSize = false;
                Title.Bounds = new Rectangle(20, 50, 320, 28);
                Title.BackColor = Color.LightBlue;
                Title.Click += delegate { Interlocked.Increment(ref Clicks); };
                // A real sibling partially occludes the wide label. UIA ignores a
                // WinForms Region for hit testing, so a Region is not a valid fixture.
                // This proves finite strict search around partial occlusion; it does
                // not claim to reproduce Chromium returning an ancestor wrapper.
                CenterBlank.Name = "fixtureCenterOccluder";
                CenterBlank.AccessibleName = "partial title occlusion";
                CenterBlank.AccessibleRole = AccessibleRole.Pane;
                CenterBlank.Bounds = new Rectangle(100, 50, 240, 28);
                CenterBlank.BackColor = Viewport.BackColor;
                Overlay.Name = "fixtureOpaqueOverlay";
                Overlay.AccessibleName = "opaque same-window overlay";
                Overlay.Bounds = Title.Bounds;
                Overlay.BackColor = Color.DarkRed;
                Overlay.Visible = false;
                Overlay.Click += delegate { Interlocked.Increment(ref Clicks); };
                Viewport.Controls.Add(Title);
                Viewport.Controls.Add(CenterBlank);
                Viewport.Controls.Add(Overlay);
                CenterBlank.BringToFront();
                Controls.Add(Viewport);
            }
            internal void OnUi(Action action)
            { if (InvokeRequired) Invoke(action); else action(); }
            internal void ResetLayout()
            {
                OnUi(delegate { Overlay.Hide(); Title.Location = new Point(20, 50); Overlay.Bounds = Title.Bounds;
                    CenterBlank.Bounds = new Rectangle(Title.Left + 80, Title.Top, Title.Width - 80, Title.Height);
                    CenterBlank.Show(); CenterBlank.BringToFront(); });
            }
        }

        private static bool WaitForFixtureForeground(IntPtr handle, int milliseconds)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.ElapsedMilliseconds < milliseconds)
            {
                Native.SetForegroundWindow(handle);
                if (Native.GetAncestor(Native.GetForegroundWindow(), 2) == handle) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        // Exercises the real production selector/verifier and real UIA providers.
        // It never calls Press or SendInput. It is not a full course/Chromium fixture.
        // A foreground-denied run fails explicitly; it must not become a fake PASS.
        internal static int RunStrictHitNativeTests()
        {
            int failures = 0, passed = 0, unavailable = 0;
            using (StrictHitFixture fixture = new StrictHitFixture())
            {
                IntPtr handle = fixture.Handle;
                HitInputCounter input = new HitInputCounter { Root = handle };
                Application.AddMessageFilter(input);
                fixture.Shown += delegate {
                    Task.Factory.StartNew(delegate {
                        try
                        {
                            if (!WaitForFixtureForeground(handle, 12000))
                            {
                                unavailable = 1;
                                Console.WriteLine("REQUIRES INTERACTIVE: native strict-hit fixture could not become foreground; no partial-occlusion/overlay success is claimed.");
                                return;
                            }
                            AutomationElement target = AutomationElement.FromHandle(fixture.Title.Handle);
                            AutomationElement viewport = AutomationElement.FromHandle(fixture.Viewport.Handle);
                            AutomationElement partialOccluder = AutomationElement.FromHandle(fixture.CenterBlank.Handle);
                            UiNode node = new UiNode { Name = fixture.Title.AccessibleName, Role = "Text", Native = target };
                            ManualResetEventSlim stop = new ManualResetEventSlim(false);
                            WindowsStudyDriver driver = new WindowsStudyDriver(handle, stop);
                            Func<System.Windows.Rect> allowed = delegate { return viewport.Current.BoundingRectangle; };
                            Action<string, Action> check = delegate(string name, Action test) {
                                try { test(); passed++; Console.WriteLine("PASS native strict hit " + name); }
                                catch (Exception error) { failures++; Console.WriteLine("FAIL native strict hit " + name + ": " + error); }
                                finally { fixture.ResetLayout(); }
                            };
                            check("fixture has actual center occlusion and a strict visible left title hit", delegate {
                                System.Windows.Rect bounds = target.Current.BoundingRectangle;
                                System.Windows.Point center = new System.Windows.Point(Math.Floor(bounds.Left + bounds.Width / 2), Math.Floor(bounds.Top + bounds.Height / 2));
                                System.Windows.Point inset = new System.Windows.Point(Math.Floor(bounds.Left + 8), center.Y);
                                GeometryRequire(target.Current.ControlType == ControlType.Text && bounds.Width >= 300,
                                    "The native fixture does not expose the intended wide Text rectangle.");
                                GeometryRequire(Automation.Compare(AutomationElement.FromPoint(center), partialOccluder),
                                    "The title center did not hit the actual sibling occluder.");
                                GeometryRequire(Automation.Compare(AutomationElement.FromPoint(inset), target),
                                    "The title inset did not strictly hit the unique Text target.");
                            });
                            check("production search selects a strict inset without pointer input", delegate {
                                System.Windows.Point point = driver.SelectStrictTargetPoint(node, allowed);
                                GeometryRequire(Automation.Compare(AutomationElement.FromPoint(point), target) &&
                                    driver.VerifyStrictTargetPoint(node, allowed, point, "fixture-selected"),
                                    "Production search did not select and confirm an exact title hit.");
                                GeometryRequire(Volatile.Read(ref input.Buttons) == 0 && fixture.ReadClicks() == 0,
                                    "Read-only target selection produced pointer input.");
                            });
                            check("same-window opaque overlay rejects every candidate with zero input", delegate {
                                fixture.OnUi(delegate { fixture.Overlay.AccessibleName = "opaque same-window overlay"; fixture.Overlay.AccessibleRole = AccessibleRole.Pane; fixture.Overlay.Show(); fixture.Overlay.BringToFront(); });
                                bool blocked = false;
                                try { driver.SelectStrictTargetPoint(node, allowed); } catch (InvalidOperationException) { blocked = true; }
                                GeometryRequire(blocked && Volatile.Read(ref input.Buttons) == 0 && fixture.ReadClicks() == 0,
                                    "A same-window overlay permitted a pointer candidate or input.");
                            });
                            check("same-name Text overlay is not the target and produces zero input", delegate {
                                fixture.OnUi(delegate { fixture.Overlay.AccessibleName = node.Name; fixture.Overlay.AccessibleRole = AccessibleRole.StaticText; fixture.Overlay.Show(); fixture.Overlay.BringToFront(); });
                                System.Windows.Rect bounds = target.Current.BoundingRectangle;
                                var physical = new Native.POINT { X = (int)bounds.Left + 8, Y = (int)(bounds.Top + bounds.Height / 2) };
                                AutomationElement hit = AutomationElement.FromPoint(new System.Windows.Point(physical.X, physical.Y));
                                GeometryRequire(Native.GetAncestor(Native.WindowFromPoint(physical), 2) == handle &&
                                    hit.Current.Name == node.Name && !Automation.Compare(hit, target),
                                    "The same-window same-name negative fixture was not established.");
                                bool blocked = false;
                                try { driver.SelectStrictTargetPoint(node, allowed); } catch (InvalidOperationException) { blocked = true; }
                                GeometryRequire(blocked && Volatile.Read(ref input.Buttons) == 0 && fixture.ReadClicks() == 0,
                                    "Name equality or a common window was mistaken for exact target identity.");
                            });
                            check("layout shift invalidates the selected point and permits fresh finite selection", delegate {
                                System.Windows.Point oldPoint = driver.SelectStrictTargetPoint(node, allowed);
                                fixture.OnUi(delegate { fixture.Title.Location = new Point(20, 110);
                                    fixture.CenterBlank.Bounds = new Rectangle(fixture.Title.Left + 80, fixture.Title.Top,
                                        fixture.Title.Width - 80, fixture.Title.Height); fixture.CenterBlank.BringToFront(); });
                                GeometryRequire(!driver.VerifyStrictTargetPoint(node, allowed, oldPoint, "fixture-shifted"),
                                    "A stale selected coordinate survived a live title layout shift.");
                                System.Windows.Point freshPoint = driver.SelectStrictTargetPoint(node, allowed);
                                GeometryRequire(freshPoint != oldPoint && driver.VerifyStrictTargetPoint(node, allowed, freshPoint, "fixture-reselected") &&
                                    Volatile.Read(ref input.Buttons) == 0 && fixture.ReadClicks() == 0,
                                    "Fresh selection did not recover without clicking a stale location.");
                            });
                            check("pre-cancelled selector never reads the allowed viewport", delegate {
                                ManualResetEventSlim cancelled = new ManualResetEventSlim(true);
                                WindowsStudyDriver stopped = new WindowsStudyDriver(handle, cancelled);
                                int reads = 0; bool blocked = false;
                                try { stopped.SelectStrictTargetPoint(node, delegate { Interlocked.Increment(ref reads); return allowed(); }); }
                                catch (OperationCanceledException) { blocked = true; }
                                GeometryRequire(blocked && reads == 0 && Volatile.Read(ref input.Buttons) == 0,
                                    "A stopped selector performed reads or pointer input.");
                            });
                            check("cancellation after selection prevents final verification and input", delegate {
                                System.Windows.Point point = driver.SelectStrictTargetPoint(node, allowed);
                                ManualResetEventSlim cancelled = new ManualResetEventSlim(true);
                                WindowsStudyDriver stopped = new WindowsStudyDriver(handle, cancelled);
                                bool blocked = false;
                                try { stopped.VerifyStrictTargetPoint(node, allowed, point, "fixture-cancelled"); }
                                catch (OperationCanceledException) { blocked = true; }
                                GeometryRequire(blocked && Volatile.Read(ref input.Buttons) == 0 && fixture.ReadClicks() == 0,
                                    "A cancelled final verification proceeded or produced input.");
                            });
                        }
                        catch (Exception error) { failures++; Console.WriteLine("FAIL native strict-hit setup: " + error); }
                        finally { fixture.BeginInvoke(new Action(fixture.Close)); }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                };
                try { Application.Run(fixture); }
                finally { Application.RemoveMessageFilter(input); }
            }
            Console.WriteLine("NATIVE STRICT HIT RESULT " + passed + " passed, " + failures + " failed, " + unavailable + " foreground unavailable");
            return failures + unavailable;
        }

        [STAThread]
        private static int Main()
        {
            if (RunChapterScrollGeometryTests() != 0) return 1;
            if (RunStrictHitGeometryTests() != 0) return 1;
            int result = 1;
            int clicks = 0;
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            using (CandidateFixture toolWindow = new CandidateFixture(0x80, 10))
            using (CandidateFixture noActivateWindow = new CandidateFixture(0x08000000, 50))
            using (CandidateFixture ordinaryWindow = new CandidateFixture(0, 90))
            using (Form form = new Form { Text = "Study helper UI Automation self-test", Size = new Size(430, 180), StartPosition = FormStartPosition.CenterScreen })
            {
                Button play = new Button { Text = "播放", Name = "fixturePlay", Location = new Point(28, 35), Size = new Size(150, 42) };
                play.Click += delegate { Interlocked.Increment(ref clicks); };
                form.Controls.Add(play);
                IntPtr handle = form.Handle;
                form.Shown += delegate {
                    Native.SetForegroundWindow(handle);
                    Task.Factory.StartNew(delegate {
                        try
                        {
                            Thread.Sleep(250);
                            ManualResetEventSlim stop = new ManualResetEventSlim(false);
                            WindowsStudyDriver driver = new WindowsStudyDriver(handle, stop);
                            if (!WindowsStudyDriver.FindVisibleWindows(new System.Collections.Generic.HashSet<int> { System.Diagnostics.Process.GetCurrentProcess().Id }).Contains(handle))
                                throw new Exception("Native process/window enumeration missed its own fixture.");
                            Console.WriteLine("PASS visible-window enumeration finds the exact fixture process");
                            CheckCandidateFixture(toolWindow.Handle, 0x80);
                            CheckCandidateFixture(noActivateWindow.Handle, 0x08000000);
                            CheckCandidateFixture(ordinaryWindow.Handle, 0);
                            var candidates = WindowsStudyDriver.FindVisibleWindows(new System.Collections.Generic.HashSet<int> { System.Diagnostics.Process.GetCurrentProcess().Id });
                            if (candidates.Contains(toolWindow.Handle))
                                throw new Exception("An unowned visible WS_EX_TOOLWINDOW was mistaken for a main application window.");
                            Console.WriteLine("PASS visible unowned tool windows are excluded from main-window candidates");
                            if (candidates.Contains(noActivateWindow.Handle))
                                throw new Exception("An unowned visible WS_EX_NOACTIVATE window was mistaken for a main application window.");
                            Console.WriteLine("PASS visible unowned no-activate windows are excluded from main-window candidates");
                            if (!candidates.Contains(ordinaryWindow.Handle) || candidates.Count != 2 || !candidates.Contains(handle))
                                throw new Exception("The ordinary blank-title window was discarded or the remaining candidate count is incorrect.");
                            Console.WriteLine("PASS genuine multiple ordinary windows remain distinct candidates even without a title");
                            if (!NativeAccessibility.TryReadClient(handle, NativeAccessibility.ClientObjectId))
                                throw new Exception("Standard MSAA AccessibleObjectFromWindow could not read the fixture client.");
                            Console.WriteLine("PASS standard MSAA client API reads the test-owned native fixture");
                            CourseSnapshot page = driver.Capture(true);
                            UiNode node = driver.LastTree.Walk().FirstOrDefault(n => n.Role == "Button" && n.Name == "播放");
                            if (node == null) throw new Exception("The real UIA cache did not expose the fixture play button.");
                            Console.WriteLine("PASS Windows UIA subtree cache and localized button properties");
                            if (page.Error == "") throw new Exception("A non-course fixture was accepted as a course.");
                            Console.WriteLine("PASS non-course native window is rejected by the parser");
                            Native.ShowWindow(handle, 6);
                            if (!Native.IsIconic(handle)) throw new Exception("The fixture did not minimize for the restore test.");
                            try { driver.Activate(); } catch (InvalidOperationException) { }
                            if (Native.IsIconic(handle)) throw new Exception("Activate left the target window minimized.");
                            Console.WriteLine("PASS starting restores a minimized target window");
                            if (Native.GetAncestor(Native.GetForegroundWindow(), 2) == handle)
                            {
                                driver.Press(node);
                                Console.WriteLine("PASS foreground native action accepted");
                            }
                            else
                            {
                                bool blocked = false;
                                try { driver.Press(node); } catch (InvalidOperationException) { blocked = true; }
                                if (!blocked || Volatile.Read(ref clicks) != 0) throw new Exception("A background driver was allowed to act.");
                                Console.WriteLine("PASS background native driver refuses to click");
                                // Exercise the exact Invoke API on this test-owned fixture only.
                                object pattern;
                                if (!((AutomationElement)node.Native).TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                                    throw new Exception("The fixture button has no Invoke pattern.");
                                ((InvokePattern)pattern).Invoke();
                            }
                            for (int i = 0; i < 20 && Volatile.Read(ref clicks) == 0; i++) Thread.Sleep(50);
                            if (Volatile.Read(ref clicks) != 1) throw new Exception("The play action did not invoke exactly one click.");
                            Console.WriteLine("PASS native UIA play-button action invokes exactly once");
                            stop.Set();
                            bool cancelled = false;
                            try { driver.Press(node); } catch (OperationCanceledException) { cancelled = true; }
                            if (!cancelled || Volatile.Read(ref clicks) != 1) throw new Exception("A stopped driver performed an action.");
                            Console.WriteLine("PASS stopped native driver cannot click");
                            result = 0;
                        }
                        catch (Exception error) { Console.WriteLine("FAIL Windows smoke: " + error); }
                        finally { form.BeginInvoke(new Action(form.Close)); }
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                };
                Application.Run(form);
            }
            return result == 0 ? RunStrictHitNativeTests() : result;
        }
    }
}
