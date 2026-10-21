using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace StudyVideoHelper
{
    public sealed class UiNode
    {
        public string Name = "";
        public string Id = "";
        public string Role = "";
        public string Value = "";
        public bool Offscreen;
        public object Native;
        public readonly List<UiNode> Children = new List<UiNode>();

        public IEnumerable<UiNode> Walk()
        {
            yield return this;
            foreach (UiNode child in Children)
                foreach (UiNode node in child.Walk()) yield return node;
        }
    }

    public sealed class Chapter
    {
        public string Number = "";
        public string Title = "";
        public bool Pending;
        public UiNode Node;
        public string Key { get { return string.IsNullOrEmpty(Number) ? Title : Number + " " + Title; } }
        public bool IsQuiz { get { return Regex.IsMatch(Title, "章节.*(测验|测试)|测验|考试|作业|问卷"); } }
    }

    public enum TaskMark { Unknown, Incomplete, Complete }

    public sealed class Playback
    {
        public TaskMark Mark;
        public int TaskCount;
        public int PlayerCount;
        public double Position = -1;
        public double Duration = -1;
        public bool Playing;
        public UiNode PlayButton;
        public UiNode CardNode;
        public string CardIdentity = "";
        public bool HasValidTime { get { return Duration > 0 && Position >= 0 && Position <= Duration + 1; } }
        public double Remaining { get { return Math.Max(0, Duration - Position); } }
    }

    public sealed class CourseSnapshot
    {
        public readonly List<Chapter> Chapters = new List<Chapter>();
        public string CurrentTitle = "";
        public Playback Playback = new Playback();
        public UiNode StudyDocument;
        public UiNode Directory;
        public UiNode CurrentTitleNode;
        public string Error = "";
        public string Fingerprint { get { return string.Join("\n", Chapters.Select(c => c.Key).ToArray()); } }
    }

    public static class SnapshotParser
    {
        public static double ParseClock(string text)
        {
            string[] parts = (text ?? "").Trim().Split(':');
            if (parts.Length != 2 && parts.Length != 3) return -1;
            double total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                int number;
                if (!Regex.IsMatch(parts[i], @"^\d{1,3}$") ||
                    !int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out number) ||
                    (i > 0 && number >= 60)) return -1;
                total = total * 60 + number;
            }
            return total;
        }

        public static CourseSnapshot Parse(UiNode root)
        {
            CourseSnapshot result = new CourseSnapshot();
            List<UiNode> documents = root.Walk().Where(n => n.Role == "Document" &&
                (n.Name == "学生学习页面" || n.Value.Contains("/studentstudy"))).ToList();
            if (documents.Count == 0)
                documents = root.Walk().Where(n => n.Role == "Document" &&
                    n.Walk().Any(x => x.Role == "List" && ParseRows(x).Count > 0) &&
                    n.Walk().Any(IsTaskMark) && n.Walk().Any(IsPlayer)).ToList();
            // An outer document can contain the actual study document. Use the innermost match.
            documents = documents.Where(n => !n.Children.Any(c => c.Walk().Any(x => documents.Contains(x)))).ToList();
            if (documents.Count != 1)
            {
                result.Error = "请在学习通中打开目标课程的章节学习页面；未找到唯一的“学生学习页面”。";
                return result;
            }
            result.StudyDocument = documents[0];
            List<UiNode> directories = result.StudyDocument.Walk().Where(n => n.Id == "content1").ToList();
            if (directories.Count == 0)
            {
                List<UiNode> candidates = result.StudyDocument.Walk()
                    .Where(n => n.Role == "List" || n.Role == "Tree").ToList();
                int count = candidates.Count == 0 ? 0 : candidates.Max(n => ParseRows(n).Count);
                if (count > 0)
                {
                    candidates = candidates.Where(n => ParseRows(n).Count == count).ToList();
                    // Several nested lists may contain the same rows; retain their outer container.
                    directories = candidates.Where(n => !candidates.Any(other => other != n && other.Walk().Contains(n))).ToList();
                }
            }
            if (directories.Count != 1)
            {
                result.Error = "没有找到唯一的课程目录（content1或可识别的章节列表）。请先打开课程并展开目录。";
                return result;
            }
            UiNode directory = directories[0];
            result.Directory = directory;
            result.Chapters.AddRange(ParseRows(directory));
            if (result.Chapters.Count == 0)
            {
                result.Error = "课程目录没有暴露可识别的小节编号和标题。请使用“诊断”保存结果。";
                return result;
            }
            Chapter missingAction = result.Chapters.FirstOrDefault(c => c.Node == null);
            if (missingAction != null)
            {
                result.Error = "目录小节“" + missingAction.Key + "”没有唯一的标题文字动作目标，已停止。请使用诊断检查目录结构。";
                return result;
            }
            if (result.Chapters.Where(c => !c.IsQuiz).GroupBy(c => c.Title).Any(g => g.Count() > 1))
            {
                result.Error = "目录中存在同名视频小节，第一版无法可靠区分其完成状态，已停止。";
                return result;
            }
            HashSet<UiNode> directoryNodes = new HashSet<UiNode>(directory.Walk());
            List<UiNode> titleNodes = result.StudyDocument.Walk().Where(n => !directoryNodes.Contains(n) &&
                n.Role != "Text" && result.Chapters.Any(c => MatchesTitle(n.Name, c))).ToList();
            List<UiNode> preferredTitles = titleNodes.Where(n => n.Role == "ListItem" || n.Role == "TabItem").ToList();
            if (preferredTitles.Count > 0) titleNodes = preferredTitles;
            List<string> titles = titleNodes.Select(n => result.Chapters.First(c => MatchesTitle(n.Name, c)).Title).Distinct().ToList();
            if (titles.Count == 1)
            {
                result.CurrentTitle = titles[0];
                result.CurrentTitleNode = titleNodes.First(n => MatchesTitle(n.Name, result.Chapters.First(c => c.Title == titles[0])));
            }
            // "章节测验" occurs in multiple chapters. We never navigate to it, so ambiguity is safe.
            result.Playback = ReadPlayback(result.StudyDocument);
            return result;
        }

        private static bool MatchesTitle(string name, Chapter chapter)
        {
            string normalized = Regex.Replace((name ?? "").Trim(), @"\s+", " ");
            return normalized == chapter.Title || normalized == chapter.Key;
        }

        private static bool IsRow(UiNode node) { return node.Role == "ListItem" || node.Role == "TreeItem"; }
        private static bool IsTaskMark(UiNode node)
        { return node.Name.Trim() == "任务点已完成" || node.Name.Trim() == "任务点未完成"; }
        private static bool IsPlayer(UiNode node) { return node.Id == "video" || node.Name == "视频播放器"; }

        private static UiNode ChapterActionNode(UiNode row, Chapter chapter)
        {
            // A blank ListItem can advertise Invoke while doing nothing. Target the
            // actual title text, never the count, row, or a same-named Custom wrapper.
            List<UiNode> titles = row.Walk().Where(n => n.Role == "Text" && MatchesTitle(n.Name, chapter)).ToList();
            titles = titles.Where(n => !n.Children.Any(c => c.Walk().Any(x => titles.Contains(x)))).ToList();
            return titles.Count == 1 ? titles[0] : null;
        }

        private static List<Chapter> ParseRows(UiNode directory)
        {
            List<Chapter> chapters = new List<Chapter>();
            foreach (UiNode row in directory.Walk().Where(IsRow))
            {
                if (row.Children.Any(c => c.Walk().Any(IsRow))) continue;
                List<string> texts = row.Walk().Where(n => n.Role == "Text" && !string.IsNullOrWhiteSpace(n.Name))
                    .Select(n => Regex.Replace(n.Name.Trim(), @"\s+", " ")).Distinct().ToList();
                List<string> plainNumbers = texts.Where(t => Regex.IsMatch(t, @"^\d+\.\d+(?:\.\d+)*$")).ToList();
                List<Match> combined = texts.Select(t => Regex.Match(t, @"^(\d+\.\d+(?:\.\d+)*)\s+(.+)$"))
                    .Where(m => m.Success).ToList();
                List<string> sectionNumbers = plainNumbers.Concat(combined.Select(m => m.Groups[1].Value)).Distinct().ToList();
                if (sectionNumbers.Count > 1) continue;
                string number = sectionNumbers.FirstOrDefault() ?? "";
                string title = combined.Count == 1 ? combined[0].Groups[2].Value : texts.Where(t =>
                    !Regex.IsMatch(t, @"^\d+(?:\.\d+)*$") && t != "已完成" && t != "未完成" && t != "✓" && t != "√")
                    .FirstOrDefault();
                if (string.IsNullOrEmpty(title)) continue;
                if (number == "")
                {
                    // An unnumbered leaf such as 导论 has a trailing task count. A parent chapter
                    // heading beginning with an integer is not a task row.
                    int titleIndex = texts.IndexOf(title);
                    if (titleIndex < 0 || !texts.Skip(titleIndex + 1).Any(t => Regex.IsMatch(t, @"^\d+$") || t == "未完成" || t == "已完成")) continue;
                }
                Chapter chapter = new Chapter { Number = number, Title = title,
                    Pending = texts.Any(t => Regex.IsMatch(t, @"^[1-9]\d*$") || t == "未完成") };
                chapter.Node = ChapterActionNode(row, chapter);
                if (!chapters.Any(c => c.Key == chapter.Key)) chapters.Add(chapter);
            }
            return chapters;
        }

        public static Playback ReadPlayback(UiNode studyDocument)
        {
            Playback result = new Playback();
            List<UiNode> cards = studyDocument.Walk().Where(n => n.Id == "iframe").ToList();
            if (cards.Count == 0)
            {
                List<UiNode> candidates = studyDocument.Walk().Where(n => n != studyDocument && n.Role == "Document" &&
                    n.Walk().Any(IsTaskMark) && n.Walk().Any(IsPlayer)).ToList();
                cards = candidates.Where(n => !n.Children.Any(c => c.Walk().Any(x => candidates.Contains(x)))).ToList();
            }
            if (cards.Count != 1) return result;
            UiNode card = cards[0];
            result.CardNode = card;
            List<UiNode> marks = card.Walk().Where(IsTaskMark).ToList();
            // Providers sometimes repeat the accessible name on a child Text node.
            marks = marks.Where(n => !n.Children.Any(c => c.Walk().Any(x => marks.Contains(x) && x.Name.Trim() == n.Name.Trim()))).ToList();
            result.TaskCount = marks.Count;
            if (marks.Count == 1) result.Mark = marks[0].Name.Trim() == "任务点已完成" ? TaskMark.Complete : TaskMark.Incomplete;
            List<UiNode> players = card.Walk().Where(IsPlayer).ToList();
            players = players.Where(n => !n.Children.Any(c => c.Walk().Any(x => players.Contains(x)))).ToList();
            result.PlayerCount = players.Count;
            result.CardIdentity = card.Walk().Where(n => n.Role == "Document" && !string.IsNullOrEmpty(n.Value))
                .Select(n => n.Value).FirstOrDefault() ?? "";
            if (players.Count != 1) return result;
            UiNode player = players[0];
            List<UiNode> nodes = player.Walk().ToList();
            result.Playing = nodes.Any(n => n.Role == "Button" && (n.Name.Trim() == "暂停" || n.Name.Trim() == "暂停视频" ||
                n.Name.Trim() == "Pause" || n.Name.Trim() == "Pause Video"));
            result.PlayButton = nodes.FirstOrDefault(n => n.Role == "Button" &&
                (n.Name.Trim() == "播放" || n.Name.Trim() == "播放视频" || n.Name.Trim() == "Play" ||
                 n.Name.Trim() == "Play Video" || n.Name.Trim() == "重播"));
            List<string> texts = nodes.Where(n => n.Role == "Text").Select(n => n.Name.Trim()).ToList();
            result.Position = ClockAfter(texts, "当前时间");
            result.Duration = ClockAfter(texts, "时长");
            // Once either clock is labelled, never infer the other from unrelated text.
            // A temporarily missing current value must not become the duration or a caption.
            if (!texts.Contains("当前时间") && !texts.Contains("时长"))
            {
                List<double> clocks = texts.Select(ParseClock).Where(t => t >= 0).ToList();
                if (clocks.Count == 2) { result.Position = clocks[0]; result.Duration = clocks[1]; }
            }
            return result;
        }

        private static double ClockAfter(List<string> texts, string anchor)
        {
            int index = texts.IndexOf(anchor);
            if (index < 0) return -1;
            for (int i = index + 1; i < Math.Min(index + 4, texts.Count); i++)
            {
                if (texts[i] == "当前时间" || texts[i] == "时长") break;
                double value = ParseClock(texts[i]);
                if (value >= 0) return value;
            }
            return -1;
        }
    }

    public static class VideoPolicy
    {
        public const int EarlyCheckSeconds = 20;
        public static Chapter NextVideo(CourseSnapshot snapshot, ISet<string> finished)
        {
            return snapshot.Chapters.FirstOrDefault(c => c.Pending && !c.IsQuiz && !finished.Contains(c.Key));
        }
        public static double UntilCheck(Playback playback)
        {
            if (!playback.HasValidTime) throw new InvalidOperationException("无法读取有效的视频时间。");
            return Math.Max(0, playback.Remaining - EarlyCheckSeconds);
        }
        public static string PlaybackProblem(Playback playback)
        {
            if (playback.TaskCount != 1) return "当前页面没有唯一的任务点；第一版仅支持每节一个任务点的视频页面。";
            if (playback.PlayerCount != 1) return "当前页面没有唯一的视频播放器，已停止。";
            if (playback.Mark == TaskMark.Unknown) return "无法确认当前任务点状态。";
            if (!playback.HasValidTime) return "无法读取视频的当前时间和总时长。";
            return "";
        }
        public static string Clock(double seconds)
        {
            if (seconds < 0) return "--:--";
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return (int)t.TotalMinutes + ":" + t.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }
}
