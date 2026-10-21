using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace StudyVideoHelper
{
    internal static class PauseVerifiedCurrent
    {
        private static string outputPath;

        private static void Report(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
            Console.WriteLine(line);
            File.AppendAllText(outputPath, line + Environment.NewLine, new UTF8Encoding(false));
        }

        private static void CheckProof(string proofPath, string expectedTitle)
        {
            string[] lines = File.ReadAllLines(proofPath, Encoding.UTF8);
            int lastObserve = Array.FindLastIndex(lines, line => line.Contains("OBSERVE："));
            if (lastObserve < 0 || !lines[lastObserve].Contains("OBSERVE：" + expectedTitle + "；") ||
                !lines[lastObserve].Contains("；任务 Incomplete；播放中 True；任务点 1；播放器 1"))
                throw new InvalidOperationException("真实日志最后一次OBSERVE不是指定标题的唯一未完成且正在播放的视频，未操作。");
            int passedAt = Array.FindLastIndex(lines, line =>
                line.Contains("PASS：一次真实播放切换验证通过") ||
                line.Contains("PASS 实机播放与下一段切换验证通过") ||
                (line.Contains("PASS：已确认起始视频任务完成，程序切换到“" + expectedTitle + "”") &&
                 line.Contains("下一视频连续播放进度已前进至少2秒")));
            if (passedAt <= lastObserve)
                throw new InvalidOperationException("真实日志尚无该次播放观察之后的完整切换PASS证据，未操作。");
        }

        private static void CheckTarget(CourseSnapshot snapshot, string expectedTitle)
        {
            if (snapshot == null || !string.IsNullOrEmpty(snapshot.Error))
                throw new InvalidOperationException("当前学习页面识别失败，未操作。");
            if (snapshot.CurrentTitle != expectedTitle ||
                snapshot.Chapters.Count(c => c.Title == expectedTitle && !c.IsQuiz) != 1)
                throw new InvalidOperationException("当前小节不是指定的唯一视频标题，未操作。");
            if (snapshot.Playback.Mark != TaskMark.Incomplete || snapshot.Playback.TaskCount != 1 ||
                snapshot.Playback.PlayerCount != 1 || snapshot.Playback.CardNode == null)
                throw new InvalidOperationException("当前页没有唯一可确认的未完成视频任务，未操作。");
        }

        [MTAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length != 4 || args[0] != "--pause-after-proof" || string.IsNullOrWhiteSpace(args[3]))
            {
                Console.WriteLine("Usage: PauseVerifiedCurrent.exe --pause-after-proof reportFile liveProofFile expectedTitle");
                Console.WriteLine("Requires a final matching playing OBSERVE followed by real transition PASS; pauses the exact current video at most once.");
                return args.Length == 1 && args[0] == "--help" ? 0 : 2;
            }
            outputPath = Path.GetFullPath(args[1]);
            string proofPath = Path.GetFullPath(args[2]);
            string expectedTitle = args[3].Trim();
            if (string.Equals(outputPath, proofPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Report and proof files must be different; no operation performed.");
                return 2;
            }
            try
            {
                CheckProof(proofPath, expectedTitle);
                Report("PROOF_CONFIRMED: 最后一次指定标题的播放观察后已有完整切换PASS；目标=" + expectedTitle);
                Native.SetProcessDPIAware();
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stop);
                    driver.Trace = Report;
                    driver.Guard();
                    CourseSnapshot before = driver.Capture(true);
                    driver.Guard();
                    CheckTarget(before, expectedTitle);
                    Report("BEFORE " + before.CurrentTitle + "; Mark=" + before.Playback.Mark +
                        "; Playing=" + before.Playback.Playing + "; Position=" + VideoPolicy.Clock(before.Playback.Position));
                    if (!before.Playback.Playing)
                    {
                        Report("ALREADY_PAUSED: 指定视频已暂停，未点击任何控件。");
                        return 0;
                    }
                    UiNode[] pause = before.Playback.CardNode.Walk().Where(n => n.Role == "Button" &&
                        ((n.Name ?? "").Trim() == "暂停视频" || (n.Name ?? "").Trim() == "暂停" ||
                         (n.Name ?? "").Trim() == "Pause" || (n.Name ?? "").Trim() == "Pause Video")).ToArray();
                    if (pause.Length != 1)
                        throw new InvalidOperationException("当前唯一视频卡片中没有唯一暂停按钮，未操作。");
                    driver.Guard();
                    driver.Press(pause[0]);
                    Report("ACTION: 已通过生产驱动正常暂停一次；不会再次点击。");
                    if (!driver.Wait(0.6)) throw new OperationCanceledException();
                    driver.Guard();
                    CourseSnapshot after = driver.Capture(true);
                    driver.Guard();
                    CheckTarget(after, expectedTitle);
                    if (after.Fingerprint != before.Fingerprint || after.CurrentTitle != before.CurrentTitle ||
                        after.Playback.Playing)
                        throw new InvalidOperationException("暂停后未确认同一课程、同一标题且Playing=False；未重试。");
                    if (!string.IsNullOrEmpty(before.Playback.CardIdentity) && !string.IsNullOrEmpty(after.Playback.CardIdentity) &&
                        before.Playback.CardIdentity != after.Playback.CardIdentity)
                        throw new InvalidOperationException("暂停后播放器卡片身份改变，无法确认结果；未重试。");
                    Report("PASS: 同一课程与指定标题已暂停，任务仍Incomplete；只暂停一次。");
                    return 0;
                }
            }
            catch (Exception error)
            {
                Report("FAIL: " + error.GetType().Name + ": " + error.Message);
                return 1;
            }
        }
    }
}
