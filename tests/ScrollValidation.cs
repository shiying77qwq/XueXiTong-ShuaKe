using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    // Integration check: reveal headings without selecting or starting a video.
    internal static class ScrollValidation
    {
        private static Chapter Unique(CourseSnapshot state, string number)
        {
            Chapter[] matches = state.Chapters.Where(c => c.Number == number && !c.IsQuiz).ToArray();
            if (matches.Length != 1 || matches[0].Node == null)
                throw new InvalidOperationException("Cannot uniquely identify chapter " + number);
            return matches[0];
        }

        private static void CheckVisible(Chapter chapter)
        {
            AutomationElement native = chapter.Node.Native as AutomationElement;
            if (native == null || native.Current.IsOffscreen || native.Current.BoundingRectangle.IsEmpty)
                throw new InvalidOperationException("Heading is still offscreen: " + chapter.Key);
            Console.WriteLine("VISIBLE: " + chapter.Key + "; bounds=" + native.Current.BoundingRectangle);
        }

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                Native.SetProcessDPIAware();
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stop);
                    driver.Trace = Console.WriteLine;
                    driver.Guard();
                    CourseSnapshot before = driver.Capture(true);
                    if (before.Error != "" || before.Playback.Mark != TaskMark.Incomplete || before.Playback.Playing ||
                        !before.CurrentTitle.Contains("总体国家安全观的科学内涵"))
                        throw new InvalidOperationException("Expected the same unfinished, paused 1.2 video before scrolling.");
                    Chapter distant = Unique(before, "10.3");
                    AutomationElement native = distant.Node.Native as AutomationElement;
                    if (native == null || !native.Current.IsOffscreen)
                        throw new InvalidOperationException("Chapter 10.3 must start offscreen to prove scrolling.");
                    Console.WriteLine("BEFORE: 10.3 offscreen=True; current=" + before.CurrentTitle +
                        "; progress=" + VideoPolicy.Clock(before.Playback.Position) + "; playing=False");
                    driver.RevealChapter(distant.Node);
                    CheckVisible(distant);
                    driver.RevealChapter(Unique(before, "1.3").Node);
                    CheckVisible(Unique(before, "1.3"));
                    CourseSnapshot after = driver.Capture(true);
                    if (after.Error != "" || after.CurrentTitle != before.CurrentTitle || after.Playback.Playing ||
                        after.Playback.Mark != TaskMark.Incomplete ||
                        Math.Abs(after.Playback.Position - before.Playback.Position) > 2)
                        throw new InvalidOperationException("Scrolling changed the paused video or its progress.");
                    Console.WriteLine("PASS: revealed offscreen 10.3, restored 1.3, current video unchanged and paused.");
                    return 0;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error); return 1; }
        }
    }
}
