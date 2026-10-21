using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    // Only observes native accessibility properties; never acts on a pattern.
    internal static class ScrollProbe
    {
        private static string Name(string text)
        {
            string value = (text ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
            return value.Length <= 80 ? value : value.Substring(0, 80) + "...";
        }

        private static string Number(double value)
        { return value.ToString("0.###", CultureInfo.InvariantCulture); }

        private static void Describe(string label, AutomationElement element)
        {
            if (element == null) { Console.WriteLine(label + "=<null>"); return; }
            try
            {
                AutomationElement.AutomationElementInformation info = element.Current;
                Console.WriteLine(label + ".Name=" + Name(info.Name) + "; Role=" + info.ControlType.ProgrammaticName +
                    "; IsOffscreen=" + info.IsOffscreen + "; Bounds=" +
                    info.BoundingRectangle.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine(label + ".ScrollPattern.IsAvailable=" +
                    element.GetCurrentPropertyValue(AutomationElement.IsScrollPatternAvailableProperty));
                object pattern;
                bool hasScroll = element.TryGetCurrentPattern(ScrollPattern.Pattern, out pattern);
                Console.WriteLine(label + ".TryGetScrollPattern=" + hasScroll);
                if (hasScroll)
                {
                    ScrollPattern.ScrollPatternInformation scroll = ((ScrollPattern)pattern).Current;
                    Console.WriteLine(label + ".Scroll.VerticalScrollable=" + scroll.VerticallyScrollable +
                        "; VerticalScrollPercent=" + Number(scroll.VerticalScrollPercent) +
                        "; VerticalViewSize=" + Number(scroll.VerticalViewSize) +
                        "; HorizontalScrollable=" + scroll.HorizontallyScrollable +
                        "; HorizontalScrollPercent=" + Number(scroll.HorizontalScrollPercent) +
                        "; HorizontalViewSize=" + Number(scroll.HorizontalViewSize));
                }
                Console.WriteLine(label + ".SupportsScrollItem=" + element.TryGetCurrentPattern(ScrollItemPattern.Pattern, out pattern));
            }
            catch (Exception error) { Console.WriteLine(label + "=<unavailable: " + Name(error.Message) + ">"); }
        }

        private static void Parents(string label, TreeWalker walker, AutomationElement target, WindowsStudyDriver driver)
        {
            AutomationElement ancestor = target;
            for (int depth = 1; depth <= 15; depth++)
            {
                driver.Guard();
                try { ancestor = walker.GetParent(ancestor); }
                catch (Exception error)
                {
                    Console.WriteLine(label + ".Parent" + depth + "=<unavailable: " + Name(error.Message) + ">");
                    break;
                }
                if (ancestor == null)
                {
                    Console.WriteLine(label + ".Parent" + depth + "=<end>");
                    break;
                }
                Describe(label + ".Parent" + depth, ancestor);
            }
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
                    Console.WriteLine("CurrentTitle=" + Name(snapshot.CurrentTitle) + "; Error=" + Name(snapshot.Error));
                    Console.WriteLine("Playback=" + VideoPolicy.Clock(snapshot.Playback.Position) + "/" +
                        VideoPolicy.Clock(snapshot.Playback.Duration) + "; Mark=" + snapshot.Playback.Mark +
                        "; Playing=" + snapshot.Playback.Playing);
                    Chapter[] matches = snapshot.Chapters.Where(c => c.Number == "10.3" && !c.IsQuiz).ToArray();
                    Console.WriteLine("TargetMatches=" + matches.Length);
                    Chapter last = snapshot.Chapters.LastOrDefault(c => !c.IsQuiz);
                    Console.WriteLine("LastNonQuiz.Key=" + (last == null ? "<null>" : Name(last.Key)));
                    if (matches.Length != 1)
                        throw new InvalidOperationException("Expected exactly one non-quiz directory chapter numbered 10.3.");
                    Chapter chapter = matches[0];
                    Console.WriteLine("Chapter.Key=" + Name(chapter.Key) + "; Pending=" + chapter.Pending + "; IsQuiz=" + chapter.IsQuiz);
                    Console.WriteLine("Node.Name=" + Name(chapter.Node == null ? null : chapter.Node.Name) +
                        "; Role=" + Name(chapter.Node == null ? null : chapter.Node.Role));
                    AutomationElement native = chapter.Node == null ? null : chapter.Node.Native as AutomationElement;
                    if (native == null) throw new InvalidOperationException("Chapter 10.3 has no native AutomationElement.");
                    Describe("Node.Native", native);
                    Parents("Raw", TreeWalker.RawViewWalker, native, driver);
                    Parents("Control", TreeWalker.ControlViewWalker, native, driver);
                    driver.Guard();
                    Console.WriteLine("READ ONLY COMPLETE: no scroll, invocation, focus, or input was sent.");
                    return string.IsNullOrEmpty(snapshot.Error) ? 0 : 2;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
        }
    }
}
