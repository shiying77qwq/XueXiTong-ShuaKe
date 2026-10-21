using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using Forms = System.Windows.Forms;

namespace StudyVideoHelper
{
    internal static class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr handle);
        internal delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr handle, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong32(IntPtr handle, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr handle, int index);
        internal static IntPtr GetWindowLongPtr(IntPtr handle, int index)
        { return IntPtr.Size == 8 ? GetWindowLongPtr64(handle, index) : new IntPtr(GetWindowLong32(handle, index)); }
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr handle);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr handle, uint flags);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr handle, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr handle, ref POINT point);
        [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT
        { internal int dx, dy; internal uint mouseData, flags, time; internal UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION
        { [FieldOffset(0)] internal MOUSEINPUT mouse; }
        [StructLayout(LayoutKind.Sequential)] internal struct INPUT
        { internal uint type; internal INPUTUNION data; }
    }

    internal static class ChapterScrollGeometry
    {
        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool Valid(System.Windows.Rect rect)
        {
            return !rect.IsEmpty && Finite(rect.X) && Finite(rect.Y) && Finite(rect.Width) && Finite(rect.Height) &&
                Finite(rect.Right) && Finite(rect.Bottom) && rect.Width > 0 && rect.Height > 0;
        }

        internal static bool TryViewport(System.Windows.Rect directoryBounds, System.Windows.Rect rawParentBounds,
            System.Windows.Rect appBounds, out System.Windows.Rect viewport)
        {
            viewport = System.Windows.Rect.Empty;
            if (!Valid(directoryBounds) || !Valid(rawParentBounds) || !Valid(appBounds)) return false;
            System.Windows.Rect clipped = System.Windows.Rect.Intersect(directoryBounds, rawParentBounds);
            clipped.Intersect(appBounds);
            if (!Valid(clipped) || clipped.Width < 16 || clipped.Height < 16) return false;
            viewport = clipped;
            return true;
        }

        internal static int Direction(int targetIndex, int[] visibleIndices)
        {
            if (targetIndex < 0 || visibleIndices == null || visibleIndices.Length == 0) return 0;
            for (int i = 0; i < visibleIndices.Length; i++)
                if (visibleIndices[i] < 0 || (i > 0 && visibleIndices[i] <= visibleIndices[i - 1])) return 0;
            if (targetIndex < visibleIndices[0]) return -1;
            if (targetIndex > visibleIndices[visibleIndices.Length - 1]) return 1;
            return 0;
        }

        internal static bool TryTargetPoint(System.Windows.Rect targetBounds, System.Windows.Rect viewport,
            out System.Windows.Point point)
        {
            point = new System.Windows.Point();
            if (!Valid(targetBounds) || !Valid(viewport) || targetBounds.Width < 2 || targetBounds.Height < 2 ||
                targetBounds.Left < viewport.Left || targetBounds.Top < viewport.Top ||
                targetBounds.Right > viewport.Right || targetBounds.Bottom > viewport.Bottom) return false;
            double x = Math.Floor(targetBounds.Left + targetBounds.Width / 2);
            double y = Math.Floor(targetBounds.Top + targetBounds.Height / 2);
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) return false;
            point = new System.Windows.Point(x, y);
            return targetBounds.Contains(point) && viewport.Contains(point);
        }
    }

    public sealed class WindowsStudyDriver : IStudyDriver
    {
        private readonly IntPtr handle;
        private readonly ManualResetEventSlim stop;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private UiNode directory;
        private UiNode currentTitle;
        private UiNode card;
        private Chapter[] latestChapters = new Chapter[0];
        private UiNode latestDirectory;
        private bool accessibilityWarmupAttempted;
        public UiNode LastTree { get; private set; }
        public double LastCaptureSeconds { get; private set; }
        public string LastCaptureBreakdown { get; private set; }
        public Action<string> Trace { get; set; }
        public bool Cancelled { get { return stop.IsSet; } }
        public double Seconds { get { return clock.Elapsed.TotalSeconds; } }

        public WindowsStudyDriver(IntPtr handle, ManualResetEventSlim stop)
        { this.handle = handle; this.stop = stop; }

        public static IntPtr FindWindow()
        {
            HashSet<int> processes = new HashSet<int>();
            foreach (Process process in Process.GetProcessesByName("cxstudy"))
            {
                using (process) { processes.Add(process.Id); }
            }
            List<IntPtr> windows = FindVisibleWindows(processes);
            if (windows.Count == 0) throw new InvalidOperationException(processes.Count == 0 ?
                "没有找到学习通窗口。请先打开学习通 App，登录并进入目标课程的章节学习页。" :
                "学习通进程正在运行，但当前桌面没有可访问的主窗口。请恢复课程窗口；若仍失败，请在你的桌面直接双击本工具再试。");
            if (windows.Count != 1) throw new InvalidOperationException("找到多个学习通窗口，请只保留一个目标课程窗口再开始。");
            return windows[0];
        }

        internal static List<IntPtr> FindVisibleWindows(ISet<int> processIds)
        {
            List<IntPtr> windows = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr candidate, IntPtr unused) {
                uint processId;
                Native.GetWindowThreadProcessId(candidate, out processId);
                if (processIds.Contains((int)processId) && Native.IsWindowVisible(candidate) &&
                    Native.GetWindow(candidate, 4) == IntPtr.Zero &&
                    // Input methods may create visible unowned auxiliary windows in
                    // the app process. Keep only ordinary activatable app windows.
                    (Native.GetWindowLongPtr(candidate, -20).ToInt64() & 0x08000080L) == 0)
                    windows.Add(candidate);
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        public void Guard()
        {
            if (stop.IsSet) throw new OperationCanceledException();
            if (!Native.IsWindow(handle)) throw new InvalidOperationException("学习通窗口已关闭，已停止。");
            if (Native.IsIconic(handle)) throw new InvalidOperationException("学习通已最小化，已停止。请恢复窗口后重新开始。");
            if (Native.GetAncestor(Native.GetForegroundWindow(), 2) != handle)
                throw new InvalidOperationException("学习通不再是前台窗口，已停止。请处理弹窗或切回课程后重新开始。");
        }

        public void Activate()
        {
            if (stop.IsSet) throw new OperationCanceledException();
            if (Native.IsIconic(handle)) Native.ShowWindow(handle, 9);
            Native.SetForegroundWindow(handle);
            for (int attempt = 0; attempt < 20; attempt++)
            {
                if (Native.GetAncestor(Native.GetForegroundWindow(), 2) == handle) return;
                if (stop.Wait(50)) throw new OperationCanceledException();
            }
            throw new InvalidOperationException("Windows没有将焦点交给学习通。请把学习通留在前台，再按 Ctrl+Alt+F11 开始。");
        }

        public bool Wait(double seconds)
        {
            double end = Seconds + seconds;
            while (Seconds < end)
            {
                if (stop.Wait((int)Math.Max(1, Math.Min(200, (end - Seconds) * 1000)))) return false;
                Guard();
            }
            return !stop.IsSet;
        }

        private static CacheRequest Request(TreeScope scope)
        {
            CacheRequest request = new CacheRequest { TreeScope = scope, TreeFilter = Automation.RawViewCondition };
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.AutomationIdProperty);
            request.Add(AutomationElement.ControlTypeProperty);
            return request;
        }

        private static UiNode ConvertTree(AutomationElement element, int depth, ref int count)
        {
            if (++count > 10000 || depth > 70) throw new InvalidOperationException("窗口的控件树过大，已停止，请关闭多余页面。");
            UiNode node = new UiNode {
                Name = element.Cached.Name ?? "", Id = element.Cached.AutomationId ?? "",
                Role = element.Cached.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                Native = element
            };
            AutomationElementCollection children = element.CachedChildren;
            if (children != null)
                foreach (AutomationElement child in children) node.Children.Add(ConvertTree(child, depth + 1, ref count));
            return node;
        }

        private static UiNode ReadTree(AutomationElement element)
        {
            int count = 0;
            return ConvertTree(element.GetUpdatedCache(Request(TreeScope.Subtree)), 0, ref count);
        }

        private AutomationElement Root()
        {
            if (Cancelled) throw new OperationCanceledException();
            if (!Native.IsWindow(handle)) throw new InvalidOperationException("学习通窗口已关闭。");
            return AutomationElement.FromHandle(handle);
        }

        private AutomationElement VisibleStudyDocument()
        {
            if (Native.GetAncestor(Native.GetForegroundWindow(), 2) != handle) return null;
            Native.RECT rect;
            if (!Native.GetClientRect(handle, out rect)) return null;
            Native.POINT point = new Native.POINT { X = (rect.Right - rect.Left) / 2, Y = (rect.Bottom - rect.Top) / 2 };
            if (!Native.ClientToScreen(handle, ref point) || Native.GetAncestor(Native.WindowFromPoint(point), 2) != handle) return null;
            AutomationElement element = AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y));
            for (int depth = 0; element != null && depth < 60; depth++) {
                if (stop.IsSet) throw new OperationCanceledException();
                AutomationElement cached = element.GetUpdatedCache(Request(TreeScope.Element));
                if (cached.Cached.ControlType == ControlType.Document && cached.Cached.Name == "学生学习页面") return element;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return null;
        }

        public CourseSnapshot Capture(bool full)
        {
            Stopwatch measurement = Stopwatch.StartNew();
            try { return BoundedRead.Run(delegate { return CaptureCore(full); }, stop, TimeSpan.FromSeconds(45)); }
            finally {
                LastCaptureSeconds = measurement.Elapsed.TotalSeconds;
                if (Trace != null) Trace("UIA " + (full ? "full" : "light") + " " + LastCaptureSeconds.ToString("0.00") + "s " + LastCaptureBreakdown);
            }
        }

        private CourseSnapshot CaptureCore(bool full)
        {
            if (full || directory == null || currentTitle == null || card == null)
            {
                Stopwatch phase = Stopwatch.StartNew();
                if (Trace != null) Trace("UIA begin window lookup");
                AutomationElement root = Root();
                if (Trace != null) Trace("UIA locating the visible study document");
                AutomationElement visibleStudy = VisibleStudyDocument();
                double discovery = phase.Elapsed.TotalSeconds;
                phase.Restart();
                if (Trace != null) Trace("UIA begin subtree cache; find=" + discovery.ToString("0.00") + "s");
                LastTree = ReadTree(visibleStudy ?? root);
                double cache = phase.Elapsed.TotalSeconds;
                phase.Restart();
                if (Trace != null) Trace("UIA begin parse; cache=" + cache.ToString("0.00") + "s");
                CourseSnapshot result = SnapshotParser.Parse(LastTree);
                if (Trace != null && result.Error != "") {
                    StringBuilder treeReport = new StringBuilder();
                    Describe(LastTree, treeReport, 0, new int[] { 0 });
                    Trace("UIA unmatched target window tree:" + Environment.NewLine + treeReport);
                }
                if (!accessibilityWarmupAttempted && result.Error != "" && result.StudyDocument == null)
                {
                    accessibilityWarmupAttempted = true;
                    if (Trace != null) Trace("UIA learning document unavailable; requesting target accessibility once");
                    NativeAccessibility.WarmUp(handle, stop, Trace);
                    if (Trace != null) Trace("UIA accessibility initialization complete; rechecking normal course structure");
                    CourseSnapshot retry = CaptureCore(true);
                    LastCaptureBreakdown += " warmup=once";
                    return retry;
                }
                directory = result.Directory;
                currentTitle = result.CurrentTitleNode;
                card = result.Playback.CardNode;
                StampCardIdentity(result);
                if (result.Error == "" && result.Directory != null)
                {
                    latestChapters = result.Chapters.ToArray();
                    latestDirectory = result.Directory;
                }
                LastCaptureBreakdown = "find=" + discovery.ToString("0.00") + " cache=" + cache.ToString("0.00") + " parse=" + phase.Elapsed.TotalSeconds.ToString("0.00");
                return result;
            }
            // The real Electron UIA tree has no HTML IDs and many Custom wrappers.
            // Refresh only the proven title/card elements; retain the directory until a full check.
            try {
                AutomationElement titleElement = currentTitle.Native as AutomationElement;
                AutomationElement cardElement = card.Native as AutomationElement;
                if (titleElement == null || cardElement == null) return CaptureCore(true);
                UiNode study = new UiNode { Name = "学生学习页面", Role = "Document" };
                study.Children.Add(new UiNode { Name = titleElement.Current.Name ?? "", Role = currentTitle.Role, Native = titleElement });
                study.Children.Add(directory);
                study.Children.Add(ReadTree(cardElement));
                CourseSnapshot light = SnapshotParser.Parse(study);
                StampCardIdentity(light);
                LastCaptureBreakdown = "card-only";
                return light;
            } catch (ElementNotAvailableException) { return CaptureCore(true); }
        }

        private static void StampCardIdentity(CourseSnapshot snapshot)
        {
            AutomationElement nativeCard = snapshot.Playback.CardNode == null ? null : snapshot.Playback.CardNode.Native as AutomationElement;
            if (nativeCard != null)
            {
                int[] identity = nativeCard.GetRuntimeId();
                if (identity != null && identity.Length > 0)
                    snapshot.Playback.CardIdentity = "uia:" + string.Join(",", identity.Select(n => n.ToString()).ToArray());
            }
        }

        private sealed class ChapterRevealContext
        {
            internal Chapter[] Chapters;
            internal UiNode DirectoryNode;
            internal AutomationElement Directory, Target;
            internal int TargetIndex;
        }

        private sealed class ChapterScrollState
        {
            internal System.Windows.Rect Viewport;
            internal System.Windows.Point TargetPoint;
            internal AutomationElement ViewportElement;
            internal bool TargetVisible;
            internal int[] VisibleIndices;
            internal string Signature;
        }

        private static string NormalTitle(string value)
        { return System.Text.RegularExpressions.Regex.Replace((value ?? "").Trim(), @"\s+", " "); }

        private static bool MatchesChapterName(string value, Chapter chapter)
        { string name = NormalTitle(value); return name == chapter.Title || name == chapter.Key; }

        private void GuardScroll()
        {
            Guard();
            // Never release a key owned by the user or turn a wheel into zoom.
            foreach (int key in new int[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 })
                if ((Native.GetAsyncKeyState(key) & 0x8000) != 0)
                    throw new InvalidOperationException("检测到 Ctrl、Alt、Shift 或 Windows 键按下，已停止目录滚动。请松开按键后重新开始。");
        }

        private ChapterRevealContext ChapterContext(UiNode node)
        {
            GuardScroll();
            Chapter[] chapters = latestChapters;
            int[] indices = Enumerable.Range(0, chapters.Length).Where(i => object.ReferenceEquals(chapters[i].Node, node)).ToArray();
            if (indices.Length != 1 || node == null || node.Role != "Text" || latestDirectory == null ||
                !(node.Native is AutomationElement) || !(latestDirectory.Native is AutomationElement))
                throw new InvalidOperationException("目录滚动仅接受最近一次成功完整读取中的唯一章节标题。");
            return new ChapterRevealContext { Chapters = chapters, DirectoryNode = latestDirectory,
                Directory = (AutomationElement)latestDirectory.Native, Target = (AutomationElement)node.Native,
                TargetIndex = indices[0] };
        }

        private bool HasRawAncestor(AutomationElement element, AutomationElement expected)
        {
            for (int depth = 0; element != null && depth < 50; depth++)
            {
                if (stop.IsSet) throw new OperationCanceledException();
                if (Automation.Compare(element, expected)) return true;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return false;
        }

        private static CacheRequest ScrollReadRequest()
        {
            CacheRequest request = Request(TreeScope.Element);
            request.Add(AutomationElement.IsOffscreenProperty);
            request.Add(AutomationElement.IsEnabledProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            return request;
        }

        private ChapterScrollState ReadChapterScrollState(ChapterRevealContext context)
        {
            GuardScroll();
            ChapterScrollState state = BoundedRead.Run(delegate {
                if (!object.ReferenceEquals(latestChapters, context.Chapters) || !object.ReferenceEquals(latestDirectory, context.DirectoryNode))
                    throw new InvalidOperationException("目录缓存已更新，请重新选择章节后开始。");
                if (!HasRawAncestor(context.Target, context.Directory))
                    throw new InvalidOperationException("目标标题已不属于已确认的课程目录，已停止。");
                CacheRequest request = ScrollReadRequest();
                AutomationElement target = context.Target.GetUpdatedCache(request);
                if (target.Cached.ControlType != ControlType.Text || !target.Cached.IsEnabled ||
                    !MatchesChapterName(target.Cached.Name, context.Chapters[context.TargetIndex]))
                    throw new InvalidOperationException("章节标题控件已改变或不可操作，已停止。");
                AutomationElement viewportElement;
                System.Windows.Rect viewport = ReadChapterViewport(context, out viewportElement);
                ChapterScrollState result = new ChapterScrollState { Viewport = viewport, ViewportElement = viewportElement };
                result.TargetVisible = !target.Cached.IsOffscreen &&
                    ChapterScrollGeometry.TryTargetPoint(target.Cached.BoundingRectangle, viewport, out result.TargetPoint);
                if (result.TargetVisible) return result;
                List<int> visible = new List<int>();
                StringBuilder signature = new StringBuilder();
                for (int i = 0; i < context.Chapters.Length; i++)
                {
                    if (stop.IsSet) throw new OperationCanceledException();
                    AutomationElement title = context.Chapters[i].Node == null ? null : context.Chapters[i].Node.Native as AutomationElement;
                    if (title == null) throw new InvalidOperationException("目录中有章节标题失效，已停止。");
                    AutomationElement fresh = title.GetUpdatedCache(request);
                    if (fresh.Cached.IsOffscreen) continue;
                    if (fresh.Cached.ControlType != ControlType.Text || !MatchesChapterName(fresh.Cached.Name, context.Chapters[i]))
                        throw new InvalidOperationException("可见目录标题已改变，已停止。");
                    System.Windows.Point point;
                    System.Windows.Rect bounds = fresh.Cached.BoundingRectangle;
                    if (!ChapterScrollGeometry.TryTargetPoint(bounds, viewport, out point)) continue;
                    visible.Add(i);
                    signature.Append(i).Append(':').Append(Math.Round(bounds.Top)).Append(';');
                }
                result.VisibleIndices = visible.ToArray();
                result.Signature = signature.ToString();
                return result;
            }, stop, TimeSpan.FromSeconds(20));
            GuardScroll();
            return state;
        }

        // Read-only callback used again immediately before every candidate proof.
        // A cached title or viewport rectangle is never an input coordinate source.
        private System.Windows.Rect ReadChapterViewport(ChapterRevealContext context, out AutomationElement viewportElement)
        {
            if (!object.ReferenceEquals(latestChapters, context.Chapters) || !object.ReferenceEquals(latestDirectory, context.DirectoryNode) ||
                !HasRawAncestor(context.Target, context.Directory))
                throw new InvalidOperationException("章节目标或目录缓存已改变，已停止。");
            viewportElement = TreeWalker.RawViewWalker.GetParent(context.Directory);
            if (viewportElement == null) throw new InvalidOperationException("没有找到目录的可视区域，已停止。");
            System.Windows.Rect viewport;
            if (!ChapterScrollGeometry.TryViewport(context.Directory.Current.BoundingRectangle,
                viewportElement.Current.BoundingRectangle, Root().Current.BoundingRectangle, out viewport))
                throw new InvalidOperationException("目录、可视容器与学习通窗口没有明确的共同可视区域，已停止。");
            return viewport;
        }

        private void CheckDirectoryPoint(ChapterRevealContext context, System.Windows.Point point)
        {
            GuardScroll();
            Native.POINT nativePoint = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
            if (Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2) != handle)
                throw new InvalidOperationException("目录滚动位置被其他窗口遮挡，已停止。");
            bool matches = BoundedRead.Run(delegate {
                return HasRawAncestor(AutomationElement.FromPoint(point), context.Directory);
            }, stop, TimeSpan.FromSeconds(10));
            GuardScroll();
            if (!matches || Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2) != handle)
                throw new InvalidOperationException("无法确认滚动位置命中课程目录，已停止。");
        }

        private ScrollPattern DirectoryScrollPattern(ChapterRevealContext context, AutomationElement viewport)
        {
            return BoundedRead.Run(delegate {
                AutomationElement ancestor = context.Target;
                for (int depth = 0; ancestor != null && depth < 15; depth++)
                {
                    if (stop.IsSet) throw new OperationCanceledException();
                    object value;
                    if (ancestor.TryGetCurrentPattern(ScrollPattern.Pattern, out value) && ((ScrollPattern)value).Current.VerticallyScrollable)
                        return (ScrollPattern)value;
                    if (Automation.Compare(ancestor, viewport)) break;
                    ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
                }
                return null;
            }, stop, TimeSpan.FromSeconds(10));
        }

        /// <summary>Reveals a cached chapter title without clicking or starting media.</summary>
        public void RevealChapter(UiNode node)
        {
            ChapterRevealContext context = ChapterContext(node);
            ChapterScrollState state = ReadChapterScrollState(context);
            if (state.TargetVisible) return;
            object scrollItem = BoundedRead.Run(delegate {
                object value; return context.Target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out value) ? value : null;
            }, stop, TimeSpan.FromSeconds(10));
            int steps = 0;
            if (scrollItem != null)
            {
                GuardScroll();
                ((ScrollItemPattern)scrollItem).ScrollIntoView();
                steps++;
                if (!Wait(0.15)) throw new OperationCanceledException();
            }
            string previous = null;
            int unchanged = 0;
            for (; steps < 40; steps++)
            {
                state = ReadChapterScrollState(context);
                if (state.TargetVisible) return;
                int direction = ChapterScrollGeometry.Direction(context.TargetIndex, state.VisibleIndices);
                if (direction == 0)
                    throw new InvalidOperationException("无法从当前可见章节确定安全滚动方向，已停止。请手动展开并显示目标小节。");
                unchanged = state.Signature == previous ? unchanged + 1 : 0;
                if (unchanged >= 3) throw new InvalidOperationException("目录连续滚动后没有可见进展，已停止，请手动检查目录。");
                previous = state.Signature;
                System.Windows.Point point = new System.Windows.Point(Math.Floor(state.Viewport.Left + state.Viewport.Width / 2),
                    Math.Floor(state.Viewport.Top + state.Viewport.Height / 2));
                CheckDirectoryPoint(context, point);
                ScrollPattern pattern = DirectoryScrollPattern(context, state.ViewportElement);
                if (pattern != null)
                {
                    GuardScroll();
                    pattern.Scroll(ScrollAmount.NoAmount, direction < 0 ? ScrollAmount.LargeDecrement : ScrollAmount.LargeIncrement);
                    if (Trace != null) Trace("ACTION SCROLL：目录 ScrollPattern；方向 " + (direction < 0 ? "向上" : "向下"));
                }
                else
                {
                    GuardScroll();
                    Native.POINT intended = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
                    if (!Native.SetCursorPos(intended.X, intended.Y))
                        throw new InvalidOperationException("系统拒绝将鼠标移动到目录，已停止，请解除鼠标限制。");
                    Native.POINT actual;
                    if (!Native.GetCursorPos(out actual) || actual.X != intended.X || actual.Y != intended.Y)
                        throw new InvalidOperationException("鼠标未到达目录滚动位置，已停止，请解除鼠标限制。");
                    CheckDirectoryPoint(context, point);
                    GuardScroll();
                    if (!Native.GetCursorPos(out actual) || actual.X != intended.X || actual.Y != intended.Y)
                        throw new InvalidOperationException("发送滚轮前鼠标位置已改变，已停止。");
                    int edge = direction < 0 ? state.VisibleIndices[0] : state.VisibleIndices[state.VisibleIndices.Length - 1];
                    int amount = Math.Abs(context.TargetIndex - edge) <= 2 ? 120 : 360;
                    Native.INPUT[] input = new Native.INPUT[1];
                    input[0].data.mouse.flags = 0x0800;
                    input[0].data.mouse.mouseData = unchecked((uint)(direction < 0 ? amount : -amount));
                    if (Native.SendInput(1, input, Marshal.SizeOf(typeof(Native.INPUT))) != 1)
                        throw new InvalidOperationException("系统未接受目录滚轮输入，已停止。");
                    if (Trace != null) Trace("ACTION SCROLL：已核验目录滚轮；方向 " + (direction < 0 ? "向上" : "向下") + "；步长 " + amount);
                }
                if (!Wait(0.15)) throw new OperationCanceledException();
            }
            state = ReadChapterScrollState(context);
            if (!state.TargetVisible) throw new InvalidOperationException("目录滚动达到40步上限仍未显示目标小节，已停止。");
        }

        private sealed class TargetGeometry
        {
            internal System.Windows.Rect Bounds, Allowed;
            internal string Name, Role, Problem;
            internal System.Windows.Point? Clickable;
        }

        private sealed class TargetPointSearch
        {
            internal bool Found;
            internal System.Windows.Point Point;
            internal string Reason;
            internal TargetGeometry Geometry;
        }

        private static bool FiniteCoordinate(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= int.MinValue && value <= int.MaxValue; }

        internal static System.Windows.Point[] StrictTargetCandidates(System.Windows.Rect bounds, System.Windows.Rect allowed,
            System.Windows.Point? providerPoint)
        {
            System.Windows.Point center;
            if (!ChapterScrollGeometry.TryTargetPoint(bounds, allowed, out center)) return new System.Windows.Point[0];
            List<System.Windows.Point> points = new List<System.Windows.Point>();
            Action<System.Windows.Point> add = delegate(System.Windows.Point source) {
                if (!FiniteCoordinate(source.X) || !FiniteCoordinate(source.Y)) return;
                System.Windows.Point integer = new System.Windows.Point(Math.Floor(source.X), Math.Floor(source.Y));
                if (bounds.Contains(integer) && allowed.Contains(integer) && !points.Contains(integer)) points.Add(integer);
            };
            if (providerPoint.HasValue) add(providerPoint.Value);
            add(center);
            add(new System.Windows.Point(bounds.Left + Math.Min(8, bounds.Width / 4), bounds.Top + bounds.Height / 2));
            add(new System.Windows.Point(bounds.Left + Math.Min(8, bounds.Width / 4), bounds.Top + Math.Min(8, bounds.Height / 4)));
            foreach (double y in new double[] { 0.5, 0.2, 0.8 })
                foreach (double x in new double[] { 0.2, 0.5, 0.8 })
                    add(new System.Windows.Point(bounds.Left + bounds.Width * x, bounds.Top + bounds.Height * y));
            return points.ToArray();
        }

        private static string HitText(string value)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(value ?? "", @"(?i)\b(?:https?|file)://[^\s]+", "<URL omitted>")
                .Replace("\r", "\\r").Replace("\n", "\\n");
            return text.Length <= 90 ? text : text.Substring(0, 90) + "...";
        }

        private TargetGeometry ReadTargetGeometry(UiNode node, Func<System.Windows.Rect> readAllowedBounds, bool readClickable)
        {
            AutomationElement target = node.Native as AutomationElement;
            AutomationElement fresh = target.GetUpdatedCache(ScrollReadRequest());
            TargetGeometry geometry = new TargetGeometry { Name = fresh.Cached.Name ?? "",
                Role = fresh.Cached.ControlType.ProgrammaticName.Replace("ControlType.", ""),
                Bounds = fresh.Cached.BoundingRectangle, Allowed = readAllowedBounds(), Problem = "" };
            if (NormalTitle(geometry.Name) != NormalTitle(node.Name) || geometry.Role != node.Role)
                throw new InvalidOperationException("目标控件的名称或类型已改变，已停止，请重新读取当前课程。");
            System.Windows.Point center;
            if (!fresh.Cached.IsEnabled) geometry.Problem = "target-disabled";
            else if (fresh.Cached.IsOffscreen) geometry.Problem = "target-offscreen";
            else if (!ChapterScrollGeometry.TryTargetPoint(geometry.Bounds, geometry.Allowed, out center))
                geometry.Problem = "target-outside-live-viewport-or-invalid-bounds";
            if (geometry.Problem == "" && readClickable)
            {
                System.Windows.Point point;
                if (target.TryGetClickablePoint(out point)) geometry.Clickable = point;
            }
            return geometry;
        }

        private void TraceHitMiss(string phase, TargetGeometry geometry, System.Windows.Point point,
            string reason, string hitName, string hitRole, System.Windows.Rect hitBounds)
        {
            if (Trace != null) Trace("HIT MISS：phase=" + phase + "; target=" + HitText(geometry.Name) +
                "; role=" + geometry.Role + "; rect=" + geometry.Bounds.ToString(CultureInfo.InvariantCulture) +
                "; point=" + point.ToString(CultureInfo.InvariantCulture) + "; reason=" + reason +
                "; hit=" + HitText(hitName) + "; hitRole=" + hitRole +
                "; hitRect=" + hitBounds.ToString(CultureInfo.InvariantCulture));
        }

        // This proof is deliberately one-way: the hit must be the exact target or
        // its descendant. A shared row, list, name, or window is never sufficient.
        private bool StrictPointRead(UiNode node, Func<System.Windows.Rect> readAllowedBounds,
            System.Windows.Point point, string phase, out string reason)
        {
            TargetGeometry geometry = ReadTargetGeometry(node, readAllowedBounds, false);
            AutomationElement target = (AutomationElement)node.Native;
            string hitName = "<not queried>", hitRole = "<unknown>";
            System.Windows.Rect hitBounds = System.Windows.Rect.Empty;
            bool integer = FiniteCoordinate(point.X) && FiniteCoordinate(point.Y) &&
                point.X == Math.Floor(point.X) && point.Y == Math.Floor(point.Y);
            if (!integer) reason = "not-a-physical-integer-point";
            else
            {
                Native.POINT nativePoint = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
                if (Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2) != handle)
                    reason = "different-native-window";
                else
                {
                    AutomationElement hit = AutomationElement.FromPoint(point);
                    if (hit != null)
                    {
                        AutomationElement cached = hit.GetUpdatedCache(ScrollReadRequest());
                        hitName = cached.Cached.Name ?? "";
                        hitRole = cached.Cached.ControlType.ProgrammaticName.Replace("ControlType.", "");
                        hitBounds = cached.Cached.BoundingRectangle;
                    }
                    if (geometry.Problem != "") reason = geometry.Problem;
                    else if (!geometry.Bounds.Contains(point) || !geometry.Allowed.Contains(point)) reason = "point-outside-live-target";
                    else if (hit == null || !HasRawAncestor(hit, target)) reason = "hit-is-not-target-or-descendant";
                    else
                    {
                        // A provider lookup can itself outlast a layout change.
                        // Re-read the live rectangle before accepting its evidence.
                        geometry = ReadTargetGeometry(node, readAllowedBounds, false);
                        if (geometry.Problem != "") reason = geometry.Problem;
                        else if (!geometry.Bounds.Contains(point) || !geometry.Allowed.Contains(point)) reason = "target-moved-during-hit-proof";
                        else if (Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2) != handle) reason = "native-window-changed-during-hit-proof";
                        else { reason = ""; return true; }
                    }
                }
            }
            TraceHitMiss(phase, geometry, point, reason, hitName, hitRole, hitBounds);
            return false;
        }

        // Internal native-fixture seam; both methods only read UIA/window state.
        internal System.Windows.Point SelectStrictTargetPoint(UiNode node, Func<System.Windows.Rect> readAllowedBounds)
        { return SelectStrictTargetPointCore(node, readAllowedBounds, Stopwatch.StartNew()); }

        private TimeSpan HitReadTimeout(Stopwatch budget)
        {
            TimeSpan remaining = TimeSpan.FromSeconds(10) - budget.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                stop.Set();
                throw new TimeoutException("目标命中核验的累计读取超过10秒，已停止且未发送点击。");
            }
            return remaining;
        }

        private System.Windows.Point SelectStrictTargetPointCore(UiNode node, Func<System.Windows.Rect> readAllowedBounds, Stopwatch budget)
        {
            if (node == null || !(node.Native is AutomationElement) || readAllowedBounds == null)
                throw new ArgumentException("Strict target selection requires a native target and live viewport reader.");
            string lastReason = "no-candidate";
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                Guard();
                int pass = attempt;
                TargetPointSearch search = BoundedRead.Run(delegate {
                    TargetGeometry geometry = ReadTargetGeometry(node, readAllowedBounds, true);
                    TargetPointSearch result = new TargetPointSearch { Geometry = geometry, Reason = geometry.Problem };
                    if (geometry.Problem != "")
                    {
                        if (Trace != null) Trace("HIT GEOMETRY：attempt=" + pass + "/3; target=" + HitText(geometry.Name) +
                            "; role=" + geometry.Role + "; rect=" + geometry.Bounds.ToString(CultureInfo.InvariantCulture) +
                            "; allowed=" + geometry.Allowed.ToString(CultureInfo.InvariantCulture) + "; reason=" + geometry.Problem);
                        return result;
                    }
                    foreach (System.Windows.Point point in StrictTargetCandidates(geometry.Bounds, geometry.Allowed, geometry.Clickable))
                    {
                        if (stop.IsSet) throw new OperationCanceledException();
                        string reason;
                        if (StrictPointRead(node, readAllowedBounds, point, "select" + pass + "/3", out reason))
                        { result.Found = true; result.Point = point; return result; }
                        result.Reason = reason;
                    }
                    return result;
                }, stop, HitReadTimeout(budget));
                Guard();
                if (search.Found)
                {
                    if (Trace != null) Trace("HIT SELECT：target=" + HitText(node.Name) + "; point=" +
                        search.Point.ToString(CultureInfo.InvariantCulture) + "; attempt=" + attempt + "/3; strict=true");
                    return search.Point;
                }
                lastReason = search.Reason ?? "no-finite-candidate";
                if (attempt < 3 && !Wait(0.2)) throw new OperationCanceledException();
            }
            throw new InvalidOperationException("连续3次实时读取仍无法严格命中目标控件，已停止（" + lastReason + "）。请查看日志中的 HIT 坐标和实际命中控件。");
        }

        internal bool VerifyStrictTargetPoint(UiNode node, Func<System.Windows.Rect> readAllowedBounds,
            System.Windows.Point physicalPoint, string phase)
        { return VerifyStrictTargetPointCore(node, readAllowedBounds, physicalPoint, phase, Stopwatch.StartNew()); }

        private bool VerifyStrictTargetPointCore(UiNode node, Func<System.Windows.Rect> readAllowedBounds,
            System.Windows.Point physicalPoint, string phase, Stopwatch budget)
        {
            if (node == null || !(node.Native is AutomationElement) || readAllowedBounds == null)
                throw new ArgumentException("Strict target verification requires a native target and live viewport reader.");
            Guard();
            bool matches = BoundedRead.Run(delegate {
                string reason; return StrictPointRead(node, readAllowedBounds, physicalPoint, phase, out reason);
            }, stop, HitReadTimeout(budget));
            Guard();
            if (matches && Trace != null) Trace("HIT CONFIRM：phase=" + phase + "; target=" + HitText(node.Name) +
                "; point=" + physicalPoint.ToString(CultureInfo.InvariantCulture) + "; strict=true");
            return matches;
        }

        public void Press(UiNode node)
        {
            Guard();
            if (node == null || !(node.Native is AutomationElement)) throw new InvalidOperationException("目标控件已经失效。");
            AutomationElement target = (AutomationElement)node.Native;
            object pattern;
            bool chapterTitle = latestChapters.Any(c => object.ReferenceEquals(c.Node, node));
            Func<System.Windows.Rect> readAllowedBounds;
            if (chapterTitle)
            {
                RevealChapter(node);
                ChapterRevealContext context = ChapterContext(node);
                readAllowedBounds = delegate {
                    AutomationElement viewport;
                    return ReadChapterViewport(context, out viewport);
                };
            }
            else
            {
                if (node.Role == "Text") throw new InvalidOperationException("章节标题不属于最近一次成功完整读取的目录，已停止。");
                if (target.TryGetCurrentPattern(ScrollItemPattern.Pattern, out pattern))
                {
                    ((ScrollItemPattern)pattern).ScrollIntoView();
                    Guard();
                }
                readAllowedBounds = delegate { return Root().Current.BoundingRectangle; };
            }
            Stopwatch hitReadBudget = Stopwatch.StartNew();
            System.Windows.Point point = SelectStrictTargetPointCore(node, readAllowedBounds, hitReadBudget);
            Native.POINT nativePoint = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
            Guard();
            if (Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2) != handle)
                throw new InvalidOperationException("目标位置被其他窗口遮挡，已停止。");
            // Chromium exposes Invoke on directory containers even when it does not
            // dispatch the heading's click handler. Use it only for actual buttons.
            if (node.Role == "Button" && target.Current.ControlType == ControlType.Button &&
                target.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
            {
                Guard(); ((InvokePattern)pattern).Invoke();
                if (Trace != null) Trace("ACTION INPUT：按钮 Invoke；" + node.Name);
                return;
            }
            if (!Native.SetCursorPos(nativePoint.X, nativePoint.Y))
                throw new InvalidOperationException("系统拒绝将鼠标移动到目标，请解除游戏或其他程序的鼠标限制后重新开始。");
            Native.POINT actualPoint;
            if (!Native.GetCursorPos(out actualPoint) || actualPoint.X != nativePoint.X || actualPoint.Y != nativePoint.Y)
                throw new InvalidOperationException("鼠标未到达目标位置，已停止，请解除鼠标限制后重新开始。");
            GuardScroll();
            if (!VerifyStrictTargetPointCore(node, readAllowedBounds, point, "after-mouse", hitReadBudget))
                throw new InvalidOperationException("鼠标移动后的实时命中与目标不一致，已停止且未发送点击。请查看 HIT 日志。");
            GuardScroll();
            if (Native.GetAncestor(Native.WindowFromPoint(nativePoint), 2) != handle)
                throw new InvalidOperationException("点击前目标位置所属窗口已改变，已停止且未发送点击。");
            if (!Native.GetCursorPos(out actualPoint) || actualPoint.X != nativePoint.X || actualPoint.Y != nativePoint.Y)
                throw new InvalidOperationException("点击前鼠标已离开目标位置，已停止且未发送点击。");
            GuardScroll();
            Native.INPUT[] input = new Native.INPUT[2];
            input[0].data.mouse.flags = 0x0002; input[1].data.mouse.flags = 0x0004;
            if (Native.SendInput(2, input, Marshal.SizeOf(typeof(Native.INPUT))) != 2)
                throw new InvalidOperationException("系统未接受完整的点击输入，已停止，请核对页面。");
            if (Trace != null) Trace("ACTION INPUT：已核验鼠标单击；" + node.Name);
        }

        public string Diagnose()
        {
            CourseSnapshot snapshot = Capture(true);
            StringBuilder text = new StringBuilder();
            text.AppendLine("学习通视频助手诊断 · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            text.AppendLine("此文件仅在本机保存，包含当前学习页的可访问文字；不记录页面URL或登录Cookie。");
            text.AppendLine("页面：" + snapshot.CurrentTitle);
            text.AppendLine("识别结果：" + (snapshot.Error == "" ? "课程目录已识别" : snapshot.Error));
            text.AppendLine("窗口在前台：" + (Native.GetAncestor(Native.GetForegroundWindow(), 2) == handle));
            text.AppendLine("控件读取耗时：" + LastCaptureSeconds.ToString("0.00") + " 秒");
            text.AppendLine("读取分段：" + LastCaptureBreakdown);
            text.AppendLine("任务点：" + snapshot.Playback.Mark + "；数量：" + snapshot.Playback.TaskCount);
            text.AppendLine("播放器数量：" + snapshot.Playback.PlayerCount);
            text.AppendLine("进度：" + VideoPolicy.Clock(snapshot.Playback.Position) + " / " + VideoPolicy.Clock(snapshot.Playback.Duration));
            text.AppendLine("播放中：" + snapshot.Playback.Playing);
            text.AppendLine("未完成视频条目：");
            foreach (Chapter chapter in snapshot.Chapters.Where(c => c.Pending && !c.IsQuiz)) text.AppendLine("  " + chapter.Key);
            text.AppendLine("\n学习页面控件（不记录其他页面）：");
            if (snapshot.StudyDocument != null) Describe(snapshot.StudyDocument, text, 0, new int[] { 0 });
            else
            {
                text.AppendLine("学习页面未匹配；下列为目标学习通窗口的有限控件结构，不含其他应用：");
                if (LastTree != null) Describe(LastTree, text, 0, new int[] { 0 });
            }
            return text.ToString();
        }

        private static void Describe(UiNode node, StringBuilder text, int depth, int[] count)
        {
            if (++count[0] > 800) return;
            string name = node.Name ?? "";
            // Captions can be long. No link target values or native handles are exported.
            if (name.Length > 160) name = name.Substring(0, 160) + "…";
            text.AppendLine(new string(' ', Math.Min(depth, 40)) + node.Role + " " + name + " [" + node.Id + "]");
            foreach (UiNode child in node.Children) Describe(child, text, depth + 1, count);
        }
    }

    public sealed class HelperForm : Form
    {
        private readonly Button start = new Button(), stopButton = new Button(), diagnose = new Button(), verify = new Button();
        private readonly Label status = new Label();
        private readonly TextBox log = new TextBox();
        private ManualResetEventSlim stopSignal;
        private Task running;
        private readonly string outputDirectory;
        private readonly string logPath;
        private readonly Func<ManualResetEventSlim, IStudyDriver> createSessionDriver;

        public HelperForm() : this(CreateWindowsDriver, null) { }

        internal HelperForm(Func<ManualResetEventSlim, IStudyDriver> driverFactory, string logsDirectory)
        {
            if (driverFactory == null) throw new ArgumentNullException("driverFactory");
            createSessionDriver = driverFactory;
            outputDirectory = logsDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            logPath = Path.Combine(outputDirectory, "session-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            Text = "学习通视频助手 · 0.3 验证版";
            ClientSize = new Size(690, 455); MinimumSize = new Size(660, 460);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(247, 249, 252);
            Font = new Font("Microsoft YaHei UI", 10);
            AutoScaleMode = AutoScaleMode.Dpi;

            Label title = new Label { Text = "学习通视频助手", Font = new Font(Font.FontFamily, 19, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 48, 71), Location = new Point(22, 18), Size = new Size(600, 40) };
            Label instructions = new Label { Text = "先打开学习通并进入目标课程的章节学习页，再点“开始”。\n学习通需保持前台；工具读取进度，在预计结束前20秒检查完成标记。",
                Location = new Point(24, 69), Size = new Size(640, 56) };
            start.Name = "start"; start.Text = "开始"; start.SetBounds(24, 137, 140, 39); start.BackColor = Color.FromArgb(44, 105, 211); start.ForeColor = Color.White;
            stopButton.Name = "stop"; stopButton.Text = "停止"; stopButton.SetBounds(176, 137, 110, 39); stopButton.Enabled = false;
            diagnose.Name = "diagnose"; diagnose.Text = "诊断（只读）"; diagnose.SetBounds(298, 137, 140, 39);
            verify.Name = "verify"; verify.Text = "验证一次切换"; verify.SetBounds(451, 137, 210, 39);
            status.Text = "就绪 · 只处理视频，自动跳过章节测验"; status.SetBounds(24, 190, 640, 36);
            status.AutoEllipsis = true;
            status.ForeColor = Color.FromArgb(47, 76, 108);
            log.Name = "log"; log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical;
            log.SetBounds(24, 229, 642, 190); log.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            log.BackColor = Color.White; log.Font = new Font("Microsoft YaHei UI", 9);
            Label footer = new Label { Text = "F11 开始 · F12 停止（均按 Ctrl+Alt）· 日志：logs",
                Location = new Point(24, 427), Size = new Size(642, 22), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            footer.ForeColor = Color.DimGray; footer.Font = new Font(Font.FontFamily, 8);
            Controls.AddRange(new Control[] { title, instructions, start, stopButton, diagnose, verify, status, log, footer });
            start.Click += delegate { StartSession(); };
            stopButton.Click += delegate { StopSession(); };
            diagnose.Click += delegate { Diagnose(); };
            verify.Click += delegate { StartSession(true); };
            FormClosing += delegate { if (stopSignal != null) stopSignal.Set(); };
        }

        private static IStudyDriver CreateWindowsDriver(ManualResetEventSlim stop)
        {
            WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stop);
            driver.Activate();
            return driver;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!Native.RegisterHotKey(Handle, 120, 0x0001 | 0x0002 | 0x4000, 0x7B))
                status.Text = "停止快捷键注册失败；可用窗口中的“停止”按钮。";
            if (!Native.RegisterHotKey(Handle, 121, 0x0001 | 0x0002 | 0x4000, 0x7A))
                status.Text = "开始快捷键注册失败；可用窗口中的“开始”按钮。";
        }
        protected override void OnHandleDestroyed(EventArgs e)
        { Native.UnregisterHotKey(Handle, 120); Native.UnregisterHotKey(Handle, 121); base.OnHandleDestroyed(e); }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 120) StopSession();
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 121) StartSession();
            base.WndProc(ref m);
        }

        private void Write(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { try { BeginInvoke(new Action<string>(Write), message); } catch (InvalidOperationException) { } return; }
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            log.AppendText(line + Environment.NewLine); status.Text = message;
            try { Directory.CreateDirectory(outputDirectory); File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { status.Text = message + "（日志写入失败）"; }
            catch (UnauthorizedAccessException) { status.Text = message + "（日志目录不可写）"; }
        }

        private void Busy(bool busy)
        { start.Enabled = !busy; diagnose.Enabled = !busy; verify.Enabled = !busy; stopButton.Enabled = busy; }

        private void StartSession()
        { StartSession(false); }

        private void StartSession(bool verifyTransition)
        {
            if (running != null && !running.IsCompleted) return;
            try
            {
                stopSignal = new ManualResetEventSlim(false);
                IStudyDriver driver = createSessionDriver(stopSignal);
                WindowsStudyDriver nativeDriver = driver as WindowsStudyDriver;
                if (nativeDriver != null) nativeDriver.Trace = verifyTransition ? (Action<string>)Write : delegate(string line) {
                    if (line.StartsWith("HIT ", StringComparison.Ordinal) || line.StartsWith("ACTION ", StringComparison.Ordinal)) Write(line);
                };
                Busy(true); Write(verifyTransition ? "开始验证一次切换。确认当前未完成视频后，只验证到下一段播放进度前进。" :
                    "已开始。学习通请保持前台；需要操作电脑时先停止助手。");
                running = Task.Factory.StartNew(delegate {
                    try {
                        driver.Wait(0.5);
                        if (verifyTransition)
                        {
                            bool passed = TransitionVerification.Run(driver, Write);
                            Write(passed ? "PASS：一次真实播放切换验证通过，验证已结束。" : "验证未通过，尚未取得完整播放切换证据。");
                        }
                        else { new VideoSession(driver, Write).Run(); Write("本轮已结束。"); }
                    }
                    catch (OperationCanceledException) { Write("已停止；学习通中的视频可能仍在继续播放。"); }
                    catch (Exception ex) { Write("已停止：" + ex.Message); }
                    finally { if (!IsDisposed) try { BeginInvoke(new Action(delegate { Busy(false); })); } catch (InvalidOperationException) { } }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception ex) { Busy(false); Write(ex.Message); }
        }

        private void StopSession()
        { if (stopSignal != null) stopSignal.Set(); }

        private void Diagnose()
        {
            Busy(true);
            stopSignal = new ManualResetEventSlim(false);
            running = Task.Factory.StartNew(delegate {
                try
                {
                    WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stopSignal);
                    string report = driver.Diagnose();
                    Directory.CreateDirectory(outputDirectory);
                    string path = Path.Combine(outputDirectory, "diagnostic-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                    File.WriteAllText(path, report, Encoding.UTF8);
                    Write(report.Split(new string[] { "\n学习页面控件" }, StringSplitOptions.None)[0].Trim());
                    Write("诊断已保存：" + path);
                }
                catch (OperationCanceledException) { Write("诊断已取消。"); }
                catch (Exception ex) { Write("诊断未完成：" + ex.Message); }
                finally { if (!IsDisposed) try { BeginInvoke(new Action(delegate { Busy(false); })); } catch (InvalidOperationException) { } }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            Forms.Application.EnableVisualStyles();
            Forms.Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--verify-transition")
            {
                string path = args[1];
                Action<string> report = delegate(string line) {
                    File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine, Encoding.UTF8);
                };
                try
                {
                    bool success = Task.Factory.StartNew(delegate {
                        WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), new ManualResetEventSlim(false));
                        driver.Trace = report;
                        driver.Activate();
                        return TransitionVerification.Run(driver, report);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Result;
                    report(success ? "PASS 实机播放与下一段切换验证通过。" : "FAIL 实机验证未通过。");
                    return success ? 0 : 3;
                }
                catch (Exception error)
                {
                    report("FAIL " + error.GetBaseException().Message);
                    return 3;
                }
            }
            if (args.Length == 2 && args[0] == "--diagnose")
            {
                try
                {
                    string report = Task.Factory.StartNew(delegate {
                        return new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), new ManualResetEventSlim(false)).Diagnose();
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Result;
                    File.WriteAllText(args[1], report, Encoding.UTF8);
                    return 0;
                }
                catch (Exception error)
                {
                    File.WriteAllText(args[1], "诊断失败：" + error.GetBaseException().Message, Encoding.UTF8);
                    return 2;
                }
            }
            if (args.Length == 2 && args[0] == "--preview")
            {
                using (HelperForm form = new HelperForm())
                {
                    form.ShowInTaskbar = false;
                    form.StartPosition = FormStartPosition.Manual;
                    form.Location = new Point(-30000, -30000);
                    form.Show(); Forms.Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(args[1]); }
                }
                return 0;
            }
            Forms.Application.Run(new HelperForm());
            return 0;
        }
    }
}
