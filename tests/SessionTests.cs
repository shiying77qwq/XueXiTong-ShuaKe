using System;
using System.Collections.Generic;
using System.Linq;
using StudyVideoHelper;

internal static class SessionTests
{
    private sealed class Video
    {
        public string Number;
        public string Title;
        public double Duration;
        public double Position;
        public bool Playing;
        public bool InitiallyComplete;
        public bool NeverComplete;
        public bool TimeUnknownUntilPlay;
        public double CompleteAt = -1;
        public double CompleteAtGlobal = -1;
        public double StallAt = double.PositiveInfinity;
        public Video(string number, string title, double duration)
        { Number = number; Title = title; Duration = duration; }
    }

    private sealed class PressRecord
    {
        public string Id;
        public double At;
        public int BeforeVideo;
    }

    private sealed class FakeDriver : IStudyDriver
    {
        public readonly List<Video> Videos = new List<Video>();
        public readonly List<PressRecord> Presses = new List<PressRecord>();
        public readonly List<string> Reports = new List<string>();
        public readonly List<double> FullCaptureTimes = new List<double>();
        public readonly List<double> LightCaptureTimes = new List<double>();
        public int Current;
        public int FullCaptures;
        public int LightCaptures;
        public double Time;
        public bool StopRequested;
        public double CancelAt = double.PositiveInfinity;
        public double PauseAt = double.PositiveInfinity;
        public double CourseChangeAt = double.PositiveInfinity;
        public double ManualChapterAt = double.PositiveInfinity;
        public double CardChangeAt = double.PositiveInfinity;
        public double UnknownMarkAt = double.PositiveInfinity;
        public double MultiplePlayersAt = double.PositiveInfinity;
        public double MultipleTasksAt = double.PositiveInfinity;
        public double FocusLostAt = double.PositiveInfinity;
        public bool FrozenCard;
        public bool InitialQuiz;
        public bool StaleClockAfterNavigation;
        public bool StaleMarkAfterNavigation;
        public double CaptureDelay;
        public double FirstCaptureDelay;
        public bool CaptureClockAtStart;
        public Action<FakeDriver, CourseSnapshot> CaptureOverride;
        public int PendingCompletedIndex = -1;
        private bool cachedDirectoryChanged;
        private Playback lastPlayback;
        private Playback navigationSource;
        public bool Cancelled { get { return StopRequested; } }
        public double Seconds { get { return Time; } }

        public bool Complete(Video video)
        {
            if (video.InitiallyComplete) return true;
            if (video.NeverComplete) return false;
            if (video.CompleteAtGlobal >= 0) return Time >= video.CompleteAtGlobal;
            return video.Position >= (video.CompleteAt >= 0 ? video.CompleteAt : video.Duration * 0.9);
        }

        public void Guard()
        {
            if (StopRequested) throw new OperationCanceledException();
            if (Time >= FocusLostAt) throw new InvalidOperationException("学习通失去前台焦点");
        }

        public bool Wait(double seconds)
        {
            if (seconds <= 0) throw new Exception("TEST: wait must advance time");
            if (Time + seconds > 10000) throw new Exception("TEST: session did not terminate");
            double advance = Math.Max(0, Math.Min(seconds, CancelAt - Time));
            Video video = Videos[Current];
            if (video.Playing)
            {
                double playingSeconds = Math.Max(0, Math.Min(advance, PauseAt - Time));
                video.Position = Math.Min(Math.Min(video.Duration, video.StallAt), video.Position + playingSeconds);
            }
            Time += advance;
            if (Time >= PauseAt) video.Playing = false;
            if (Time >= CancelAt) StopRequested = true;
            return !StopRequested;
        }

        public CourseSnapshot Capture(bool full)
        {
            double startingPosition = Videos[Current].Position;
            double captureDelay = CaptureDelay + (FullCaptures + LightCaptures == 0 ? FirstCaptureDelay : 0);
            if (captureDelay > 0) Wait(captureDelay);
            if (full)
            {
                FullCaptures++;
                FullCaptureTimes.Add(Time);
                cachedDirectoryChanged = Time >= CourseChangeAt;
            }
            else
            {
                LightCaptures++;
                LightCaptureTimes.Add(Time);
            }
            CourseSnapshot snapshot = new CourseSnapshot();
            for (int i = 0; i < Videos.Count; i++)
            {
                Video v = Videos[i];
                snapshot.Chapters.Add(new Chapter {
                    Number = v.Number,
                    Title = v.Title + (cachedDirectoryChanged && i == 0 ? "（另一课程）" : ""),
                    Pending = !Complete(v) || i == PendingCompletedIndex,
                    Node = new UiNode { Name = v.Title, Id = "chapter:" + i, Role = "ListItem" }
                });
            }
            snapshot.Chapters.Add(new Chapter {
                Number = "10.9", Title = "章节测验", Pending = true,
                Node = new UiNode { Name = "章节测验", Id = "quiz", Role = "ListItem" }
            });
            Video current = Videos[Current];
            snapshot.CurrentTitle = InitialQuiz ? "章节测验" : current.Title;
            if (Time >= ManualChapterAt) snapshot.CurrentTitle = "用户手动打开的另一节";
            bool unknownTime = current.TimeUnknownUntilPlay && !current.Playing;
            snapshot.Playback = new Playback {
                Mark = Time >= UnknownMarkAt ? TaskMark.Unknown : (Complete(current) ? TaskMark.Complete : TaskMark.Incomplete),
                TaskCount = Time >= MultipleTasksAt ? 2 : 1,
                PlayerCount = Time >= MultiplePlayersAt ? 2 : 1,
                Position = unknownTime ? -1 : (CaptureClockAtStart ? startingPosition : current.Position),
                Duration = unknownTime ? -1 : current.Duration,
                Playing = current.Playing,
                PlayButton = current.Playing ? null : new UiNode { Name = "播放", Id = "play", Role = "Button" },
                CardIdentity = InitialQuiz ? "quiz-card" : (FrozenCard ? "card:fixed" : "card:" + Current + (Time >= CardChangeAt ? ":changed" : ""))
            };
            if (navigationSource != null)
            {
                if (StaleClockAfterNavigation)
                {
                    snapshot.Playback.Position = navigationSource.Position;
                    snapshot.Playback.Duration = navigationSource.Duration;
                }
                if (StaleMarkAfterNavigation) snapshot.Playback.Mark = navigationSource.Mark;
            }
            if (CaptureOverride != null) CaptureOverride(this, snapshot);
            lastPlayback = snapshot.Playback;
            return snapshot;
        }

        public void Press(UiNode node)
        {
            Guard();
            Presses.Add(new PressRecord { Id = node.Id, At = Time, BeforeVideo = Current });
            if (node.Id == "play") Videos[Current].Playing = true;
            else if (node.Id.StartsWith("chapter:"))
            {
                navigationSource = lastPlayback;
                Current = int.Parse(node.Id.Substring("chapter:".Length));
                InitialQuiz = false;
            }
            else throw new Exception("TEST: attempted to interact with a quiz or unknown control: " + node.Id);
        }

        public VideoSession Session()
        { return new VideoSession(this, delegate(string report) { Reports.Add(report); }); }
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static Exception RunFailure(VideoSession session)
    {
        try { session.Run(); }
        catch (InvalidOperationException error) { return error; }
        throw new Exception("Expected a safe InvalidOperationException stop");
    }

    private static FakeDriver One(double duration)
    {
        FakeDriver driver = new FakeDriver();
        driver.Videos.Add(new Video("10.4", "劳动世界的新劳动者", duration) { Playing = true });
        return driver;
    }

    private static Playback CompletedPlayback()
    {
        return new Playback { Mark = TaskMark.Complete, TaskCount = 1, PlayerCount = 1,
            Position = 100, Duration = 100, CardIdentity = "reused-shell" };
    }

    private static Playback UnplayedPlayback()
    {
        return new Playback { Mark = TaskMark.Incomplete, TaskCount = 1, PlayerCount = 1,
            Position = -1, Duration = -1, CardIdentity = "reused-shell",
            PlayButton = new UiNode { Name = "播放视频", Role = "Button" } };
    }

    private static void Run(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine("PASS session: " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL session: " + name + ": " + error.Message); }
    }

    public static int Run()
    {
        int failures = 0;

        Run("early server completion still waits until last twenty seconds", delegate {
            FakeDriver d = One(100);
            d.Videos[0].CompleteAt = 50;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            VideoSession s = d.Session();
            s.Run();
            PressRecord next = d.Presses.First(p => p.Id == "chapter:1");
            Check(next.At >= 80 && next.At < 82, "first switch must be at the planned end-minus-20 check");
            Check(s.Completed == 2, "both actual video completions should be counted");
            Check(!d.Presses.Any(p => p.Id == "quiz"), "tests must not be opened");
            Check(d.LightCaptures > 0, "use lighter progress checks before the final check");
        }, ref failures);

        Run("near-end timer alone cannot switch before completion mark", delegate {
            FakeDriver d = One(50);
            d.Videos[0].CompleteAtGlobal = 56;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            VideoSession s = d.Session();
            s.Run();
            double switchAt = d.Presses.First(p => p.Id == "chapter:1").At;
            Check(switchAt >= 56 && switchAt <= 58, "wait beyond the end until a real completed mark is returned");
            Check(s.Completed == 2, "confirmed completed marks only");
        }, ref failures);

        Run("near-end completion polls refresh the card without repeatedly reading the directory", delegate {
            FakeDriver d = One(50);
            d.Videos[0].CompleteAtGlobal = 56;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1, "the real completion mark is still required");
            Check(d.FullCaptureTimes.SequenceEqual(new double[] { 0, 30, 56 }),
                "only initial capture, thirty-second audit and final pre-navigation capture should be full");
            Check(d.LightCaptureTimes.Count(t => t >= 32 && t <= 56) == 13,
                "poll the card every two seconds through the final twenty seconds and server delay");
            Check(!d.FullCaptureTimes.Any(t => t > 30 && t < 56),
                "near-end polls must not repeatedly refresh the directory");
        }, ref failures);

        Run("cached light directory is checked again at the thirty-second audit", delegate {
            FakeDriver d = One(100);
            d.CourseChangeAt = 12;
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("目录发生变化"), "the audit must reject a changed course directory");
            Check(d.LightCaptureTimes.Contains(20), "a light read should retain the previous directory until the audit");
            Check(d.FullCaptureTimes.SequenceEqual(new double[] { 0, 30 }) && d.Time == 30,
                "perform the next full directory audit after thirty seconds");
            Check(d.Presses.Count == 0 && s.Completed == 0, "reject a changed directory before any completion or navigation");
        }, ref failures);

        Run("ended video without completion mark stops after bounded grace period", delegate {
            FakeDriver d = One(50);
            d.Videos[0].NeverComplete = true;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("60秒"), "explain missing server completion after grace period");
            Check(d.Time >= 110 && d.Time <= 114, "wait about 60 seconds past the actual end");
            Check(s.Completed == 0 && d.Presses.Count == 0, "never mark complete or advance on a local timer");
        }, ref failures);

        Run("already completed video is neither replayed nor counted", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Playing = false;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 0, "pre-existing completion does not count as newly completed");
            Check(d.Presses.Count == 0 && d.Time == 0, "do not replay a completed video");
        }, ref failures);

        Run("skip completed first section then play unfinished section", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30) { Playing = false });
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1, "only unfinished video completion counts");
            Check(d.Presses.Count(p => p.Id == "play") == 1, "start only the second video");
            Check(d.Presses.Where(p => p.Id == "play").All(p => p.BeforeVideo == 1), "never replay the green-checked first video");
        }, ref failures);

        Run("starting in the middle never returns to earlier sections with stale pending counters", delegate {
            FakeDriver d = One(100);
            d.Videos.Add(new Video("10.5", "当前已完成视频", 100) { InitiallyComplete = true, Position = 100 });
            d.Videos.Add(new Video("10.6", "后续未完成视频", 30));
            d.Current = 1;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && d.Presses.First().Id == "chapter:2",
                "select the pending video after the current section");
            Check(!d.Presses.Any(p => p.Id == "chapter:0") && d.Videos[0].Position == 0,
                "never revisit earlier entries whose unfinished quiz keeps their aggregate counter positive");
            Check(!d.Presses.Any(p => p.Id == "quiz"), "the chapter test is never selected");
        }, ref failures);

        Run("the final section does not wrap to earlier pending rows", delegate {
            FakeDriver d = One(100);
            d.Videos.Add(new Video("10.5", "目录最后的视频", 100) { InitiallyComplete = true, Position = 100 });
            d.Current = 1;
            VideoSession s = d.Session();
            s.Run();
            Check(d.Presses.Count == 0 && s.Completed == 0 && d.Time == 0,
                "a pending earlier entry does not extend the forward processing range");
            Check(d.Reports.Any(r => r.Contains("从当前小节向后没有")),
                "report completion of the current forward range rather than the entire course");
        }, ref failures);

        Run("a completed target with a reset player is skipped without playing", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Position = 100;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "目录计数滞后的已完成视频", 30) { InitiallyComplete = true });
            d.Videos.Add(new Video("10.6", "真正未完成的视频", 40));
            d.PendingCompletedIndex = 1;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && d.Presses.Any(p => p.Id == "chapter:1") && d.Presses.Any(p => p.Id == "chapter:2"),
                "verify and skip the reset completed target before continuing forward");
            Check(d.Presses.Where(p => p.Id == "play").All(p => p.BeforeVideo == 2),
                "never replay the pre-existing completed target");
        }, ref failures);

        Run("a completed target that still shows the old ending clock is not skipped", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Position = 100;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "新标题旧片尾", 100) { InitiallyComplete = true, Position = 100 });
            d.Videos.Add(new Video("10.6", "后续视频", 30));
            d.PendingCompletedIndex = 1;
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("25秒") && s.Completed == 0,
                "a completed mark and changed title cannot substitute for a newly loaded player");
            Check(!d.Presses.Any(p => p.Id == "play" || p.Id == "chapter:2"),
                "do not replay or silently skip the ambiguous target");
        }, ref failures);

        Run("two completed pages with unknown metadata do not prove a new completed target", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Playing = false;
            d.Videos[0].TimeUnknownUntilPlay = true;
            d.Videos.Add(new Video("10.5", "新标题未确认播放器", 30) { InitiallyComplete = true, TimeUnknownUntilPlay = true });
            d.Videos.Add(new Video("10.6", "后续视频", 30));
            d.PendingCompletedIndex = 1;
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("25秒") && s.Completed == 0,
                "unchanged complete status and missing clocks cannot prove a new player");
            Check(!d.Presses.Any(p => p.Id == "play" || p.Id == "chapter:2"),
                "do not silently skip an unconfirmed completed target");
            Playback loadedOld = CompletedPlayback();
            Playback unloadedComplete = UnplayedPlayback(); unloadedComplete.Mark = TaskMark.Complete;
            Check(TransitionReadiness.IsNewPlayerState(loadedOld, unloadedComplete),
                "a known old clock unloading is evidence for a newly loaded completed target");
            loadedOld.Position = -1; loadedOld.Duration = -1;
            Check(!TransitionReadiness.IsNewPlayerState(loadedOld, unloadedComplete),
                "two unknown clocks with complete marks provide no transition evidence");
            Check(!TransitionReadiness.IsFresh(CompletedPlayback(), unloadedComplete),
                "verification freshness must remain restricted to incomplete targets");
        }, ref failures);

        Run("initial paused video can be started before duration is loaded", delegate {
            FakeDriver d = One(30);
            d.Videos[0].Playing = false;
            d.Videos[0].TimeUnknownUntilPlay = true;
            VideoSession s = d.Session();
            s.Run();
            Check(d.Presses.Count(p => p.Id == "play") == 1, "click normal play once");
            Check(s.Completed == 1, "read duration after play exposes metadata");
        }, ref failures);

        Run("pause during playback safely stops without pressing play again", delegate {
            FakeDriver d = One(100);
            d.PauseAt = 25;
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("暂停"), "explain unexpected pause");
            Check(s.Completed == 0 && !d.Presses.Any(p => p.Id == "play"), "do not auto-dismiss pause or verification");
        }, ref failures);

        Run("stalled progress safely stops", delegate {
            FakeDriver d = One(100);
            d.Videos[0].StallAt = 0;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("40秒"), "bounded stall detection");
            Check(d.Time >= 40 && d.Time <= 42, "stop after forty seconds of no progress");
            Check(d.Presses.Count == 0, "no speculative click after stall");
        }, ref failures);

        Run("one erroneous total-time position recovers without poisoning the progress baseline", delegate {
            FakeDriver d = One(934);
            d.Videos[0].Position = 777;
            int glitches = 1;
            d.CaptureOverride = delegate(FakeDriver capture, CourseSnapshot snapshot) {
                if (capture.Time >= 10 && glitches > 0)
                { glitches--; snapshot.Playback.Position = snapshot.Playback.Duration; }
            };
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && d.Time >= 137 && d.Time <= 139,
                "normal progress after the transient end-clock must complete without a false forty-second stall");
            Check(d.FullCaptureTimes.Count(t => t == 10) == 1,
                "recheck the suspicious light read immediately with one full capture");
            Check(d.Reports.Count(r => r.Contains("时间读数异常")) == 1,
                "only the one bad position should require a retry");
        }, ref failures);

        Run("a completion mark paired with an impossible clock jump cannot advance chapters", delegate {
            FakeDriver d = One(100);
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            int glitches = 1;
            d.CaptureOverride = delegate(FakeDriver capture, CourseSnapshot snapshot) {
                if (capture.Current == 0 && capture.Time >= 10 && glitches > 0)
                {
                    glitches--;
                    snapshot.Playback.Position = snapshot.Playback.Duration;
                    snapshot.Playback.Mark = TaskMark.Complete;
                }
            };
            VideoSession s = d.Session();
            s.Run();
            double switchAt = d.Presses.Single(p => p.Id == "chapter:1").At;
            Check(switchAt >= 90 && s.Completed == 2,
                "discard the false completed ending clock and wait for credible playback plus the real task mark");
        }, ref failures);

        Run("temporarily missing current time can recover on the second full recheck", delegate {
            FakeDriver d = One(100);
            int glitches = 2;
            d.CaptureOverride = delegate(FakeDriver capture, CourseSnapshot snapshot) {
                if (capture.Time >= 10 && glitches > 0)
                { glitches--; snapshot.Playback.Position = -1; }
            };
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && d.FullCaptureTimes.Count(t => t == 10) == 2,
                "allow two full reads to replace invalid mixed metadata before failing time validation");
            Check(d.Reports.Count(r => r.Contains("时间读数异常")) == 2,
                "both rechecks must remain anchored to the last accepted clock");
        }, ref failures);

        Run("a persistent impossible forward jump stops after exactly two full rechecks", delegate {
            FakeDriver d = One(1000);
            d.Videos[0].Position = 100;
            d.CaptureOverride = delegate(FakeDriver capture, CourseSnapshot snapshot) {
                if (capture.Time >= 10) snapshot.Playback.Position = snapshot.Playback.Duration;
            };
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("连续跳变") && d.Time == 10,
                "persistent seek or corrupt time must fail promptly rather than become a new baseline");
            Check(d.FullCaptureTimes.Count(t => t == 10) == 2 && d.Presses.Count == 0 && s.Completed == 0,
                "bound retries and never act on the implausible ending clock");
        }, ref failures);

        Run("a persistent backward clock stops without accepting a new progress baseline", delegate {
            FakeDriver d = One(100);
            d.Videos[0].Position = 50;
            d.CaptureOverride = delegate(FakeDriver capture, CourseSnapshot snapshot) {
                if (capture.Time >= 10) snapshot.Playback.Position = 20;
            };
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("倒退") && d.Time == 10 && s.Completed == 0 && d.Presses.Count == 0,
                "a sustained seek backwards is not normal playback progress");
        }, ref failures);

        Run("rechecking an anomalous clock does not extend a real stall deadline", delegate {
            FakeDriver d = One(100);
            d.Videos[0].StallAt = 0;
            int glitches = 1;
            d.CaptureOverride = delegate(FakeDriver capture, CourseSnapshot snapshot) {
                if (capture.Time >= 30 && glitches > 0)
                { glitches--; snapshot.Playback.Position = snapshot.Playback.Duration; }
            };
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("40秒") && d.Time == 40 && d.Presses.Count == 0,
                "a recovered read with no actual progress must not reset lastProgressAt");
        }, ref failures);

        Run("normal progress during a slow UIA capture remains plausible", delegate {
            FakeDriver d = One(100);
            d.CaptureDelay = 5;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && !d.Reports.Any(r => r.Contains("时间读数异常")),
                "sampling timestamps must include the UIA read duration instead of mistaking read latency for a seek");
        }, ref failures);

        Run("an old clock returned by a slow capture may catch up on the following fast capture", delegate {
            FakeDriver d = One(300);
            d.Videos[0].Position = 50;
            d.FirstCaptureDelay = 34;
            d.CaptureClockAtStart = true;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && !d.Reports.Any(r => r.Contains("时间读数异常")),
                "bound plausible progress by the previous capture start and current capture end");
        }, ref failures);

        Run("clock plausibility bounds elapsed progress, rollback, duration and finite values", delegate {
            Playback clock = new Playback { Position = 18, Duration = 100 };
            Check(PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "allow elapsed time plus three seconds of clock skew");
            clock.Position = 18.01;
            Check(!PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "reject progress beyond elapsed plus tolerance");
            clock.Position = 8;
            Check(PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "allow at most two seconds of read-order jitter");
            clock.Position = 7.99;
            Check(!PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "reject a larger backward step");
            clock.Position = 15; clock.Duration = 101;
            Check(PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "allow one second of total-time rounding");
            clock.Duration = 101.01;
            Check(!PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "reject a changed media duration");
            clock.Duration = 100;
            Check(!PlaybackClock.IsPlausible(10, 100, 20, clock, 19), "sample time cannot run backwards");
            foreach (double invalid in new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                clock.Position = invalid;
                Check(!PlaybackClock.IsPlausible(10, 100, 20, clock, 25), "nonfinite player time is never accepted");
                clock.Position = 15;
                Check(!PlaybackClock.IsPlausible(10, 100, invalid, clock, 25), "nonfinite previous sample time is never accepted");
                Check(!PlaybackClock.IsPlausible(10, 100, 20, clock, invalid), "nonfinite current sample time is never accepted");
            }
        }, ref failures);

        Run("course directory change stops the session", delegate {
            FakeDriver d = One(100);
            d.CourseChangeAt = 20;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("目录发生变化"), "detect course identity change");
            Check(d.Presses.Count == 0, "do not operate another course");
        }, ref failures);

        Run("manual section change stops the session", delegate {
            FakeDriver d = One(100);
            d.ManualChapterAt = 20;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("小节已改变"), "detect user changing chapter");
            Check(d.Presses.Count == 0, "do not pull user back to original section");
        }, ref failures);

        Run("player card identity change stops even with same title", delegate {
            FakeDriver d = One(100);
            d.CardChangeAt = 20;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("播放器内容发生变化"), "detect replaced frame with unchanged title");
        }, ref failures);

        Run("unknown task state during playback stops", delegate {
            FakeDriver d = One(100);
            d.UnknownMarkAt = 20;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("任务点状态"), "do not infer task completion from time");
        }, ref failures);

        Run("multiple players appearing during playback stops", delegate {
            FakeDriver d = One(100);
            d.MultiplePlayersAt = 20;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("唯一的视频播放器"), "unsupported page does not choose arbitrary video");
        }, ref failures);

        Run("stale card after navigation times out before any play action", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Position = 100;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30) { Playing = false });
            d.FrozenCard = true;
            d.StaleClockAfterNavigation = true;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("25秒"), "a title change must not authorize replay of the old ending clock");
            Check(!d.Presses.Any(p => p.Id == "play"), "never click stale old player");
        }, ref failures);

        Run("reused native card identity permits a reset paused video to start", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Position = 100;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            d.FrozenCard = true;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && d.Presses.Count(p => p.Id == "play") == 1,
                "a reused wrapper must not prevent the fresh next video from playing");
            Check(d.Presses.Single(p => p.Id == "play").At >= 2,
                "require two stable title and reset-state observations before play");
        }, ref failures);

        Run("completed old page with unknown metadata can enter a new unloaded normal player", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Playing = false;
            d.Videos[0].TimeUnknownUntilPlay = true;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30) { TimeUnknownUntilPlay = true });
            d.FrozenCard = true;
            VideoSession s = d.Session();
            s.Run();
            Check(s.Completed == 1 && d.Presses.Count(p => p.Id == "play") == 1,
                "normal unloaded metadata becomes valid only after the one allowed play action");
        }, ref failures);

        Run("new title with an old complete mark cannot authorize playback", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Position = 100;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            d.StaleMarkAfterNavigation = true;
            d.StaleClockAfterNavigation = true;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("25秒") && !d.Presses.Any(p => p.Id == "play"),
                "the previous completed task must not be assigned to the new title");
        }, ref failures);

        Run("autoplaying target is conservatively refused before any play action", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Position = 100;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 100) { Playing = true });
            d.StaleClockAfterNavigation = true;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("25秒") && !d.Presses.Any(p => p.Id == "play"),
                "a playing old clock is not proof of a newly loaded paused player");
        }, ref failures);

        Run("unknown next task state is never promoted to a fresh transition", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos[0].Playing = false;
            d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
            d.UnknownMarkAt = 1;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("25秒") && !d.Presses.Any(p => p.Id == "play"),
                "a loaded title with unknown task state must time out safely");
        }, ref failures);

        Run("ambiguous target tasks or players prevent transition play", delegate {
            for (int ambiguity = 0; ambiguity < 2; ambiguity++)
            {
                FakeDriver d = One(100);
                d.Videos[0].InitiallyComplete = true;
                d.Videos[0].Playing = false;
                d.Videos.Add(new Video("10.5", "人类劳动新特征", 30));
                if (ambiguity == 0) d.MultipleTasksAt = 1; else d.MultiplePlayersAt = 1;
                Exception error = RunFailure(d.Session());
                Check(error.Message.Contains("25秒") && !d.Presses.Any(p => p.Id == "play"),
                    "no arbitrary task or player may be selected after navigation");
            }
        }, ref failures);

        Run("transition freshness requires a genuine clock reset or fully unloaded metadata", delegate {
            Playback old = CompletedPlayback();
            Playback next = UnplayedPlayback();
            Check(TransitionReadiness.IsFresh(old, next), "fully unloaded metadata supports reused shells");
            next.Position = 0; next.Duration = 100;
            Check(TransitionReadiness.IsFresh(old, next), "a clear position rollback supports equal-duration videos");
            next.Position = 40; next.Duration = 150;
            Check(TransitionReadiness.IsFresh(old, next), "a different total duration supports a partially watched next video");
            next.Position = 100; next.Duration = 100;
            Check(!TransitionReadiness.IsFresh(old, next), "the previous ending clock remains stale");
            next.Position = 100; next.Duration = -1;
            Check(!TransitionReadiness.IsFresh(old, next), "old position with missing duration is a partial stale frame");
            next.Position = -1; next.Duration = 100;
            Check(!TransitionReadiness.IsFresh(old, next), "missing position with old duration is a partial stale frame");
            next.Position = 101; next.Duration = 100;
            Check(!TransitionReadiness.IsFresh(old, next), "known invalid or ended metadata does not count as unloaded");
        }, ref failures);

        Run("transition freshness rejects nonfinite metadata and replay controls", delegate {
            foreach (double invalid in new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Playback old = CompletedPlayback();
                Playback next = UnplayedPlayback();
                next.Position = invalid;
                Check(!TransitionReadiness.IsFresh(old, next), "a nonfinite next position cannot prove freshness");
                next = UnplayedPlayback(); next.Duration = invalid;
                Check(!TransitionReadiness.IsFresh(old, next), "a nonfinite next duration cannot prove freshness");
                old.Position = invalid;
                Check(!TransitionReadiness.IsFresh(old, UnplayedPlayback()), "a nonfinite old clock cannot prove freshness");
            }
            foreach (string replay in new string[] { "重播", "Replay", "Pause", "" })
            {
                Playback next = UnplayedPlayback(); next.PlayButton.Name = replay;
                Check(!TransitionReadiness.IsFresh(CompletedPlayback(), next), "only normal play controls can start a fresh target");
            }
        }, ref failures);

        Run("start from chapter test safely requires an explicit video page", delegate {
            FakeDriver d = One(30);
            d.InitialQuiz = true;
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("明确的视频页面"), "explain the required starting page");
            Check(s.Completed == 0 && d.Presses.Count == 0 && d.Time == 0,
                "an unidentified initial video context must stop before any navigation or play");
        }, ref failures);

        Run("loss of target foreground stops promptly", delegate {
            FakeDriver d = One(100);
            d.FocusLostAt = 5;
            Exception error = RunFailure(d.Session());
            Check(error.Message.Contains("前台"), "stop when user takes over computer");
            Check(d.Time <= 6 && d.Presses.Count == 0, "do not wait until final completion check to notice lost focus");
        }, ref failures);

        Run("stop request cancels a wait without any subsequent action", delegate {
            FakeDriver d = One(100);
            d.CancelAt = 5;
            VideoSession s = d.Session();
            bool cancelled = false;
            try { s.Run(); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "cooperative cancellation is reported");
            Check(d.Time == 5 && d.Presses.Count == 0 && s.Completed == 0, "cancel must not click or increment completion");
        }, ref failures);

        Run("already cancelled session never captures or acts", delegate {
            FakeDriver d = One(100);
            d.StopRequested = true;
            bool cancelled = false;
            try { d.Session().Run(); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "initial cancellation is honored");
            Check(d.FullCaptures == 0 && d.LightCaptures == 0 && d.Presses.Count == 0, "no UI work after cancellation");
        }, ref failures);

        Run("different numbered videos sharing a title stop before any click", delegate {
            FakeDriver d = One(100);
            d.Videos[0].InitiallyComplete = true;
            d.Videos.Add(new Video("10.5", d.Videos[0].Title, 30) { Playing = false });
            VideoSession s = d.Session();
            Exception error = RunFailure(s);
            Check(error.Message.Contains("同名视频"), "ambiguous duplicate titles must not inherit the other video's completion");
            Check(d.Presses.Count == 0 && d.Time == 0 && s.Completed == 0, "detect ambiguity before navigation, play, or completion counting");
        }, ref failures);

        Console.WriteLine("SESSION RESULT " + (43 - failures) + " passed, " + failures + " failed");
        return failures;
    }
}
