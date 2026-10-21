using System;
using System.Collections.Generic;
using System.Linq;
using StudyVideoHelper;

internal static class CoreTests
{
    private static int passed;
    private static int failed;

    private sealed class Fixture
    {
        public UiNode Root;
        public UiNode Study;
        public UiNode Directory;
        public UiNode Rows;
        public UiNode Card;
        public UiNode Player;
        public UiNode Mark;
        public UiNode Current;
    }

    private static UiNode N(string role, string name, params UiNode[] children)
    {
        UiNode node = new UiNode { Role = role, Name = name };
        node.Children.AddRange(children);
        return node;
    }

    private static UiNode Row(string number, string title, bool pending)
    {
        UiNode row = N("ListItem", "", N("Text", number), N("Text", title));
        if (pending) row.Children.Add(N("Text", "1"));
        return row;
    }

    private static Fixture Course()
    {
        Fixture f = new Fixture();
        f.Current = N("ListItem", "劳动世界的新“劳动者”");
        f.Mark = N("ListItem", "任务点未完成", N("Text", "完成条件"));
        f.Player = N("Group", "视频播放器",
            N("Group", ""),
            N("Button", "暂停", N("Text", "暂停")),
            N("Text", "当前时间"), N("Group", "", N("Text", "4:10")),
            N("Text", "/"), N("Text", "时长"), N("Group", "", N("Text", "9:15")),
            N("Button", "静音"), N("MenuItem", "设置"));
        f.Player.Id = "video";
        UiNode videoDocument = N("Document", "", f.Player);
        videoDocument.Value = "https://mooc1.chaoxing.com/ananas/modules/video/index.html";
        UiNode cardDocument = N("Document", "视频", f.Mark, videoDocument);
        cardDocument.Value = "https://mooc1.chaoxing.com/mooc-ans/knowledge/cards?knowledgeid=1221677477";
        f.Card = N("Pane", "", cardDocument);
        f.Card.Id = "iframe";
        f.Rows = N("List", "",
            Row("10.3", "组织新特征", false),
            Row("10.4", "劳动世界的新“劳动者”", true),
            Row("10.5", "人类劳动新特征", true),
            Row("10.9", "章节测验", true),
            Row("11.1", "就业创业与劳动教育的内涵", true),
            Row("11.4", "章节测验", true));
        UiNode outerRow = N("ListItem", "", N("Text", "10"), N("Text", "劳动的未来"), f.Rows);
        f.Directory = N("Group", "课程目录", N("Edit", "搜索"), N("List", "", outerRow));
        f.Directory.Id = "content1";
        f.Study = N("Document", "学生学习页面", f.Current, f.Card, N("Button", "下一节"), f.Directory);
        f.Study.Value = "https://mooc1.chaoxing.com/mycourse/studentstudy?chapterId=1221677477";
        f.Root = N("Pane", "学习通", N("Document", "学习通", f.Study), N("Document", "学习通首页"));
        return f;
    }

    private static CourseSnapshot Parse(Fixture fixture)
    {
        CourseSnapshot snapshot = SnapshotParser.Parse(fixture.Root);
        Equal("", snapshot.Error, "fixture must be a recognizable study page");
        return snapshot;
    }

    private static void SetTime(Fixture f, string current, string duration)
    {
        List<UiNode> texts = f.Player.Walk().Where(n => n.Role == "Text").ToList();
        texts.First(n => n.Name == "4:10").Name = current;
        texts.First(n => n.Name == "9:15").Name = duration;
    }

    private static void SetPlayerTexts(Fixture f, params string[] texts)
    {
        f.Player.Children.Clear();
        f.Player.Children.Add(N("Button", "暂停"));
        foreach (string text in texts) f.Player.Children.Add(N("Text", text));
    }

    private static void Equal<T>(T expected, T actual, string reason)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(reason + "; expected=" + expected + ", actual=" + actual);
    }

    private static void True(bool actual, string reason)
    {
        if (!actual) throw new Exception(reason);
    }

    private static void Throws(Action action, string reason)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception(reason);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception error)
        {
            failed++;
            Console.WriteLine("FAIL " + name + ": " + error.Message);
        }
    }

    public static int Main()
    {
        Run("realistic study page parses current chapter and playback", delegate {
            CourseSnapshot s = Parse(Course());
            Equal(6, s.Chapters.Count, "outer chapter row must not become a section");
            Equal("劳动世界的新“劳动者”", s.CurrentTitle, "selected section title");
            Equal(250.0, s.Playback.Position, "saved playback position");
            Equal(555.0, s.Playback.Duration, "total video duration");
            Equal(TaskMark.Incomplete, s.Playback.Mark, "unfinished task mark");
            Equal(1, s.Playback.PlayerCount, "one video");
            True(s.Playback.Playing, "pause button means video is playing");
            Equal("", VideoPolicy.PlaybackProblem(s.Playback), "normal player is usable");
        });

        Run("choose first unfinished video and skip completed video", delegate {
            Chapter next = VideoPolicy.NextVideo(Parse(Course()), new HashSet<string>());
            Equal("10.4", next.Number, "10.3 is already complete");
        });

        Run("skip chapter already finished in this session", delegate {
            CourseSnapshot s = Parse(Course());
            HashSet<string> finished = new HashSet<string> { s.Chapters[1].Key };
            Equal("10.5", VideoPolicy.NextVideo(s, finished).Number, "stale pending marker must not repeat a completed section");
        });

        Run("skip unfinished chapter tests with repeated same title", delegate {
            CourseSnapshot s = Parse(Course());
            HashSet<string> finished = new HashSet<string> { s.Chapters[1].Key, s.Chapters[2].Key };
            Equal("11.1", VideoPolicy.NextVideo(s, finished).Number, "10.9 is a test, not a video");
            Equal(2, s.Chapters.Count(c => c.Title == "章节测验"), "do not merge separate tests with same name");
        });

        Run("all videos complete leaves unfinished tests untouched", delegate {
            CourseSnapshot s = Parse(Course());
            HashSet<string> finished = new HashSet<string>(s.Chapters.Where(c => !c.IsQuiz).Select(c => c.Key));
            True(VideoPolicy.NextVideo(s, finished) == null, "must stop without opening tests");
        });

        Run("titles about discussion are still eligible videos", delegate {
            Chapter video = new Chapter { Number = "4.1", Title = "讨论劳动的未来", Pending = true };
            True(!video.IsQuiz, "discussion in a video title does not establish quiz type");
        });

        Run("only explicit pending markers select a chapter", delegate {
            Fixture f = Course();
            f.Rows.Children[1].Children.RemoveAt(2);
            f.Rows.Children[2].Children[2].Name = "✓";
            CourseSnapshot s = Parse(f);
            Equal("11.1", VideoPolicy.NextVideo(s, new HashSet<string>()).Number, "unmarked and green-check rows are not selected");
        });

        Run("written unfinished marker is supported", delegate {
            Fixture f = Course();
            f.Rows.Children[1].Children[2].Name = "未完成";
            Equal("10.4", VideoPolicy.NextVideo(Parse(f), new HashSet<string>()).Number, "text pending marker");
        });

        Run("positive multi-task marker is pending and never becomes the title", delegate {
            Fixture f = Course();
            UiNode row = f.Rows.Children[1];
            row.Children.RemoveAt(2);
            row.Children.Insert(1, N("Text", "2"));
            CourseSnapshot s = Parse(f);
            Chapter chapter = s.Chapters.First(c => c.Number == "10.4");
            True(chapter.Pending, "two unfinished tasks still make the section pending");
            Equal("劳动世界的新“劳动者”", chapter.Title, "numeric marker preceding the real title must be ignored");
            Equal("10.4", VideoPolicy.NextVideo(s, new HashSet<string>()).Number, "positive marker remains eligible for page inspection");
        });

        Run("separate title action targets native text instead of blank row or task count", delegate {
            Fixture f = Course();
            UiNode row = f.Rows.Children[1], title = row.Children[1];
            object nativeTitle = new object(); title.Native = nativeTitle;
            Chapter chapter = Parse(f).Chapters.First(c => c.Number == "10.4");
            True(object.ReferenceEquals(chapter.Node, title), "action must use the title leaf, not a ListItem advertising a no-op Invoke");
            True(chapter.Node.Role == "Text" && object.ReferenceEquals(chapter.Node.Native, nativeTitle), "native title element must be preserved");
            True(chapter.Node.Name == chapter.Title && chapter.Pending, "the task count is metadata, not an action target");
        });

        Run("combined title action ignores same-named Custom wrappers", delegate {
            Fixture f = Course();
            UiNode row = f.Rows.Children[1];
            string key = "10.4 劳动世界的新“劳动者”";
            UiNode title = N("Text", key);
            row.Children.Clear();
            row.Children.Add(N("Custom", key, N("Custom", key, title)));
            row.Children.Add(N("Text", "2"));
            Chapter chapter = Parse(f).Chapters.First(c => c.Number == "10.4");
            True(object.ReferenceEquals(chapter.Node, title), "only the combined title Text is actionable");
            Equal(key, chapter.Node.Name, "count and wrappers cannot replace the title");
            True(chapter.Pending, "pending count still comes from the row");
        });

        Run("unnumbered intro action uses its unique title text", delegate {
            Fixture f = Course();
            UiNode title = N("Text", "导论");
            f.Rows.Children.Add(N("ListItem", "", N("Custom", "导论", title), N("Text", "2")));
            Chapter intro = Parse(f).Chapters.First(c => c.Title == "导论");
            Equal("导论", intro.Key, "unnumbered metadata stays intact");
            True(object.ReferenceEquals(intro.Node, title) && intro.Node.Role == "Text", "intro action is the title, not the count or blank row");
        });

        Run("independent matching title texts reject the directory action", delegate {
            foreach (string duplicate in new string[] { "劳动世界的新“劳动者”", "10.4 劳动世界的新“劳动者”" })
            {
                Fixture f = Course();
                f.Rows.Children[1].Children.Add(N("Custom", duplicate, N("Text", duplicate)));
                CourseSnapshot snapshot = SnapshotParser.Parse(f.Root);
                True(snapshot.Error.Contains("唯一的标题文字动作目标"), "same-title and key/title peers must not choose an arbitrary target");
                True(snapshot.Chapters.First(c => c.Number == "10.4").Node == null, "ambiguous text cannot fall back to the row or a count");
            }
        });

        Run("nested repeated title text chooses only the innermost match", delegate {
            Fixture f = Course();
            UiNode outer = f.Rows.Children[1].Children[1];
            UiNode inner = N("Text", outer.Name); outer.Children.Add(inner);
            Chapter chapter = Parse(f).Chapters.First(c => c.Number == "10.4");
            True(object.ReferenceEquals(chapter.Node, inner), "nested provider name repetition is one leaf action, not two independent targets");
        });

        Run("zero unfinished-task marker is not pending", delegate {
            Fixture f = Course();
            f.Rows.Children[1].Children[2].Name = "0";
            CourseSnapshot s = Parse(f);
            True(!s.Chapters.First(c => c.Number == "10.4").Pending, "zero does not establish an unfinished task");
            Equal("10.5", VideoPolicy.NextVideo(s, new HashSet<string>()).Number, "skip zero-marker section");
        });

        Run("pending marker two does not permit playing a two-task page", delegate {
            Fixture f = Course();
            f.Rows.Children[1].Children[2].Name = "2";
            f.Card.Children[0].Children.Add(N("ListItem", "任务点未完成"));
            CourseSnapshot s = Parse(f);
            True(s.Chapters.First(c => c.Number == "10.4").Pending, "directory recognizes the positive task count");
            Equal(2, s.Playback.TaskCount, "independent task points must not be merged even with identical states");
            Equal(TaskMark.Unknown, s.Playback.Mark, "two task points have no single completion state");
            True(VideoPolicy.PlaybackProblem(s.Playback).Length > 0, "multi-task page must safely stop after inspection");
        });

        Run("nested single-section outer row is not duplicated", delegate {
            Fixture f = Course();
            UiNode row = Row("12.1", "新增视频", true);
            f.Rows.Children.Add(N("ListItem", "", N("Text", "12"), N("Text", "新增章"), N("List", "", row)));
            CourseSnapshot s = Parse(f);
            Equal(1, s.Chapters.Count(c => c.Number == "12.1"), "retain inner section once");
            Equal("新增视频", s.Chapters.First(c => c.Number == "12.1").Title, "do not use outer chapter title as video title");
        });

        Run("duplicate copies of same directory row are deduplicated", delegate {
            Fixture f = Course();
            f.Rows.Children.Add(Row("10.4", "劳动世界的新“劳动者”", true));
            Equal(6, Parse(f).Chapters.Count, "same section key must appear once");
        });

        Run("different numbered videos with the same title fail closed", delegate {
            Fixture f = Course();
            f.Rows.Children[2].Children[1].Name = "劳动世界的新“劳动者”";
            CourseSnapshot snapshot = SnapshotParser.Parse(f.Root);
            True(snapshot.Error.Contains("同名视频"), "same title in distinct numbered sections cannot safely identify task completion");
        });

        Run("nested duplicate document providers select innermost study page", delegate {
            Fixture f = Course();
            UiNode outer = N("Document", "学生学习页面", f.Study);
            f.Root.Children.Clear();
            f.Root.Children.Add(outer);
            Equal("劳动世界的新“劳动者”", Parse(f).CurrentTitle, "duplicated document wrapper is harmless");
        });

        Run("two separate study pages are rejected as ambiguous", delegate {
            Fixture f = Course();
            f.Root.Children.Add(Course().Study);
            True(SnapshotParser.Parse(f.Root).Error.Length > 0, "do not choose arbitrary course tab");
        });

        Run("non-course home page has an actionable error", delegate {
            True(SnapshotParser.Parse(N("Pane", "学习通", N("Document", "首页"))).Error.Length > 0, "home page must not be automated");
        });

        Run("missing directory has an actionable error", delegate {
            Fixture f = Course();
            f.Study.Children.Remove(f.Directory);
            True(SnapshotParser.Parse(f.Root).Error.Length > 0, "cannot enumerate pending tasks");
        });

        Run("ambiguous directories are rejected", delegate {
            Fixture f = Course();
            UiNode second = N("Group", "另一目录");
            second.Id = "content1";
            f.Study.Children.Add(second);
            True(SnapshotParser.Parse(f.Root).Error.Length > 0, "do not merge two different course directories");
        });

        Run("empty directory is not success", delegate {
            Fixture f = Course();
            f.Rows.Children.Clear();
            True(SnapshotParser.Parse(f.Root).Error.Length > 0, "empty accessibility tree is not all-videos-complete");
        });

        Run("explicit complete task point is read", delegate {
            Fixture f = Course();
            f.Mark.Name = "任务点已完成";
            Equal(TaskMark.Complete, Parse(f).Playback.Mark, "server-reported task point is complete");
        });

        Run("nested repeated task accessible name is counted once", delegate {
            Fixture f = Course();
            f.Mark.Children.Add(N("Text", "任务点未完成"));
            Playback p = Parse(f).Playback;
            Equal(1, p.TaskCount, "provider repeats the same mark on inner text");
            Equal(TaskMark.Incomplete, p.Mark, "repeated state remains incomplete");
        });

        Run("contradictory nested task states fail closed", delegate {
            Fixture f = Course();
            f.Mark.Name = "任务点已完成";
            f.Mark.Children.Add(N("Text", "任务点未完成"));
            True(VideoPolicy.PlaybackProblem(Parse(f).Playback).Length > 0, "conflicting complete/incomplete must not be treated as a usable single state");
        });

        Run("two task points fail closed", delegate {
            Fixture f = Course();
            f.Card.Children[0].Children.Add(N("ListItem", "任务点已完成"));
            Playback p = Parse(f).Playback;
            Equal(2, p.TaskCount, "multiple task points are retained");
            Equal(TaskMark.Unknown, p.Mark, "cannot ascribe a single task state");
            True(VideoPolicy.PlaybackProblem(p).Length > 0, "unsupported multi-task section stops");
        });

        Run("unknown task point never becomes complete", delegate {
            Fixture f = Course();
            f.Mark.Name = "状态加载中";
            Playback p = Parse(f).Playback;
            Equal(TaskMark.Unknown, p.Mark, "unknown is not completion");
            True(VideoPolicy.PlaybackProblem(p).Length > 0, "unknown task point stops");
        });

        Run("task markers outside active iframe are ignored", delegate {
            Fixture f = Course();
            f.Mark.Name = "加载中";
            f.Study.Children.Add(N("ListItem", "任务点已完成"));
            Equal(TaskMark.Unknown, Parse(f).Playback.Mark, "stale outer-page completion cannot complete active video");
        });

        Run("two video players fail closed", delegate {
            Fixture f = Course();
            UiNode secondPlayer = N("Group", "视频播放器");
            secondPlayer.Id = "video";
            f.Card.Children[0].Children.Add(secondPlayer);
            Playback p = Parse(f).Playback;
            Equal(2, p.PlayerCount, "two independent players");
            True(VideoPolicy.PlaybackProblem(p).Length > 0, "do not pick an arbitrary player");
        });

        Run("missing player fails closed", delegate {
            Fixture f = Course();
            f.Player.Name = "其他区域";
            f.Player.Id = "other";
            True(VideoPolicy.PlaybackProblem(Parse(f).Playback).Length > 0, "non-video task must not be played");
        });

        Run("paused player exposes a play action", delegate {
            Fixture f = Course();
            UiNode button = f.Player.Children.First(n => n.Role == "Button" && n.Name == "暂停");
            button.Name = "播放";
            Playback p = Parse(f).Playback;
            True(!p.Playing, "play button means paused");
            True(p.PlayButton != null && p.PlayButton.Name == "播放", "normal play control is available");
        });

        Run("read labelled clocks despite a subtitle containing another clock", delegate {
            Fixture f = Course();
            f.Player.Children.Insert(0, N("Group", "字幕", N("Text", "12:34")));
            Playback p = Parse(f).Playback;
            Equal(250.0, p.Position, "use 当前时间 label instead of caption timestamp");
            Equal(555.0, p.Duration, "use 时长 label");
        });

        Run("missing current value cannot borrow the following duration", delegate {
            Fixture f = Course();
            SetPlayerTexts(f, "当前时间", "时长", "15:34");
            Playback p = Parse(f).Playback;
            Equal(-1.0, p.Position, "duration belongs to its own anchor when current text is temporarily missing");
            Equal(934.0, p.Duration, "the valid duration remains readable");
            True(!p.HasValidTime, "missing position must trigger a reread rather than appear to be the video end");
        });

        Run("a duration label alone cannot enable two-clock fallback", delegate {
            Fixture f = Course();
            SetPlayerTexts(f, "4:10", "时长", "9:15");
            Playback p = Parse(f).Playback;
            Equal(-1.0, p.Position, "an unlabelled clock cannot supply position when a duration anchor exists");
            Equal(555.0, p.Duration, "labelled duration is retained");
        });

        Run("a current label alone cannot enable two-clock fallback", delegate {
            Fixture f = Course();
            SetPlayerTexts(f, "当前时间", "4:10", "9:15");
            Playback p = Parse(f).Playback;
            Equal(250.0, p.Position, "labelled current value is retained");
            Equal(-1.0, p.Duration, "a missing duration anchor cannot be guessed from the second clock");
            True(!p.HasValidTime, "partial labels are an invalid time sample");
        });

        Run("subtitle before missing labelled current value cannot become position", delegate {
            Fixture f = Course();
            SetPlayerTexts(f, "0:12", "当前时间", "时长", "9:15");
            Playback p = Parse(f).Playback;
            Equal(-1.0, p.Position, "two clocks including a subtitle must not bypass recognized labels");
            Equal(555.0, p.Duration, "subtitle does not contaminate labelled duration");
        });

        Run("missing duration value cannot borrow a later current value", delegate {
            Fixture f = Course();
            SetPlayerTexts(f, "时长", "当前时间", "4:10");
            Playback p = Parse(f).Playback;
            Equal(-1.0, p.Duration, "clock lookup stops at either kind of time anchor");
            Equal(250.0, p.Position, "later current anchor retains its own value");
        });

        Run("a repeated time anchor cannot hide the first missing value", delegate {
            Fixture f = Course();
            SetPlayerTexts(f, "当前时间", "当前时间", "4:10", "时长", "9:15");
            Playback p = Parse(f).Playback;
            Equal(-1.0, p.Position, "a new anchor ends the first anchor's search even when its name repeats");
            Equal(555.0, p.Duration, "independent duration stays readable");
        });

        Run("unlabelled player with exactly two times supports fallback", delegate {
            Fixture f = Course();
            foreach (UiNode n in f.Player.Walk())
                if (n.Name == "当前时间" || n.Name == "时长") n.Name = "";
            Playback p = Parse(f).Playback;
            Equal(250.0, p.Position, "fallback current time");
            Equal(555.0, p.Duration, "fallback duration");
        });

        Run("ambiguous unlabelled clocks fail closed", delegate {
            Fixture f = Course();
            foreach (UiNode n in f.Player.Walk())
                if (n.Name == "当前时间" || n.Name == "时长") n.Name = "";
            f.Player.Children.Insert(0, N("Text", "12:34"));
            True(VideoPolicy.PlaybackProblem(Parse(f).Playback).Length > 0, "third time must not be guessed as playback time");
        });

        Run("hour format is accepted", delegate {
            Equal(3723.0, SnapshotParser.ParseClock("1:02:03"), "h:mm:ss clock");
            Fixture f = Course();
            SetTime(f, "1:02:03", "2:00:00");
            Equal(7200.0, Parse(f).Playback.Duration, "two-hour video duration");
        });

        Run("clock supports whitespace and long minutes", delegate {
            Equal(555.0, SnapshotParser.ParseClock(" 9:15 "), "trim whitespace");
            Equal(7500.0, SnapshotParser.ParseClock("125:00"), "long mm:ss clock");
            Equal(0.0, SnapshotParser.ParseClock("0:00"), "start of playback");
        });

        Run("invalid clocks are rejected", delegate {
            string[] invalid = { null, "", "9", "9:60", "1:60:00", "1:00:60", "-1:00", "1:2:3:4", "NaN:00", "9.5:00", "0:ab" };
            foreach (string clock in invalid) Equal(-1.0, SnapshotParser.ParseClock(clock), "reject " + (clock ?? "<null>"));
        });

        Run("zero total duration is not a loaded video", delegate {
            Fixture f = Course();
            SetTime(f, "0:00", "0:00");
            True(VideoPolicy.PlaybackProblem(Parse(f).Playback).Length > 0, "not-yet-loaded metadata stops action");
        });

        Run("position beyond duration is invalid", delegate {
            Fixture f = Course();
            SetTime(f, "9:30", "9:15");
            True(VideoPolicy.PlaybackProblem(Parse(f).Playback).Length > 0, "mismatched stale time fields are rejected");
        });

        Run("first completion check is twenty seconds before end", delegate {
            Playback p = Parse(Course()).Playback;
            Equal(285.0, VideoPolicy.UntilCheck(p), "9:15 minus 4:10 minus 20 seconds");
            Equal(20, VideoPolicy.EarlyCheckSeconds, "user-selected lead time");
        });

        Run("last twenty seconds trigger immediate check", delegate {
            Fixture f = Course();
            SetTime(f, "9:00", "9:15");
            Equal(0.0, VideoPolicy.UntilCheck(Parse(f).Playback), "do not wait a negative duration");
        });

        Run("ended video triggers immediate check", delegate {
            Fixture f = Course();
            SetTime(f, "9:15", "9:15");
            Equal(0.0, VideoPolicy.UntilCheck(Parse(f).Playback), "ended video waits for explicit completion mark");
        });

        Run("invalid clocks cannot schedule a timer", delegate {
            Throws(delegate { VideoPolicy.UntilCheck(new Playback()); }, "unknown duration must not silently schedule a timer");
        });

        Run("course identity ignores progress changes", delegate {
            Fixture a = Course();
            Fixture b = Course();
            b.Rows.Children[1].Children.RemoveAt(2);
            Equal(Parse(a).Fingerprint, Parse(b).Fingerprint, "completion updates do not look like course navigation");
        });

        Run("course identity changes if section list changes", delegate {
            Fixture a = Course();
            Fixture b = Course();
            b.Rows.Children[0].Children[1].Name = "另一门课程的内容";
            True(Parse(a).Fingerprint != Parse(b).Fingerprint, "changed course cannot continue silently");
        });

        failed += SessionTests.Run();
        failed += RealCourseTests.Run();
        failed += VerificationTests.Run();
        failed += BoundedReadTests.Run();
        Console.WriteLine("CORE RESULT " + passed + " passed; total failures including session tests: " + failed);
        return failed == 0 ? 0 : 1;
    }
}
