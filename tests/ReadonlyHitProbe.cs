using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    // Diagnostics only: no scrolling, pattern action, cursor movement or input.
    // Focus restoration is permitted only with the explicit --activate switch.
    internal static class ReadonlyHitProbe
    {
        private sealed class NodeInfo
        {
            internal AutomationElement Element;
            internal string Name, Role, RuntimeId;
            internal Rect Bounds;
            internal bool Enabled, Offscreen;
        }

        private static string Safe(string value)
        {
            string text = Regex.Replace(value ?? "", @"(?i)\b(?:https?|file)://[^\s]+", "<URL omitted>")
                .Replace("\r", "\\r").Replace("\n", "\\n");
            return text.Length <= 140 ? text : text.Substring(0, 140) + "...";
        }

        private static string Id(AutomationElement element)
        {
            try
            {
                int[] values = element.GetRuntimeId();
                return values == null || values.Length == 0 ? "<none>" :
                    string.Join(",", values.Take(16).Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray());
            }
            catch (Exception error) { return "<unavailable:" + error.GetType().Name + ">"; }
        }

        private static NodeInfo ReadNode(AutomationElement element)
        {
            AutomationElement.AutomationElementInformation current = element.Current;
            return new NodeInfo { Element = element, Name = current.Name,
                Role = current.ControlType.ProgrammaticName, RuntimeId = Id(element),
                Bounds = current.BoundingRectangle, Enabled = current.IsEnabled, Offscreen = current.IsOffscreen };
        }

        private static void PrintNode(string label, NodeInfo node)
        {
            Console.WriteLine(label + " Name=" + Safe(node.Name) + "; Role=" + Safe(node.Role) +
                "; Bounds=" + node.Bounds.ToString(CultureInfo.InvariantCulture) +
                "; RuntimeId=" + node.RuntimeId + "; Enabled=" + node.Enabled + "; Offscreen=" + node.Offscreen);
        }

        private static List<NodeInfo> RawChain(AutomationElement first, ManualResetEventSlim stop)
        {
            List<NodeInfo> result = new List<NodeInfo>();
            AutomationElement element = first;
            for (int depth = 0; element != null && depth < 30; depth++)
            {
                if (stop.IsSet) throw new OperationCanceledException();
                result.Add(ReadNode(element));
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return result;
        }

        private static bool SameId(NodeInfo first, NodeInfo second)
        {
            return first.RuntimeId != "<none>" && !first.RuntimeId.StartsWith("<") &&
                first.RuntimeId == second.RuntimeId;
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static bool Usable(Rect bounds)
        {
            return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 &&
                Finite(bounds.X) && Finite(bounds.Y) && Finite(bounds.Right) && Finite(bounds.Bottom);
        }

        private static bool Usable(Point point)
        {
            return Finite(point.X) && Finite(point.Y) && point.X >= int.MinValue && point.X <= int.MaxValue &&
                point.Y >= int.MinValue && point.Y <= int.MaxValue;
        }

        private static void DescribePoint(string label, Point point, Rect appBounds, IntPtr app,
            List<NodeInfo> targetChain, ManualResetEventSlim stop)
        {
            if (!Usable(point) || !appBounds.Contains(point))
            {
                Console.WriteLine(label + " SKIP: point outside application bounds or invalid.");
                return;
            }
            Native.POINT nativePoint = new Native.POINT { X = (int)point.X, Y = (int)point.Y };
            IntPtr hwnd = Native.WindowFromPoint(nativePoint);
            IntPtr root = Native.GetAncestor(hwnd, 2);
            Console.WriteLine(label + " Point=" + point.ToString(CultureInfo.InvariantCulture) +
                "; NativePoint=" + nativePoint.X + "," + nativePoint.Y + "; HWND=" + hwnd.ToInt64() +
                "; RootHWND=" + root.ToInt64() + "; SameAppRoot=" + (root == app));
            try
            {
                BoundedRead.Run(delegate {
                    AutomationElement hit = AutomationElement.FromPoint(point);
                    if (hit == null) { Console.WriteLine(label + ".Hit=<null>"); return false; }
                    List<NodeInfo> hitChain = RawChain(hit, stop);
                    for (int depth = 0; depth < hitChain.Count; depth++)
                        PrintNode(label + ".Raw[" + depth + "]", hitChain[depth]);
                    int hitTargetDepth = hitChain.FindIndex(n => SameId(n, targetChain[0]));
                    int targetHitDepth = targetChain.FindIndex(n => SameId(n, hitChain[0]));
                    int commonHit = -1, commonTarget = -1;
                    for (int i = 0; i < hitChain.Count; i++)
                        for (int j = 0; j < targetChain.Count; j++)
                            if (SameId(hitChain[i], targetChain[j]) &&
                                (commonHit < 0 || i + j < commonHit + commonTarget))
                            { commonHit = i; commonTarget = j; }
                    Console.WriteLine(label + ".Relation HitHasTargetAncestorDepth=" + hitTargetDepth +
                        "; TargetHasHitAncestorDepth=" + targetHitDepth + "; CommonHitDepth=" + commonHit +
                        "; CommonTargetDepth=" + commonTarget + "; DirectAutomationCompare=" +
                        Automation.Compare(hit, targetChain[0].Element));
                    return true;
                }, stop, TimeSpan.FromSeconds(15));
            }
            catch (Exception error)
            {
                Console.WriteLine(label + ".ERROR=" + error.GetType().Name + ":" + Safe(error.Message));
                if (stop.IsSet) throw;
            }
        }

        private static void ProbeTarget(string label, UiNode node, Rect appBounds, IntPtr app, ManualResetEventSlim stop)
        {
            Console.WriteLine("TARGET " + label + "; CachedName=" + Safe(node == null ? null : node.Name) +
                "; CachedRole=" + Safe(node == null ? null : node.Role));
            AutomationElement native = node == null ? null : node.Native as AutomationElement;
            if (native == null) { Console.WriteLine(label + " SKIP: no native target."); return; }
            List<NodeInfo> targetChain;
            Point clickable = new Point();
            bool hasClickable = false;
            try
            {
                targetChain = BoundedRead.Run(delegate {
                    List<NodeInfo> chain = RawChain(native, stop);
                    hasClickable = native.TryGetClickablePoint(out clickable);
                    return chain;
                }, stop, TimeSpan.FromSeconds(20));
            }
            catch (Exception error)
            {
                Console.WriteLine(label + ".ERROR=" + error.GetType().Name + ":" + Safe(error.Message));
                if (stop.IsSet) throw;
                return;
            }
            for (int depth = 0; depth < targetChain.Count; depth++)
                PrintNode(label + ".TargetRaw[" + depth + "]", targetChain[depth]);
            Console.WriteLine(label + ".TryGetClickablePoint=" + hasClickable + "; Point=" +
                clickable.ToString(CultureInfo.InvariantCulture));
            NodeInfo target = targetChain[0];
            if (target.Offscreen || !Usable(target.Bounds))
            {
                Console.WriteLine(label + " SKIP screen hit tests: target offscreen or no finite positive live rectangle.");
                return;
            }
            List<Point> sampled = new List<Point>();
            if (hasClickable && Usable(clickable))
            {
                DescribePoint(label + ".Clickable", clickable, appBounds, app, targetChain, stop);
                sampled.Add(clickable);
            }
            int index = 0;
            foreach (double y in new double[] { 0.2, 0.5, 0.8 })
                foreach (double x in new double[] { 0.2, 0.5, 0.8 })
                {
                    Point point = new Point(Math.Floor(target.Bounds.Left + target.Bounds.Width * x),
                        Math.Floor(target.Bounds.Top + target.Bounds.Height * y));
                    index++;
                    if (sampled.Contains(point)) continue;
                    DescribePoint(label + ".Grid" + index, point, appBounds, app, targetChain, stop);
                    sampled.Add(point);
                }
        }

        [MTAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            bool activate = args.Length == 1 && args[0] == "--activate";
            if (args.Length != 0 && !activate)
            {
                Console.WriteLine("Usage: ReadonlyHitProbe.exe [--activate] (read-only diagnostics to console)");
                return args.Length == 1 && args[0] == "--help" ? 0 : 2;
            }
            try
            {
                Native.SetProcessDPIAware();
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    IntPtr app = WindowsStudyDriver.FindWindow();
                    IntPtr foreground = Native.GetAncestor(Native.GetForegroundWindow(), 2);
                    Console.WriteLine("READ ONLY HIT PROBE " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                        "; AppHWND=" + app.ToInt64() + "; ForegroundRoot=" + foreground.ToInt64() +
                        "; AppForeground=" + (foreground == app));
                    WindowsStudyDriver driver = new WindowsStudyDriver(app, stop);
                    driver.Trace = line => Console.WriteLine("TRACE " + Safe(line));
                    if (activate)
                    {
                        Console.WriteLine("EXPLICIT_ACTIVATE: restoring focus only; no cursor or input operation.");
                        driver.Activate();
                    }
                    if (Native.GetAncestor(Native.GetForegroundWindow(), 2) != app)
                    {
                        Console.WriteLine("NEED_FOREGROUND: capture skipped to avoid reading a background whole-window tree.");
                        return 3;
                    }
                    driver.Guard();
                    CourseSnapshot snapshot = driver.Capture(true);
                    driver.Guard();
                    Console.WriteLine("SNAPSHOT CurrentTitle=" + Safe(snapshot.CurrentTitle) + "; Error=" + Safe(snapshot.Error) +
                        "; Mark=" + snapshot.Playback.Mark + "; Playing=" + snapshot.Playback.Playing +
                        "; Tasks=" + snapshot.Playback.TaskCount + "; Players=" + snapshot.Playback.PlayerCount);
                    if (!string.IsNullOrEmpty(snapshot.Error)) return 2;
                    Rect appBounds = BoundedRead.Run(delegate {
                        return AutomationElement.FromHandle(app).Current.BoundingRectangle;
                    }, stop, TimeSpan.FromSeconds(10));
                    Console.WriteLine("AppBounds=" + appBounds.ToString(CultureInfo.InvariantCulture));
                    if (!Usable(appBounds)) throw new InvalidOperationException("Application bounds unavailable.");
                    Chapter[] current = snapshot.Chapters.Where(c => c.Title == snapshot.CurrentTitle ||
                        c.Key == snapshot.CurrentTitle).ToArray();
                    Console.WriteLine("CurrentChapterMatches=" + current.Length);
                    if (current.Length == 1)
                    {
                        ProbeTarget("CurrentChapter " + Safe(current[0].Key), current[0].Node, appBounds, app, stop);
                        int currentIndex = snapshot.Chapters.IndexOf(current[0]);
                        Chapter next = snapshot.Chapters.Skip(currentIndex + 1).FirstOrDefault(c => c.Pending && !c.IsQuiz);
                        if (next != null) ProbeTarget("NextChapter " + Safe(next.Key), next.Node, appBounds, app, stop);
                        else Console.WriteLine("NextChapter=<none after current chapter>");
                    }
                    ProbeTarget("CurrentPlayButton", snapshot.Playback.PlayButton, appBounds, app, stop);
                    Console.WriteLine("READ ONLY COMPLETE: no scrolling, cursor movement, click or input was sent.");
                    return 0;
                }
            }
            catch (Exception error)
            {
                Console.WriteLine("FAILED " + error.GetType().Name + ":" + Safe(error.Message));
                return 1;
            }
        }
    }
}
