using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    // Read-only native chapter diagnostics. No Press, pattern actions, focus,
    // scrolling, mouse, or keyboard input is performed by this entry point.
    internal static class ChapterProbe
    {
        private static string Text(string value)
        { return (value ?? "").Replace("\r", "\\r").Replace("\n", "\\n"); }

        private static string RuntimeId(AutomationElement element)
        {
            if (element == null) return "<null>";
            try
            {
                int[] values = element.GetRuntimeId();
                return values == null ? "<null>" : string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToArray());
            }
            catch (Exception error) { return "<unavailable: " + Text(error.Message) + ">"; }
        }

        private static void Describe(string label, AutomationElement element)
        {
            if (element == null) { Console.WriteLine(label + "=<null>"); return; }
            try
            {
                AutomationElement.AutomationElementInformation info = element.Current;
                Console.WriteLine(label + ".Name=" + Text(info.Name) + "; Role=" + info.ControlType.ProgrammaticName +
                    "; Id=" + Text(info.AutomationId) + "; RuntimeId=" + RuntimeId(element) +
                    "; IsOffscreen=" + info.IsOffscreen + "; IsEnabled=" + info.IsEnabled +
                    "; BoundingRectangle=" + info.BoundingRectangle.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception error) { Console.WriteLine(label + "=<unavailable: " + Text(error.Message) + ">"); }
        }

        private static void DescribeHit(System.Windows.Point point, WindowsStudyDriver driver)
        {
            driver.Guard();
            Console.WriteLine("FromPoint.Point=" + point.ToString(CultureInfo.InvariantCulture));
            AutomationElement hit = AutomationElement.FromPoint(point);
            Describe("FromPoint.Hit", hit);
            for (int depth = 1; hit != null && depth <= 8; depth++)
            {
                try { hit = TreeWalker.RawViewWalker.GetParent(hit); }
                catch (Exception error)
                {
                    Console.WriteLine("FromPoint.Parent" + depth + "=<unavailable: " + Text(error.Message) + ">");
                    break;
                }
                if (hit == null) break;
                Describe("FromPoint.Parent" + depth, hit);
            }
            driver.Guard();
        }

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                Native.SetProcessDPIAware();
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    IntPtr handle = WindowsStudyDriver.FindWindow();
                    Console.WriteLine("WindowHandle=" + handle.ToInt64());
                    WindowsStudyDriver driver = new WindowsStudyDriver(handle, stop);
                    driver.Trace = Console.WriteLine;
                    driver.Guard();
                    CourseSnapshot snapshot = driver.Capture(true);
                    driver.Guard();
                    Console.WriteLine("CurrentTitle=" + Text(snapshot.CurrentTitle));
                    Console.WriteLine("Error=" + Text(snapshot.Error));
                    Playback playback = snapshot.Playback;
                    Console.WriteLine("Playback=" + VideoPolicy.Clock(playback.Position) + "/" + VideoPolicy.Clock(playback.Duration) +
                        "; Position=" + playback.Position.ToString(CultureInfo.InvariantCulture) +
                        "; Duration=" + playback.Duration.ToString(CultureInfo.InvariantCulture) +
                        "; Mark=" + playback.Mark + "; Playing=" + playback.Playing +
                        "; Tasks=" + playback.TaskCount + "; Players=" + playback.PlayerCount);
                    Console.WriteLine("PlayButton.Name=" + (playback.PlayButton == null ? "<null>" : Text(playback.PlayButton.Name)));
                    Chapter[] matches = snapshot.Chapters.Where(c => c.Number == "1.2" && c.Title == "总体国家安全观的科学内涵").ToArray();
                    Console.WriteLine("TargetMatches=" + matches.Length);
                    if (matches.Length != 1)
                    {
                        foreach (Chapter chapter in snapshot.Chapters.Where(c => c.Number == "1.2" || c.Title.Contains("科学内涵")))
                            Console.WriteLine("Candidate.Key=" + Text(chapter.Key));
                        throw new InvalidOperationException("Expected exactly one chapter 1.2 总体国家安全观的科学内涵.");
                    }
                    Chapter target = matches[0];
                    Console.WriteLine("Chapter.Key=" + Text(target.Key) + "; Pending=" + target.Pending + "; IsQuiz=" + target.IsQuiz);
                    Console.WriteLine("Node.Name=" + Text(target.Node == null ? null : target.Node.Name) +
                        "; Role=" + Text(target.Node == null ? null : target.Node.Role));
                    AutomationElement native = target.Node == null ? null : target.Node.Native as AutomationElement;
                    if (native == null) throw new InvalidOperationException("Chapter has no native AutomationElement.");
                    Describe("Node.Native", native);
                    UiNode[] details = target.Node.Walk().Where(n => n != target.Node && n.Role == "Text" && n.Children.Count == 0)
                        .Concat(target.Node.Children).Distinct().Take(6).ToArray();
                    Console.WriteLine("Node.ChildOrLeafTextCount=" + details.Length + " (maximum 6)");
                    for (int i = 0; i < details.Length; i++)
                    {
                        UiNode detail = details[i];
                        string label = "Node.Detail" + (i + 1);
                        Console.WriteLine(label + ".Name=" + Text(detail.Name) + "; Role=" + Text(detail.Role) +
                            "; DirectChild=" + target.Node.Children.Contains(detail));
                        Describe(label + ".Native", detail.Native as AutomationElement);
                    }
                    object pattern;
                    Console.WriteLine("Node.SupportsInvoke=" + native.TryGetCurrentPattern(InvokePattern.Pattern, out pattern));
                    Console.WriteLine("Node.SupportsScrollItem=" + native.TryGetCurrentPattern(ScrollItemPattern.Pattern, out pattern));
                    System.Windows.Point point;
                    bool hasClickablePoint = native.TryGetClickablePoint(out point);
                    Console.WriteLine("Node.TryGetClickablePoint=" + hasClickablePoint + "; Point=" +
                        point.ToString(CultureInfo.InvariantCulture));
                    if (hasClickablePoint) DescribeHit(point, driver);
                    else
                    {
                        System.Windows.Rect bounds = native.Current.BoundingRectangle;
                        if (!bounds.IsEmpty && bounds.Width >= 2 && bounds.Height >= 2)
                        {
                            Console.WriteLine("FromPoint.Source=BoundingRectangle center (same fallback as production)");
                            DescribeHit(new System.Windows.Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), driver);
                        }
                        else Console.WriteLine("FromPoint=<no usable clickable point or rectangle>");
                    }
                    driver.Guard();
                    Console.WriteLine("READ ONLY COMPLETE: no chapter action or input was sent.");
                    return string.IsNullOrEmpty(snapshot.Error) ? 0 : 2;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
        }
    }
}
