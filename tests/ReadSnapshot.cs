using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    internal static class ReadSnapshot
    {
        private static string RuntimeId(UiNode node)
        {
            AutomationElement native = node == null ? null : node.Native as AutomationElement;
            if (native == null) return "<no native element>";
            try
            {
                int[] values = native.GetRuntimeId();
                return values == null ? "<null>" : string.Join(",", values.Select(v => v.ToString()).ToArray());
            }
            catch (Exception error) { return "<unavailable: " + error.Message + ">"; }
        }

        private static int Main()
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stop);
                    driver.Trace = Console.WriteLine;
                    driver.Guard();
                    CourseSnapshot snapshot = driver.Capture(true);
                    driver.Guard();
                    Console.WriteLine("CurrentTitle=" + snapshot.CurrentTitle);
                    Console.WriteLine("Error=" + snapshot.Error);
                    foreach (Chapter chapter in snapshot.Chapters.Where(c => c.Title.Contains("创立")))
                        Console.WriteLine("Chapter.Title=" + chapter.Title + "; Key=" + chapter.Key + "; Pending=" + chapter.Pending);
                    Playback playback = snapshot.Playback;
                    Console.WriteLine("CardIdentity=" + playback.CardIdentity);
                    Console.WriteLine("Playback=" + VideoPolicy.Clock(playback.Position) + "/" + VideoPolicy.Clock(playback.Duration) +
                        "; Mark=" + playback.Mark + "; Playing=" + playback.Playing + "; Tasks=" + playback.TaskCount + "; Players=" + playback.PlayerCount);
                    Console.WriteLine("CurrentTitleNode.RuntimeId=" + RuntimeId(snapshot.CurrentTitleNode));
                    Console.WriteLine("CardNode.RuntimeId=" + RuntimeId(playback.CardNode));
                    if (playback.CardNode != null)
                    {
                        foreach (UiNode document in playback.CardNode.Walk().Where(n => n.Role == "Document"))
                            Console.WriteLine("CardDocument.Name=" + document.Name + "; Id=" + document.Id + "; RuntimeId=" + RuntimeId(document));
                        foreach (UiNode player in playback.CardNode.Walk().Where(n => n.Id == "video" || n.Name == "视频播放器"))
                            Console.WriteLine("Player.Name=" + player.Name + "; Id=" + player.Id + "; RuntimeId=" + RuntimeId(player));
                    }
                    return string.IsNullOrEmpty(snapshot.Error) ? 0 : 2;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error.Message); return 1; }
        }
    }
}
