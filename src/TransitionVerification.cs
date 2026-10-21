using System;
using System.Linq;

namespace StudyVideoHelper
{
    /// <summary>
    /// Runs the production session until one newly completed video has been followed by
    /// a program-initiated chapter navigation and independently observed playback progress.
    /// It never treats a timer, a title change, or a click alone as a successful transition.
    /// </summary>
    public static class TransitionVerification
    {
        public static bool Run(IStudyDriver inner, Action<string> report)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            Action<string> output = report ?? delegate { };
            ProofDriver proof = new ProofDriver(inner, output);
            VideoSession session = new VideoSession(proof, output);
            proof.CompletedCount = delegate { return session.Completed; };
            try
            {
                session.Run();
            }
            catch (OperationCanceledException)
            {
                if (proof.Succeeded && !inner.Cancelled)
                {
                    output("PASS：已确认起始视频任务完成，程序切换到“" + proof.NextTitle +
                        "”，且下一视频连续播放进度已前进至少2秒；验证已停止，不继续处理后续视频。");
                    return true;
                }
                output("FAIL：验证被取消，尚未完成“完成任务→程序切换→下一视频进度前进”的完整验证。");
                return false;
            }
            catch (Exception error)
            {
                output("FAIL：" + error.Message);
                throw;
            }
            output("FAIL：会话已结束，但没有取得下一视频正常播放的完整证据。可能没有下一段未完成视频。");
            return false;
        }

        private sealed class ProofDriver : IStudyDriver
        {
            private readonly IStudyDriver inner;
            private readonly Action<string> report;
            private readonly AcceptedClock initialClock = new AcceptedClock();
            private readonly AcceptedClock nextClock = new AcceptedClock();
            private CourseSnapshot last;
            private string initialTitle;
            private string initialFingerprint;
            private Playback oldCompletePlayback;
            private bool initialCompletionObserved;
            private bool chapterPressRecorded;
            private int freshObservations;
            private bool confirmedFresh;
            private double nextBaselinePosition = -1;
            private double nextBaselineTime;
            private string nextBaselineCard;
            private int nextObservationCount;
            public Func<int> CompletedCount;
            public bool Succeeded { get; private set; }
            public string NextTitle { get; private set; }
            public bool Cancelled { get { return Succeeded || inner.Cancelled; } }
            public double Seconds { get { return inner.Seconds; } }

            public ProofDriver(IStudyDriver inner, Action<string> report)
            { this.inner = inner; this.report = report; }

            public void Guard()
            {
                if (Succeeded) throw new OperationCanceledException();
                inner.Guard();
            }

            public bool Wait(double seconds)
            {
                if (Succeeded) return false;
                return inner.Wait(seconds);
            }

            public CourseSnapshot Capture(bool full)
            {
                double sampledFrom = inner.Seconds;
                CourseSnapshot snapshot = inner.Capture(full);
                // UIA capture can outlast a stop request. Never promote its returned
                // progress to success after the underlying driver was cancelled.
                if (inner.Cancelled) throw new OperationCanceledException();
                double sampledAt = inner.Seconds;
                last = snapshot;
                Playback playback = snapshot.Playback;
                report("OBSERVE：" + snapshot.CurrentTitle + "；进度 " +
                    VideoPolicy.Clock(playback.Position) + " / " + VideoPolicy.Clock(playback.Duration) +
                    "；任务 " + playback.Mark + "；播放中 " + playback.Playing +
                    "；任务点 " + playback.TaskCount + "；播放器 " + playback.PlayerCount);

                if (initialTitle == null)
                {
                    if (snapshot.Error != "") throw new InvalidOperationException(snapshot.Error);
                    Chapter current = snapshot.Chapters.FirstOrDefault(c => c.Title == snapshot.CurrentTitle && !c.IsQuiz);
                    if (current == null || playback.TaskCount != 1 || playback.PlayerCount != 1)
                        throw new InvalidOperationException("实测必须从一个可识别、只有一个视频任务点的小节开始。");
                    if (playback.Mark != TaskMark.Incomplete)
                        throw new InvalidOperationException("实测起点必须是明确未完成的视频；已完成或未知状态不能作为本次播放切换验证的起点。");
                    initialTitle = snapshot.CurrentTitle;
                    initialFingerprint = snapshot.Fingerprint;
                    report("验证起点：" + current.Key + "。将等待真实完成标记，并只验证一次程序切换。");
                }

                // Validate facts here as well as in VideoSession: a successful proof cancels
                // Read immediately, so it must not bypass its course/error safety checks.
                if (snapshot.Error != "") return snapshot;
                if (snapshot.Fingerprint != initialFingerprint)
                    throw new InvalidOperationException("验证期间课程目录发生变化，不能判定切换通过。");
                if (snapshot.Chapters.Where(c => !c.IsQuiz).GroupBy(c => c.Title).Any(g => g.Count() > 1))
                    throw new InvalidOperationException("验证期间出现同名视频小节，不能区分其完成状态。");
                if (nextBaselinePosition >= 0 && snapshot.CurrentTitle == NextTitle &&
                    (playback.CardIdentity ?? "") != nextBaselineCard)
                    throw new InvalidOperationException("验证期间下一视频的播放器内容发生变化，不能判定连续播放通过。");

                // Session cannot validate a read after a successful proof cancels it.
                // Keep our own rolling accepted clocks, including the initial task's
                // completion evidence, and let Session recheck a suspicious snapshot.
                if (snapshot.CurrentTitle == initialTitle && !initialClock.Accept(playback, sampledFrom, sampledAt))
                {
                    report("CLOCK：起始视频时间读数尚不可信，等待会话完整复核。");
                    return snapshot;
                }

                if (snapshot.CurrentTitle == initialTitle && playback.Mark == TaskMark.Complete &&
                    playback.TaskCount == 1 && playback.PlayerCount == 1 && playback.HasValidTime &&
                    playback.Remaining <= VideoPolicy.EarlyCheckSeconds + 0.5)
                {
                    initialCompletionObserved = true;
                    if (!chapterPressRecorded && CompletedCount() >= 1)
                        oldCompletePlayback = new Playback {
                            Mark = playback.Mark, TaskCount = playback.TaskCount, PlayerCount = playback.PlayerCount,
                            Position = playback.Position, Duration = playback.Duration, Playing = playback.Playing,
                            PlayButton = playback.PlayButton, CardIdentity = playback.CardIdentity
                        };
                }

                // The app reuses UIA runtime IDs when iframe content changes. Confirm
                // fresh, paused target state during navigation, before Session presses
                // Play; that evidence need not remain present after playback starts.
                if (chapterPressRecorded && !confirmedFresh)
                {
                    bool fresh = full && oldCompletePlayback != null && CompletedCount() >= 1 &&
                        snapshot.CurrentTitle == NextTitle && TransitionReadiness.IsFresh(oldCompletePlayback, playback);
                    freshObservations = fresh ? freshObservations + 1 : 0;
                    if (freshObservations >= 2)
                    {
                        confirmedFresh = true;
                        report("FRESH：连续两次完整读取已确认目标未完成任务与播放器重新加载；等待实际播放进度。");
                    }
                }

                if (chapterPressRecorded && confirmedFresh && snapshot.CurrentTitle == NextTitle &&
                    !nextClock.Accept(playback, sampledFrom, sampledAt))
                {
                    report("CLOCK：下一视频时间读数尚不可信，等待会话完整复核。");
                    return snapshot;
                }

                if (chapterPressRecorded && confirmedFresh && initialCompletionObserved && CompletedCount() >= 1 &&
                    snapshot.CurrentTitle == NextTitle && playback.TaskCount == 1 && playback.PlayerCount == 1 &&
                    playback.HasValidTime && playback.Playing)
                {
                    if (nextBaselinePosition < 0)
                    {
                        nextBaselinePosition = playback.Position;
                        nextBaselineTime = sampledAt;
                        nextBaselineCard = playback.CardIdentity ?? "";
                        nextObservationCount = 1;
                    }
                    else
                    {
                        nextObservationCount++;
                        if (nextObservationCount >= 2 && playback.Position - nextBaselinePosition >= 2 && sampledAt - nextBaselineTime >= 2)
                        {
                            inner.Guard();
                            if (inner.Cancelled) throw new OperationCanceledException();
                            Succeeded = true;
                        }
                    }
                }
                return snapshot;
            }

            private sealed class AcceptedClock
            {
                private bool hasValue;
                private double position, duration, sampledFrom;

                public bool Accept(Playback current, double captureStartedAt, double now)
                {
                    bool plausible = hasValue
                        ? PlaybackClock.IsPlausible(position, duration, sampledFrom, current, now)
                        : PlaybackClock.IsPlausible(current.Position, current.Duration, captureStartedAt, current, now);
                    if (!plausible) return false;
                    position = current.Position;
                    duration = current.Duration;
                    // UIA may return a clock captured early in a slow traversal.
                    // The next value can legally advance since this window began.
                    sampledFrom = captureStartedAt;
                    hasValue = true;
                    return true;
                }
            }

            public void Press(UiNode node)
            {
                Guard();
                Chapter target = last == null ? null : last.Chapters.FirstOrDefault(c => object.ReferenceEquals(c.Node, node));
                if (target != null)
                {
                    if (!initialCompletionObserved || CompletedCount() < 1 || oldCompletePlayback == null)
                        throw new InvalidOperationException("验证拒绝在起始视频尚未确认完成时切换目录。");
                    if (chapterPressRecorded)
                        throw new InvalidOperationException("下一视频尚未取得有效播放进度证据，验证拒绝继续打开第三个视频。");
                    if (target.IsQuiz || target.Title == initialTitle)
                        throw new InvalidOperationException("验证目标必须是另一段视频，不能是测验或起始小节。");
                    inner.Press(node);
                    NextTitle = target.Title;
                    chapterPressRecorded = true;
                    report("ACTION：程序已点击下一视频目录项“" + target.Key + "”；等待播放进度证据。");
                    return;
                }
                inner.Press(node);
            }
        }
    }
}
