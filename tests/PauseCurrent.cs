using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace StudyVideoHelper
{
    internal static class PauseCurrent
    {
        private const string ExpectedTitle = "总体国家安全观的科学内涵";
        private const string NextTitle = "总体国家安全观的重点领域和基本特征";
        private static string outputPath;

        private static void Report(string message)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
            Console.WriteLine(line);
            File.AppendAllText(outputPath, line + Environment.NewLine, new UTF8Encoding(false));
        }

        private static void CheckProof(string proofPath)
        {
            string[] lines = File.ReadAllLines(Path.GetFullPath(proofPath), Encoding.UTF8);
            int observedAt = Array.FindIndex(lines, line => line.Contains("OBSERVE：" + NextTitle + "；") &&
                line.Contains("；任务 Incomplete；播放中 True；任务点 1；播放器 1"));
            int passedAt = Array.FindLastIndex(lines, line => line.Contains("PASS：一次真实播放切换验证通过"));
            if (observedAt < 0 || passedAt <= observedAt)
                throw new InvalidOperationException("真实会话日志尚无指定1.3视频的播放观察及其后PASS验证，未操作。");
        }

        private static void CheckTarget(CourseSnapshot snapshot, string expectedTitle, string expectedNumber)
        {
            if (snapshot == null || !string.IsNullOrEmpty(snapshot.Error))
                throw new InvalidOperationException("当前学习页面识别失败，未操作。" + (snapshot == null ? "" : snapshot.Error));
            if (snapshot.CurrentTitle != expectedTitle ||
                snapshot.Chapters.Count(c => c.Number == expectedNumber && c.Title == expectedTitle && !c.IsQuiz) != 1 ||
                !snapshot.Chapters.Any(c => c.Number == "1.2" && c.Title == ExpectedTitle && !c.IsQuiz) ||
                !snapshot.Chapters.Any(c => c.Number == "1.1" && c.Title == "总体国家安全观的创立") ||
                !snapshot.Chapters.Any(c => c.Number == "1.3" && c.Title == NextTitle && !c.IsQuiz))
                throw new InvalidOperationException("当前不是指定国家安全教育" + expectedNumber + "小节，未操作。");
            if (snapshot.Playback.TaskCount != 1 || snapshot.Playback.PlayerCount != 1 ||
                snapshot.Playback.Mark != TaskMark.Incomplete || snapshot.Playback.CardNode == null)
                throw new InvalidOperationException("指定小节的唯一未完成视频任务无法确认，未操作。");
        }

        [MTAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            bool nextAfterProof = args.Length == 3 && args[0] == "--pause-next-after-proof";
            bool current = (args.Length == 1 || args.Length == 2) && args[0] == "--pause-current";
            if (!current && !nextAfterProof)
            {
                Console.WriteLine("Usage: PauseCurrent.exe --pause-current [local-report-path]");
                Console.WriteLine("   or: PauseCurrent.exe --pause-next-after-proof local-report-path real-session-log-path");
                Console.WriteLine("Only pauses the currently playing 国家安全教育 1.2 总体国家安全观的科学内涵 once. No activation or navigation.");
                Console.WriteLine("The explicit after-proof mode pauses 1.3 总体国家安全观的重点领域和基本特征 only after its real-session OBSERVE and PASS records exist.");
                return args.Length == 1 && args[0] == "--help" ? 0 : 2;
            }
            outputPath = args.Length >= 2 ? Path.GetFullPath(args[1]) :
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "pause-current-result.txt"));
            string expectedTitle = nextAfterProof ? NextTitle : ExpectedTitle;
            string expectedNumber = nextAfterProof ? "1.3" : "1.2";
            try
            {
                if (nextAfterProof)
                {
                    CheckProof(args[2]);
                    Report("PROOF_CONFIRMED: 指定1.3视频已有真实OBSERVE与随后PASS记录。");
                }
                Native.SetProcessDPIAware();
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stop);
                    driver.Trace = Report;
                    driver.Guard();
                    CourseSnapshot before = driver.Capture(true);
                    driver.Guard();
                    CheckTarget(before, expectedTitle, expectedNumber);
                    Report("BEFORE " + expectedNumber + " " + before.CurrentTitle + " Mark=" + before.Playback.Mark +
                        " Playing=" + before.Playback.Playing + " Position=" + VideoPolicy.Clock(before.Playback.Position));
                    if (!before.Playback.Playing)
                    {
                        Report("ALREADY_PAUSED: 指定视频当前未播放，未点击任何控件。");
                        return 0;
                    }
                    UiNode[] pauseButtons = before.Playback.CardNode.Walk().Where(n => n.Role == "Button" &&
                        ((n.Name ?? "").Trim() == "暂停视频" || (n.Name ?? "").Trim() == "暂停")).ToArray();
                    if (pauseButtons.Length != 1)
                        throw new InvalidOperationException("当前卡片内没有唯一暂停按钮，未操作。");
                    driver.Guard();
                    driver.Press(pauseButtons[0]);
                    Report("ACTION: 已通过生产驱动点击暂停一次；不再发送任何点击。");
                    if (!driver.Wait(0.6)) throw new OperationCanceledException();
                    CourseSnapshot after = driver.Capture(false);
                    driver.Guard();
                    CheckTarget(after, expectedTitle, expectedNumber);
                    if (after.Fingerprint != before.Fingerprint || after.CurrentTitle != before.CurrentTitle)
                        throw new InvalidOperationException("暂停后课程或小节发生变化，无法确认结果。");
                    if (after.Playback.Playing)
                        throw new InvalidOperationException("暂停后仍显示播放中；未重试点击。");
                    Report("PASS: 同一" + expectedNumber + "小节 Playing=False，任务点仍Incomplete；本次仅暂停一次。");
                    return 0;
                }
            }
            catch (Exception error)
            {
                Report("FAIL: " + error);
                return 1;
            }
        }
    }
}
