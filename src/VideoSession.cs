using System;
using System.Collections.Generic;
using System.Linq;

namespace StudyVideoHelper
{
    public interface IStudyDriver
    {
        bool Cancelled { get; }
        double Seconds { get; }
        void Guard();
        bool Wait(double seconds);
        CourseSnapshot Capture(bool full);
        void Press(UiNode node);
    }

    public static class TransitionReadiness
    {
        public static bool IsFresh(Playback old, Playback next)
        { return next != null && next.Mark == TaskMark.Incomplete && IsNewPlayerState(old, next); }

        public static bool IsNewPlayerState(Playback old, Playback next)
        {
            if (old == null || next == null || old.Mark != TaskMark.Complete ||
                (next.Mark != TaskMark.Incomplete && next.Mark != TaskMark.Complete) ||
                old.TaskCount != 1 || old.PlayerCount != 1 || next.TaskCount != 1 || next.PlayerCount != 1 || next.Playing ||
                next.PlayButton == null || next.PlayButton.Role != "Button") return false;
            if (!IsFinite(old.Position) || !IsFinite(old.Duration) || !IsFinite(next.Position) || !IsFinite(next.Duration)) return false;
            string playName = (next.PlayButton.Name ?? "").Trim();
            bool normalPlay = playName == "播放" || playName == "播放视频" ||
                playName.Equals("Play", StringComparison.OrdinalIgnoreCase) ||
                playName.Equals("Play Video", StringComparison.OrdinalIgnoreCase);
            if (!normalPlay) return false;

            // A reused accessibility element can display a different video. Require
            // unloaded metadata or a real clock reset instead of a changed RuntimeId.
            if (!next.HasValidTime) return next.Position < 0 && next.Duration <= 0 &&
                (next.Mark == TaskMark.Incomplete || old.HasValidTime);
            if (!old.HasValidTime || next.Remaining <= 0.5) return false;
            return next.Position <= old.Position - 2 || Math.Abs(next.Duration - old.Duration) > 1;
        }

        private static bool IsFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public static class PlaybackClock
    {
        // previousAt is the start of the last accepted capture; now is the end of
        // this capture. UIA may return text sampled anywhere within either read.
        public static bool IsPlausible(double previousPosition, double previousDuration, double previousAt,
            Playback current, double now)
        {
            if (current == null || !current.HasValidTime || !IsFinite(previousPosition) || !IsFinite(previousDuration) ||
                !IsFinite(previousAt) || !IsFinite(current.Position) || !IsFinite(current.Duration) || !IsFinite(now) ||
                previousDuration <= 0 || previousPosition < 0 || previousPosition > previousDuration + 1 ||
                previousAt < 0 || now < previousAt) return false;
            double elapsed = now - previousAt;
            double advance = current.Position - previousPosition;
            return IsFinite(elapsed) && Math.Abs(current.Duration - previousDuration) <= 1 &&
                advance >= -2 && advance <= elapsed + 3;
        }

        private static bool IsFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed class VideoSession
    {
        private readonly IStudyDriver driver;
        private readonly Action<string> report;
        private readonly HashSet<string> finished = new HashSet<string>();
        private string fingerprint;
        private double lastFullReadAt = double.NegativeInfinity;
        private double lastReadStartedAt;
        private int completionCount;
        public int Completed { get { return completionCount; } }

        public VideoSession(IStudyDriver driver, Action<string> report)
        { this.driver = driver; this.report = report; }

        private CourseSnapshot Read(bool full)
        {
            driver.Guard();
            lastReadStartedAt = driver.Seconds;
            CourseSnapshot snapshot = driver.Capture(full);
            if (driver.Cancelled) throw new OperationCanceledException();
            if (full) lastFullReadAt = driver.Seconds;
            if (!string.IsNullOrEmpty(snapshot.Error)) throw new InvalidOperationException(snapshot.Error);
            if (snapshot.Chapters.Where(c => !c.IsQuiz).GroupBy(c => c.Title).Any(g => g.Count() > 1))
                throw new InvalidOperationException("存在同名视频小节，无法可靠区分完成状态，已停止。");
            if (fingerprint == null) fingerprint = snapshot.Fingerprint;
            if (snapshot.Fingerprint != fingerprint) throw new InvalidOperationException("课程目录发生变化，已停止，请核对当前课程。");
            return snapshot;
        }

        private void Wait(double seconds)
        {
            if (!driver.Wait(seconds)) throw new OperationCanceledException();
            driver.Guard();
        }

        public void Run()
        {
            CourseSnapshot snapshot = Read(true);
            Chapter current = snapshot.Chapters.FirstOrDefault(c => c.Title == snapshot.CurrentTitle && !c.IsQuiz);
            if (current == null) throw new InvalidOperationException("请先进入明确的视频页面再开始。");
            while (!driver.Cancelled)
            {
                if (current == null || snapshot.Playback.Mark == TaskMark.Complete || finished.Contains(current.Key))
                {
                    // A completed mark must belong to a recognized, single-player card before we skip it.
                    if (current != null && snapshot.Playback.Mark == TaskMark.Complete &&
                        (snapshot.Playback.TaskCount != 1 || snapshot.Playback.PlayerCount != 1))
                        throw new InvalidOperationException("当前页的任务或播放器数量不明确，请手动核对。");
                    if (current != null && snapshot.Playback.Mark == TaskMark.Complete) finished.Add(current.Key);
                    int currentIndex = snapshot.Chapters.FindIndex(c => c.Key == current.Key);
                    if (currentIndex < 0) throw new InvalidOperationException("当前小节已不在课程目录中，已停止。");
                    Chapter next = snapshot.Chapters.Skip(currentIndex + 1)
                        .FirstOrDefault(c => c.Pending && !c.IsQuiz && !finished.Contains(c.Key));
                    if (next == null)
                    {
                        report("从当前小节向后没有找到其他带未完成标记的视频。本次确认新完成 " + Completed + " 段。请核对课程进度；章节测验未操作。");
                        return;
                    }
                    current = next;
                    report("打开 " + current.Key);
                    Playback oldPlayback = snapshot.Playback;
                    driver.Press(current.Node);
                    double deadline = driver.Seconds + 25;
                    int stable = 0;
                    do
                    {
                        Wait(1);
                        snapshot = Read(true);
                        bool ready = snapshot.CurrentTitle == current.Title &&
                            TransitionReadiness.IsNewPlayerState(oldPlayback, snapshot.Playback);
                        stable = ready ? stable + 1 : 0;
                    } while (stable < 2 && driver.Seconds < deadline);
                    if (stable < 2) throw new InvalidOperationException("新小节未能在25秒内稳定加载，请手动核对。若该条目不是视频，请先手动完成或跳过它。");
                    if (snapshot.Playback.Mark == TaskMark.Complete) { finished.Add(current.Key); continue; }
                }

                if (snapshot.Playback.TaskCount != 1 || snapshot.Playback.PlayerCount != 1 || snapshot.Playback.Mark == TaskMark.Unknown)
                    throw new InvalidOperationException("当前页面没有唯一、可识别的视频任务点。");
                string problem;
                if (!snapshot.Playback.Playing)
                {
                    if (snapshot.Playback.PlayButton == null) throw new InvalidOperationException("没有找到可识别的播放按钮。");
                    report("开始播放 " + current.Key);
                    driver.Press(snapshot.Playback.PlayButton);
                    double playDeadline = driver.Seconds + 12;
                    do
                    {
                        Wait(1);
                        snapshot = Read(false);
                        EnsureChapter(snapshot, current);
                    } while ((!snapshot.Playback.Playing || !snapshot.Playback.HasValidTime) && driver.Seconds < playDeadline);
                    if (!snapshot.Playback.Playing || !snapshot.Playback.HasValidTime)
                        throw new InvalidOperationException("点击播放后未确认开始或未能读取时长，可能被弹窗或网络问题阻挡。");
                }

                EnsureChapter(snapshot, current);
                problem = VideoPolicy.PlaybackProblem(snapshot.Playback);
                if (problem != "") throw new InvalidOperationException(problem);
                Playback playback = snapshot.Playback;
                string identity = playback.CardIdentity;
                double lastPosition = playback.Position;
                double lastProgressAt = driver.Seconds;
                double acceptedPosition = playback.Position;
                double acceptedDuration = playback.Duration;
                double acceptedAt = lastReadStartedAt;
                double nextReadAt = driver.Seconds + Math.Min(10, VideoPolicy.UntilCheck(playback));
                double checkAt = driver.Seconds + VideoPolicy.UntilCheck(playback);
                report(current.Key + "  " + VideoPolicy.Clock(playback.Position) + " / " + VideoPolicy.Clock(playback.Duration) +
                    "；预计 " + (int)Math.Ceiling(VideoPolicy.UntilCheck(playback)) + " 秒后检查完成标记。");

                bool completed = false;
                double endGraceAt = double.PositiveInfinity;
                while (!driver.Cancelled)
                {
                    Wait(Math.Max(0.1, Math.Min(1, Math.Min(nextReadAt, checkAt) - driver.Seconds)));
                    if (driver.Seconds + 0.05 < Math.Min(nextReadAt, checkAt)) continue;
                    // The card contains the task mark as well as progress. Refresh the
                    // directory periodically instead of on every near-end card poll.
                    snapshot = Read(driver.Seconds - lastFullReadAt >= 30);
                    double sampledAt = driver.Seconds;
                    EnsurePlaybackContext(snapshot, current, identity);
                    int rechecks = 0;
                    while (!PlaybackClock.IsPlausible(acceptedPosition, acceptedDuration, acceptedAt, snapshot.Playback, sampledAt))
                    {
                        if (rechecks >= 2)
                            throw new InvalidOperationException("视频时间连续跳变或倒退，复核后仍无法确认真实进度，已停止。");
                        rechecks++;
                        report("视频时间读数异常，正在完整复核（" + rechecks + "/2）。");
                        snapshot = Read(true);
                        sampledAt = driver.Seconds;
                        EnsurePlaybackContext(snapshot, current, identity);
                    }
                    playback = snapshot.Playback;
                    acceptedPosition = playback.Position;
                    acceptedDuration = playback.Duration;
                    acceptedAt = lastReadStartedAt;

                    // Check the server's task mark, not an inferred 90% or a local countdown.
                    if (playback.Remaining <= VideoPolicy.EarlyCheckSeconds + 0.5 && playback.Mark == TaskMark.Complete)
                    { completed = true; break; }

                    if (playback.Position > lastPosition + 0.2)
                    { lastPosition = playback.Position; lastProgressAt = driver.Seconds; }
                    if (playback.Remaining <= 0.5)
                    {
                        if (double.IsPositiveInfinity(endGraceAt)) endGraceAt = driver.Seconds + 60;
                        if (driver.Seconds >= endGraceAt)
                            throw new InvalidOperationException("视频已到结尾，但60秒后仍未收到“任务点已完成”。请检查网络或手动刷新。");
                    }
                    else
                    {
                        if (!playback.Playing) throw new InvalidOperationException("播放中途暂停，已停止。请先处理弹窗或验证，再重新开始。");
                        if (driver.Seconds - lastProgressAt >= 40)
                            throw new InvalidOperationException("视频进度40秒未前进，可能正在缓冲或被弹窗阻挡，已停止。");
                    }
                    double until = VideoPolicy.UntilCheck(playback);
                    checkAt = driver.Seconds + until;
                    nextReadAt = driver.Seconds + (until > 0 ? Math.Min(10, until) : 2);
                    if (until <= 0) checkAt = nextReadAt; // Poll only the last 20 seconds at two-second intervals.
                }
                if (!completed) throw new OperationCanceledException();
                finished.Add(current.Key);
                completionCount++;
                report("已确认完成 " + current.Key + "；本次新完成 " + Completed + " 段。");
                snapshot = Read(true);
                EnsureChapter(snapshot, current);
            }
            throw new OperationCanceledException();
        }

        private static void EnsureChapter(CourseSnapshot snapshot, Chapter current)
        {
            if (snapshot.CurrentTitle != current.Title)
                throw new InvalidOperationException("当前小节已改变或无法识别，已停止，请核对页面。");
        }

        private static void EnsurePlaybackContext(CourseSnapshot snapshot, Chapter current, string identity)
        {
            EnsureChapter(snapshot, current);
            Playback playback = snapshot.Playback;
            if (playback.TaskCount != 1 || playback.PlayerCount != 1 || playback.Mark == TaskMark.Unknown)
                throw new InvalidOperationException(VideoPolicy.PlaybackProblem(playback));
            if (identity != "" && playback.CardIdentity != "" && playback.CardIdentity != identity)
                throw new InvalidOperationException("播放器内容发生变化，已停止。");
        }
    }
}
