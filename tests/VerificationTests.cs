using System;
using System.Collections.Generic;
using System.Linq;
using StudyVideoHelper;

internal static class VerificationTests
{
    private sealed class Video
    {
        public string Title;
        public double Position;
        public double Duration = 50;
        public bool Playing;
        public bool InitiallyComplete;
        public bool NeverComplete;
        public bool Stall;
    }

    private sealed class Driver : IStudyDriver
    {
        public readonly List<Video> Videos = new List<Video>();
        public readonly List<string> Actions = new List<string>();
        public readonly List<string> Reports = new List<string>();
        public int Current;
        public double Time;
        public double CancelAt = double.PositiveInfinity;
        public double PauseAt = double.PositiveInfinity;
        public double ManualChangeAt = double.PositiveInfinity;
        public double CourseChangeAt = double.PositiveInfinity;
        public bool Stop;
        public bool FrozenCard;
        public bool FakeNextClockJump;
        public bool StaleCompletedRow;
        public bool ReplaceNextCardDuringProof;
        public bool CancelDuringNextProgressCapture;
        public int NextPlayingCaptures;
        public bool NextClockSpikeOnce;
        public bool NextClockSpikeAlways;
        public bool NextClockRecentAcceptedSpike;
        public int NextClockSpikes;
        public int NormalNextCapturesAfterSpike;
        public bool SlowFirstNextPlayingCapture;
        public int SlowNextCaptures;
        public bool OldNextMetadata;
        public bool NoNextClockReset;
        public bool TransientNextFreshness;
        public int NextFullCaptures;
        public int NextPlayPresses;
        public int NextFullCapturesAtPlay;
        public bool FreshReportAtNextPlay;
        public bool Cancelled { get { return Stop; } }
        public double Seconds { get { return Time; } }

        public Driver(int count)
        {
            for (int i = 0; i < count; i++) Videos.Add(new Video { Title = "视频" + (i + 1) });
        }

        private bool Complete(Video v) { return v.InitiallyComplete || (!v.NeverComplete && v.Position >= v.Duration * 0.9); }
        public void Guard() { if (Stop) throw new OperationCanceledException(); }
        public bool Wait(double seconds)
        {
            if (Time + seconds > 5000) throw new Exception("Verification did not terminate");
            if (Videos[Current].Playing && !Videos[Current].Stall)
                Videos[Current].Position = Math.Min(Videos[Current].Duration, Videos[Current].Position + seconds);
            Time += seconds;
            if (Time >= PauseAt) Videos[Current].Playing = false;
            if (Time >= CancelAt) Stop = true;
            return !Stop;
        }

        public CourseSnapshot Capture(bool full)
        {
            CourseSnapshot snapshot = new CourseSnapshot();
            for (int i = 0; i < Videos.Count; i++)
                snapshot.Chapters.Add(new Chapter { Number = "1." + (i + 1), Title = Videos[i].Title +
                    (Time >= CourseChangeAt && i == 0 ? "另一课程" : ""), Pending = !Complete(Videos[i]) || (StaleCompletedRow && i == 1),
                    Node = new UiNode { Name = Videos[i].Title, Id = "chapter:" + i, Role = "ListItem" } });
            Video current = Videos[Current];
            snapshot.CurrentTitle = Time >= ManualChangeAt ? "用户手动切换的页面" : current.Title;
            snapshot.Playback = new Playback { Mark = Complete(current) ? TaskMark.Complete : TaskMark.Incomplete,
                TaskCount = 1, PlayerCount = 1, Position = current.Position, Duration = current.Duration,
                Playing = current.Playing,
                PlayButton = current.Playing ? null : new UiNode { Name = "播放视频", Id = "play", Role = "Button" },
                CardIdentity = FrozenCard ? "fixed" : "card:" + Current };
            if (Current == 1)
            {
                if (full) NextFullCaptures++;
                if (OldNextMetadata || NoNextClockReset || (TransientNextFreshness && NextFullCaptures > 1))
                {
                    snapshot.Playback.Position = Videos[0].Position;
                    snapshot.Playback.Duration = Videos[0].Duration;
                    if (!NoNextClockReset)
                    {
                        snapshot.Playback.Mark = TaskMark.Complete;
                        snapshot.Playback.Playing = true;
                        snapshot.Playback.PlayButton = null;
                    }
                }
            }
            if (FakeNextClockJump && Current == 1) snapshot.Playback.Position = 30;
            if (Current == 1 && current.Playing)
            {
                NextPlayingCaptures++;
                bool spike = (NextClockSpikeOnce && NextPlayingCaptures == 2) ||
                    (NextClockSpikeAlways && NextPlayingCaptures >= 2);
                if (NextClockRecentAcceptedSpike)
                {
                    snapshot.Playback.Position = NextPlayingCaptures == 1 ? 0 : NextPlayingCaptures == 2 ? 1 : 20;
                    spike = NextPlayingCaptures >= 3;
                }
                else if (spike) snapshot.Playback.Position = snapshot.Playback.Duration;
                if (spike) NextClockSpikes++;
                else if (NextClockSpikes > 0) NormalNextCapturesAfterSpike++;
                if (NextPlayingCaptures >= 2)
                {
                    if (ReplaceNextCardDuringProof) snapshot.Playback.CardIdentity = "replaced:1";
                    if (CancelDuringNextProgressCapture) Stop = true;
                }
                if (SlowFirstNextPlayingCapture && NextPlayingCaptures == 1)
                {
                    // Return the clock read at Capture's beginning after a long UIA
                    // traversal; the real player keeps advancing during that read.
                    SlowNextCaptures++;
                    Wait(34);
                }
            }
            return snapshot;
        }

        public void Press(UiNode node)
        {
            Guard(); Actions.Add(node.Id);
            if (node.Id == "play")
            {
                if (Current == 1)
                {
                    NextPlayPresses++;
                    NextFullCapturesAtPlay = NextFullCaptures;
                    FreshReportAtNextPlay = Reports.Any(r => r.StartsWith("FRESH："));
                }
                Videos[Current].Playing = true;
            }
            else if (node.Id.StartsWith("chapter:")) Current = int.Parse(node.Id.Substring(8));
            else throw new Exception("Unexpected action " + node.Id);
        }

        public bool Verify() { return TransitionVerification.Run(this, delegate(string line) { Reports.Add(line); }); }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Fails(Driver d, string contains)
    {
        bool failed = false;
        try { failed = !d.Verify(); }
        catch (InvalidOperationException error) { failed = contains == "" || error.Message.Contains(contains); }
        Check(failed, "Expected an unsuccessful verification with reason " + contains);
        Check(!d.Reports.Any(r => r.StartsWith("PASS：")), "An incomplete proof emitted PASS");
    }
    private static void Test(string name, Action test, ref int failures, ref int passed)
    {
        try { test(); passed++; Console.WriteLine("PASS verification: " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL verification: " + name + ": " + error.Message); }
    }

    public static int Run()
    {
        int failures = 0, passed = 0;
        Test("new completion then actual navigation and advancing next playback succeeds and stops before third", delegate {
            Driver d = new Driver(3);
            Check(d.Verify(), "Normal first-to-second playback proof failed");
            Check(d.Current == 1, "Verification ran past the second video");
            Check(d.Videos[1].Position >= 3, "Next video never advanced after its initial observation");
            Check(d.Videos[1].Position < d.Videos[1].Duration * 0.9, "Verification unnecessarily completed the second video");
            Check(d.Actions.Count(a => a.StartsWith("chapter:")) == 1 && !d.Actions.Contains("chapter:2"), "Unexpected directory action count");
            Check(d.NextFullCapturesAtPlay >= 2 && d.FreshReportAtNextPlay,
                "Verifier did not confirm two fresh full observations before the session pressed next play");
            Check(d.Reports.Last().StartsWith("PASS："), "Missing final proof report");
        }, ref failures, ref passed);
        Test("already completed initial video is rejected with no action", delegate {
            Driver d = new Driver(2); d.Videos[0].InitiallyComplete = true;
            Fails(d, "实测起点"); Check(d.Actions.Count == 0, "A completed starting video caused an action");
        }, ref failures, ref passed);
        Test("timer and video end without task completion cannot pass", delegate {
            Driver d = new Driver(2); d.Videos[0].NeverComplete = true;
            Fails(d, "60秒"); Check(!d.Actions.Contains("chapter:1"), "Changed chapter without completion");
        }, ref failures, ref passed);
        Test("manual chapter change is not a program transition", delegate {
            Driver d = new Driver(2); d.ManualChangeAt = 12;
            Fails(d, "小节"); Check(!d.Actions.Contains("chapter:1"), "Manual change caused an unearned navigation");
        }, ref failures, ref passed);
        Test("pause in next video never becomes a successful proof", delegate {
            Driver d = new Driver(2); d.PauseAt = 50;
            Fails(d, "暂停");
        }, ref failures, ref passed);
        Test("stalled next video cannot pass from just a title change", delegate {
            Driver d = new Driver(3); d.Videos[1].Stall = true;
            Fails(d, "未前进"); Check(!d.Actions.Contains("chapter:2"), "Stall caused a third video to be opened");
        }, ref failures, ref passed);
        Test("fixed next clock cannot pass from just a nonzero time", delegate {
            Driver d = new Driver(2); d.FakeNextClockJump = true; d.CancelAt = 75;
            Fails(d, "");
        }, ref failures, ref passed);
        Test("a single huge next clock jump cannot turn a stalled video into PASS", delegate {
            Driver d = new Driver(3); d.NextClockSpikeOnce = true; d.Videos[1].Stall = true;
            Fails(d, "未前进");
            Check(d.NextClockSpikes == 1 && d.NormalNextCapturesAfterSpike > 0,
                "The single bad read was not followed by a normal full recheck");
            Check(!d.Actions.Contains("chapter:2"), "A rejected time spike caused a third navigation");
        }, ref failures, ref passed);
        Test("next playback recovers from one clock spike and passes only on real progress", delegate {
            Driver d = new Driver(3); d.NextClockSpikeOnce = true;
            Check(d.Verify(), "Normal next playback did not recover after a single bad clock read");
            Check(d.NextClockSpikes == 1 && d.NormalNextCapturesAfterSpike >= 1 && d.NextPlayingCaptures >= 3,
                "The verifier passed on the bad read before a normal progress observation");
            Check(d.Current == 1 && d.Actions.Count(a => a.StartsWith("chapter:")) == 1,
                "Recovering a clock read caused another navigation");
        }, ref failures, ref passed);
        Test("persistent huge next clock jumps are rejected before proof succeeds", delegate {
            Driver d = new Driver(3); d.NextClockSpikeAlways = true;
            Fails(d, "视频时间");
            Check(d.NextClockSpikes >= 3 && d.NormalNextCapturesAfterSpike == 0,
                "The session did not exhaust its bounded clock rechecks");
            Check(!d.Actions.Contains("chapter:2"), "Persistent bad clock data caused a third navigation");
        }, ref failures, ref passed);
        Test("next clock plausibility uses the latest accepted read rather than the first baseline", delegate {
            Driver d = new Driver(3); d.NextClockRecentAcceptedSpike = true;
            Fails(d, "视频时间");
            Check(d.NextPlayingCaptures >= 5 && d.NextClockSpikes >= 3,
                "The rolling-baseline jump was not rechecked before stopping");
            Check(!d.Actions.Contains("chapter:2"), "An implausible rolling-clock jump caused a third navigation");
        }, ref failures, ref passed);
        Test("an early clock from a slow capture permits the next fast read to catch up normally", delegate {
            Driver d = new Driver(3); d.SlowFirstNextPlayingCapture = true; d.Videos[1].Duration = 150;
            Check(d.Verify(), "A normal fresh clock after an early sample in a slow capture was rejected");
            Check(d.SlowNextCaptures == 1 && d.NextPlayingCaptures == 2,
                "The normal catch-up read did not establish progress immediately");
            Check(!d.Reports.Any(r => r.StartsWith("CLOCK：下一视频")), "A legal slow-capture sampling window was flagged as a jump");
            Check(d.Current == 1 && !d.Actions.Contains("chapter:2"), "Slow capture caused a third navigation");
        }, ref failures, ref passed);
        Test("reused runtime identity passes only after a fresh task and advancing playback", delegate {
            Driver d = new Driver(3); d.FrozenCard = true;
            Check(d.Verify(), "A reused document runtime ID incorrectly blocked a genuinely fresh target");
            Check(d.NextPlayPresses == 1 && d.FreshReportAtNextPlay && d.NextPlayingCaptures >= 2,
                "Reused identity passed without fresh-loading and actual playback evidence");
            Check(d.Current == 1 && d.Actions.Count(a => a.StartsWith("chapter:")) == 1, "Reused identity caused an extra navigation");
        }, ref failures, ref passed);
        Test("new title with old completed player metadata cannot pass", delegate {
            Driver d = new Driver(3); d.FrozenCard = true; d.OldNextMetadata = true;
            Fails(d, "25秒");
            Check(d.NextPlayPresses == 0 && !d.Actions.Contains("chapter:2"), "Stale metadata caused a play or third navigation");
            Check(!d.Reports.Any(r => r.StartsWith("FRESH：")), "Old metadata was accepted as a fresh video");
        }, ref failures, ref passed);
        Test("incomplete mark and normal play button without a reset cannot pass", delegate {
            Driver d = new Driver(3); d.NoNextClockReset = true;
            Fails(d, "25秒");
            Check(d.NextPlayPresses == 0 && !d.Reports.Any(r => r.StartsWith("FRESH：")),
                "Unchanged clock metadata was treated as a loaded new player");
        }, ref failures, ref passed);
        Test("one transient fresh observation is insufficient to confirm loading", delegate {
            Driver d = new Driver(3); d.TransientNextFreshness = true;
            Fails(d, "25秒");
            Check(d.NextFullCaptures > 1 && d.NextPlayPresses == 0 && !d.Reports.Any(r => r.StartsWith("FRESH：")),
                "A single transient fresh snapshot was promoted to confirmed loading");
        }, ref failures, ref passed);
        Test("replacing next card after its playback baseline cannot pass", delegate {
            Driver d = new Driver(3); d.ReplaceNextCardDuringProof = true;
            Fails(d, "播放器");
            Check(d.NextPlayingCaptures == 2, "Card replacement did not occur on the would-be successful progress observation");
            Check(d.Actions.Count(a => a.StartsWith("chapter:")) == 1 && d.Current == 1, "Changed player caused another directory action");
        }, ref failures, ref passed);
        Test("cancellation during a successful progress capture takes priority", delegate {
            Driver d = new Driver(3); d.CancelDuringNextProgressCapture = true;
            Fails(d, "");
            Check(d.Stop && d.NextPlayingCaptures == 2, "Cancellation did not occur inside the would-be successful capture");
            Check(d.Reports.Last().Contains("被取消"), "Cancellation was not reported as the stopping reason");
            Check(d.Actions.Count(a => a.StartsWith("chapter:")) == 1 && d.Current == 1, "Cancellation caused another directory action");
        }, ref failures, ref passed);
        Test("user cancellation remains unsuccessful and never continues", delegate {
            Driver d = new Driver(2); d.CancelAt = 12;
            Fails(d, ""); Check(!d.Actions.Contains("chapter:1"), "Cancelled proof continued to another video");
        }, ref failures, ref passed);
        Test("no remaining next video is incomplete verification", delegate {
            Driver d = new Driver(1); Fails(d, "");
        }, ref failures, ref passed);
        Test("course fingerprint change cannot bypass safety on successful observation", delegate {
            Driver d = new Driver(2); d.CourseChangeAt = 50; Fails(d, "课程");
        }, ref failures, ref passed);
        Test("completed next video cannot be skipped to a third by verifier", delegate {
            Driver d = new Driver(3); d.Videos[1].InitiallyComplete = true; d.StaleCompletedRow = true;
            Fails(d, "");
            Check(d.Current == 1 && !d.Actions.Contains("chapter:2"), "Verifier continued through a stale completed target to a third video");
        }, ref failures, ref passed);
        Test("completed directory rows are ignored before the sole verified target", delegate {
            Driver d = new Driver(3); d.Videos[1].InitiallyComplete = true;
            Check(d.Verify(), "A valid program-selected unfinished target should pass");
            Check(d.Actions.Count(a => a.StartsWith("chapter:")) == 1, "Proof opened more than one new directory item");
        }, ref failures, ref passed);
        Console.WriteLine("VERIFICATION RESULT " + passed + " passed, " + failures + " failed");
        return failures;
    }
}
