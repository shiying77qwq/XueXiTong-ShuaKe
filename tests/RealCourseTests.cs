using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using StudyVideoHelper;

internal static class RealCourseTests
{
    // Captured from the actual 国家安全教育 study page on 2026-10-20. Only the
    // selected page's role/name hierarchy is retained; no URLs or account data.
    private const string CaptureText = @"Document 学生学习页面 []
 Custom  []
  Custom  []
  Custom  []
   Custom  []
    Custom  []
     Hyperlink  返回课程 []
      Custom  []
       Text  []
      Text 返回课程 []
     Custom 章节详情 []
      Text 章节详情 []
    Custom  []
     ListItem 导论 []
      Custom 导论 []
       Text 导论 []
     Custom  []
      List  []
       ListItem 视频 []
       ListItem 章节测验 []
     Custom  []
      Document  []
       Document 视频 []
        Custom  []
         ListItem 正文 []
         Custom  []
         Custom  []
          Custom  []
           Custom  []
            ListItem 任务点未完成 []
             Custom  []
             Custom  []
              Text 完成条件 []
              Text 观看时长需 ≥ 总时长的 90% (未完成任务点前, 当前视频不可拖拽、观看时不可离开或将页面最小化) []
            Document  []
             Document  []
              Custom  []
               Custom  []
                Custom  []
                 Custom  []
                  Pane 视频播放器 []
                   Custom  []
                   Custom  []
                   Custom  []
                   Custom  []
                    Custom  []
                   Button 播放视频 []
                    Custom  []
                     Text 播放视频 []
                 Custom  []
                  Custom  []
                  Custom  []
                  Custom  []
                  Custom  []
                Custom  []
            Custom  []
             Custom  []
             Custom  []
          Custom  []
           Custom 
 []
          Custom  []
           Custom 
 []
     Custom  []
      Button 下一节 []
     Custom  []
      Custom  []
       Custom  []
      Custom  []
      Custom  []
       Custom  []
    Custom 正文结束 []
  Custom  []
   Custom  []
    Custom  []
   Custom  []
   Custom  []
    List  []
     ListItem  []
      Text 目录 []
     ListItem  []
      Text 讨论 []
     ListItem  []
      Text 笔记 []
     ListItem  []
      Text 标注 []
   Custom  []
    Custom  []
     Custom  []
      Custom  []
       Edit 搜索 []
       Button  []
     Custom  []
      List  []
       ListItem  []
        Custom  []
         Custom 导论 []
          Custom  []
          Text  导论 []
        List  []
         ListItem  []
          Custom  []
           Custom 导论 []
            Text 导论 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第一章 完整准确领会总体国家安全观 []
          Custom  []
          Text  第一章 完整准确领会总体国家安全观 []
        List  []
         ListItem  []
          Custom  []
           Custom 1.1 总体国家安全观的创立 []
            Text 1.1 总体国家安全观的创立 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 1.2 总体国家安全观的科学内涵 []
            Text 1.2 总体国家安全观的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 1.3 总体国家安全观的重点领域和基本特征 []
            Text 1.3 总体国家安全观的重点领域和基本特征 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第二章 在党的领导下走好中国特色国家安全道路 []
          Custom  []
          Text  第二章 在党的领导下走好中国特色国家安全道路 []
        List  []
         ListItem  []
          Custom  []
           Custom 2.1 坚持党对国家安全工作的绝对领导 []
            Text 2.1 坚持党对国家安全工作的绝对领导 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 2.2 坚持中国特色国家安全道路 []
            Text 2.2 坚持中国特色国家安全道路 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 2.3 推进国家安全体系和能力现代化 []
            Text 2.3 推进国家安全体系和能力现代化 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第三章 更好统筹发展和安全 []
          Custom  []
          Text  第三章 更好统筹发展和安全 []
        List  []
         ListItem  []
          Custom  []
           Custom 3.1 统筹发展和安全的重大意义 []
            Text 3.1 统筹发展和安全的重大意义 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 3.2 统筹发展和安全的科学内涵 []
            Text 3.2 统筹发展和安全的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 3.3 更好统筹发展和安全的途径和方法 []
            Text 3.3 更好统筹发展和安全的途径和方法 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第四章 坚持以人民安全为宗旨 []
          Custom  []
          Text  第四章 坚持以人民安全为宗旨 []
        List  []
         ListItem  []
          Custom  []
           Custom 4.1 坚持以人民安全为宗旨的科学内涵 []
            Text 4.1 坚持以人民安全为宗旨的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 4.2 新时代人民安全面临的风险挑战 []
            Text 4.2 新时代人民安全面临的风险挑战 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 4.3 新时代维护人民安全的途径和方法 []
            Text 4.3 新时代维护人民安全的途径和方法 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第五章 坚持以政治安全为根本 []
          Custom  []
          Text  第五章 坚持以政治安全为根本 []
        List  []
         ListItem  []
          Custom  []
           Custom 5.1 坚持以政治安全为根本的科学内涵 []
            Text 5.1 坚持以政治安全为根本的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 5.2 新时代政治安全面临的风险挑战 []
            Text 5.2 新时代政治安全面临的风险挑战 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 5.3 新时代维护政治安全的途径和方法 []
            Text 5.3 新时代维护政治安全的途径和方法 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第六章 坚持以经济安全为基础 []
          Custom  []
          Text  第六章 坚持以经济安全为基础 []
        List  []
         ListItem  []
          Custom  []
           Custom 6.1 坚持以经济安全为基础的科学内涵 []
            Text 6.1 坚持以经济安全为基础的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 6.2 新时代经济安全面临的风险挑战 []
            Text 6.2 新时代经济安全面临的风险挑战 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 6.3 新时代维护经济安全的途径和方法 []
            Text 6.3 新时代维护经济安全的途径和方法 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第七章 坚持以军事、科技、文化、社会安全为保障 []
          Custom  []
          Text  第七章 坚持以军事、科技、文化、社会安全为保障 []
        List  []
         ListItem  []
          Custom  []
           Custom 7.1 坚持以军事、科技、文化、社会安全为保障的科学内涵 []
            Text 7.1 坚持以军事、科技、文化、社会安全为保障的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 7.2 新时代军事、科技、文化、社会安全面临的风险挑战 []
            Text 7.2 新时代军事、科技、文化、社会安全面临的风险挑战 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 7.3 新时代维护军事、科技、文化、社会安全的途径和方法 []
            Text 7.3 新时代维护军事、科技、文化、社会安全的途径和方法 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第八章 坚持以促进国际安全为依托 []
          Custom  []
          Text  第八章 坚持以促进国际安全为依托 []
        List  []
         ListItem  []
          Custom  []
           Custom 8.1 坚持以促进国际安全为依托的科学内涵 []
            Text 8.1 坚持以促进国际安全为依托的科学内涵 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 8.2 新时代国际安全面临的风险挑战 []
            Text 8.2 新时代国际安全面临的风险挑战 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 8.3 新时代维护国际安全的途径和方法 []
            Text 8.3 新时代维护国际安全的途径和方法 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第九章 筑牢其他各领域国家安全屏障 []
          Custom  []
          Text  第九章 筑牢其他各领域国家安全屏障 []
        List  []
         ListItem  []
          Custom  []
           Custom 9.1 筑牢国土安全屏障 []
            Text 9.1 筑牢国土安全屏障 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 9.2 筑牢生态安全屏障 []
            Text 9.2 筑牢生态安全屏障 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 9.3 筑牢资源安全屏障 []
            Text 9.3 筑牢资源安全屏障 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 9.4 筑牢国家网络安全屏障 []
            Text 9.4 筑牢国家网络安全屏障 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 9.5 筑牢核安全屏障 []
            Text 9.5 筑牢核安全屏障 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 第十章 争做总体国家安全观坚定践行者 []
          Custom  []
          Text  第十章 争做总体国家安全观坚定践行者 []
        List  []
         ListItem  []
          Custom  []
           Custom 10.1 增强国家安全意识，坚持国家利益至上 []
            Text 10.1 增强国家安全意识，坚持国家利益至上 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 10.2 增强忧患意识，发扬斗争精神 []
            Text 10.2 增强忧患意识，发扬斗争精神 []
           Text 2 []
         ListItem  []
          Custom  []
           Custom 10.3 增强社会责任，形成维护国家安全合力 []
            Text 10.3 增强社会责任，形成维护国家安全合力 []
           Text 2 []
       ListItem  []
        Custom  []
         Custom 阅读 []
          Custom  []
          Text  阅读 []
        List  []
         ListItem  []
          Custom  []
           Custom 阅读 []
            Text 阅读 []
           Text 0 []
   Custom  []
  Custom  []
  Custom  []
  Custom  []
   Image  []
  Custom  []
";
    private static int passed, failed;

    private static UiNode Captured()
    {
        UiNode root = new UiNode { Role = "Pane", Name = "Captured learning window" };
        Stack<KeyValuePair<int, UiNode>> parents = new Stack<KeyValuePair<int, UiNode>>();
        parents.Push(new KeyValuePair<int, UiNode>(-1, root));
        foreach (string line in CaptureText.Replace("\r", "").Split('\n'))
        {
            Match match = Regex.Match(line, @"^( *)([A-Za-z]+) (.*) \[([^\]]*)\]$");
            if (!match.Success) continue;
            int depth = match.Groups[1].Value.Length;
            while (parents.Peek().Key >= depth) parents.Pop();
            UiNode node = new UiNode { Role = match.Groups[2].Value, Name = match.Groups[3].Value, Id = match.Groups[4].Value };
            parents.Peek().Value.Children.Add(node);
            parents.Push(new KeyValuePair<int, UiNode>(depth, node));
        }
        return root;
    }

    private static UiNode Clone(UiNode node)
    {
        UiNode copy = new UiNode { Name = node.Name, Role = node.Role, Id = node.Id, Value = node.Value, Offscreen = node.Offscreen };
        foreach (UiNode child in node.Children) copy.Children.Add(Clone(child));
        return copy;
    }

    private static CourseSnapshot Parse(UiNode root)
    {
        CourseSnapshot result = SnapshotParser.Parse(root);
        Check(result.Error == "", result.Error);
        return result;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Run(string name, Action test)
    {
        try { test(); passed++; Console.WriteLine("PASS real capture: " + name); }
        catch (Exception error) { failed++; Console.WriteLine("FAIL real capture: " + name + ": " + error.Message); }
    }

    public static int Run()
    {
        passed = failed = 0;
        Run("the exact recorded UIA page parses without any AutomationIds", delegate {
            UiNode root = Captured();
            Check(root.Walk().All(n => n.Id == ""), "capture must preserve missing AutomationIds");
            CourseSnapshot page = Parse(root);
            Check(page.Chapters.Count == 34, "expected 32 numbered lessons, intro and reading");
            Check(page.CurrentTitle == "导论", "active intro must be found outside the nested directory");
            Check(page.Playback.TaskCount == 1 && page.Playback.PlayerCount == 1, "one active video task");
        });
        Run("unnumbered intro has a stable title-only key", delegate {
            Chapter intro = Parse(Captured()).Chapters.First();
            Check(intro.Number == "" && intro.Title == "导论" && intro.Key == "导论" && intro.Pending, "intro metadata");
        });
        Run("combined number and title is split without incorporating task count", delegate {
            Chapter first = Parse(Captured()).Chapters.First(c => c.Number == "1.1");
            Check(first.Title == "总体国家安全观的创立" && first.Pending, "combined UIA label");
        });
        Run("recorded directory actions target title Text leaves rather than blank rows or counts", delegate {
            CourseSnapshot page = Parse(Captured());
            Check(page.Chapters.All(c => c.Node != null && c.Node.Role == "Text" &&
                (c.Node.Name.Trim() == c.Title || c.Node.Name.Trim() == c.Key)), "every action must be an actual chapter title text");
            Check(page.Chapters.All(c => c.Node.Native == null), "static capture fixtures must work without fabricated native handles");
            Check(page.Chapters.First().Node.Name == "导论", "unnumbered intro uses its title");
            Check(page.Chapters.First(c => c.Number == "1.1").Node.Name == "1.1 总体国家安全观的创立", "combined title text is the action target");
            Check(page.Chapters.All(c => c.Node.Name != "2" && c.Node.Name != "0"), "task counters are never click targets");
        });
        Run("same-named real Custom wrappers do not replace a native title leaf", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            Chapter first = page.Chapters.First(c => c.Number == "1.1");
            UiNode leaf = first.Node;
            object native = new object(); leaf.Native = native;
            UiNode parent = page.Directory.Walk().First(n => n.Children.Contains(leaf));
            UiNode wrapper = new UiNode { Role = "Custom", Name = first.Key };
            UiNode inner = new UiNode { Role = "Custom", Name = first.Title };
            wrapper.Children.Add(inner); inner.Children.Add(leaf);
            parent.Children[parent.Children.IndexOf(leaf)] = wrapper;
            Chapter reparsed = Parse(root).Chapters.First(c => c.Number == "1.1");
            Check(object.ReferenceEquals(reparsed.Node, leaf) && object.ReferenceEquals(reparsed.Node.Native, native),
                "title identity survives nested same-name wrappers without selecting their Invoke surfaces");
        });
        Run("independent repeated title texts in a recorded blank row reject the action", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            Chapter first = page.Chapters.First(c => c.Number == "1.1");
            UiNode parent = page.Directory.Walk().First(n => n.Children.Contains(first.Node));
            parent.Children.Add(new UiNode { Role = "Text", Name = first.Key });
            CourseSnapshot reparsed = SnapshotParser.Parse(root);
            Check(reparsed.Error.Contains("唯一的标题文字动作目标"), "two independent Text hits must fail clearly");
            Check(reparsed.Chapters.First(c => c.Number == "1.1").Node == null,
                "ambiguous titles cannot fall back to a blank ListItem, Custom wrapper, or count");
        });
        Run("chapter header rows and nested sublists are not duplicate sections", delegate {
            CourseSnapshot page = Parse(Captured());
            Check(page.Chapters.Count(c => c.Number == "9.5") == 1, "leaf section occurs exactly once");
            Check(!page.Chapters.Any(c => c.Title.StartsWith("第一章")), "chapter heading is not a task");
            Check(page.Directory != null && page.Directory.Role == "List", "semantic directory container returned");
        });
        Run("aggregate count two does not mean two active video task points", delegate {
            CourseSnapshot page = Parse(Captured());
            Check(page.Chapters.First().Pending && page.Playback.TaskCount == 1, "directory totals cannot determine card count");
            Check(page.Playback.Mark == TaskMark.Incomplete, "actual current mark remains authoritative");
        });
        Run("reading row with zero tasks is not pending", delegate {
            Chapter reading = Parse(Captured()).Chapters.Last();
            Check(reading.Title == "阅读" && !reading.Pending, "zero marker is not unfinished");
        });
        Run("real big play button is recognized before metadata loads", delegate {
            Playback playback = Parse(Captured()).Playback;
            Check(playback.PlayButton != null && playback.PlayButton.Name == "播放视频", "real button name");
            Check(!playback.HasValidTime && !playback.Playing, "no fabricated initial time or playback state");
            Check(playback.CardNode != null && playback.CardNode.Role == "Document" && playback.CardNode.Name == "视频", "semantic card identity");
        });
        Run("active title node is the selected ListItem outside the directory", delegate {
            CourseSnapshot page = Parse(Captured());
            Check(page.CurrentTitleNode != null && page.CurrentTitleNode.Role == "ListItem", "native title node returned");
            Check(!page.Directory.Walk().Contains(page.CurrentTitleNode), "not a sidebar title");
        });
        Run("additional Custom wrappers preserve semantic recognition", delegate {
            UiNode root = Captured();
            UiNode page = root.Children[0];
            root.Children.Clear();
            UiNode outer = new UiNode { Role = "Custom" }, inner = new UiNode { Role = "Custom" };
            outer.Children.Add(inner); inner.Children.Add(page); root.Children.Add(outer);
            Check(Parse(root).CurrentTitle == "导论", "wrapper tolerance");
        });
        Run("a unique study document can be identified by directory and card structure", delegate {
            UiNode root = Captured(); root.Children[0].Name = "国家安全教育学习中心";
            Check(Parse(root).Chapters.Count == 34, "semantic document fallback");
        });
        Run("two separate learning documents are rejected", delegate {
            UiNode root = Captured(); root.Children.Add(Clone(root.Children[0]));
            Check(SnapshotParser.Parse(root).Error != "", "do not choose arbitrary page");
        });
        Run("two independent equal-sized semantic directories are rejected", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            page.StudyDocument.Children.Add(Clone(page.Directory));
            Check(SnapshotParser.Parse(root).Error != "", "ambiguous directories cannot be merged");
        });
        Run("two semantic video cards do not become a single completed task", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            page.StudyDocument.Children.Add(Clone(page.Playback.CardNode));
            Playback playback = Parse(root).Playback;
            Check(playback.Mark == TaskMark.Unknown && VideoPolicy.PlaybackProblem(playback) != "", "ambiguous video documents fail closed");
        });
        Run("two players inside one real card fail closed", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            UiNode player = page.Playback.CardNode.Walk().First(n => n.Name == "视频播放器");
            page.Playback.CardNode.Children.Add(Clone(player));
            Playback playback = Parse(root).Playback;
            Check(playback.PlayerCount == 2 && VideoPolicy.PlaybackProblem(playback) != "", "multiple players remain unsupported");
        });
        Run("a named video card cannot hide an independent unnamed video document", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            UiNode other = Clone(page.Playback.CardNode); other.Name = "";
            page.StudyDocument.Children.Add(other);
            Playback playback = Parse(root).Playback;
            Check(playback.CardNode == null && playback.Mark == TaskMark.Unknown, "both cards must remain ambiguous");
        });
        Run("real card time labels and pause-video name can be read after play", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            UiNode player = page.Playback.CardNode.Walk().First(n => n.Name == "视频播放器");
            page.Playback.PlayButton.Name = "暂停视频";
            foreach (string name in new string[] { "当前时间", "4:40", "/", "时长", "10:00" })
                player.Children.Add(new UiNode { Role = "Text", Name = name });
            Playback playback = Parse(root).Playback;
            Check(playback.Playing && playback.Position == 280 && playback.Duration == 600, "real-style metadata");
            Check(VideoPolicy.UntilCheck(playback) == 300, "remaining-time scheduling");
        });
        Run("unrecognized task marker is never treated as completed", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            page.Playback.CardNode.Walk().First(n => n.Name == "任务点未完成").Name = "任务点待验证";
            Playback playback = Parse(root).Playback;
            Check(playback.Mark == TaskMark.Unknown && VideoPolicy.PlaybackProblem(playback) != "", "unknown stays unknown");
        });
        Run("an extra task marker outside the video document cannot contaminate its status", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            page.StudyDocument.Children.Add(new UiNode { Role = "ListItem", Name = "任务点已完成" });
            Playback playback = Parse(root).Playback;
            Check(playback.TaskCount == 1 && playback.Mark == TaskMark.Incomplete, "only scoped card task counts");
        });
        Run("multiple chapters with duplicate video titles still fail closed", delegate {
            UiNode root = Captured(); CourseSnapshot page = Parse(root);
            UiNode label = page.Directory.Walk().First(n => n.Role == "Text" && n.Name == "1.2 总体国家安全观的科学内涵");
            label.Name = "1.2 总体国家安全观的创立";
            Check(SnapshotParser.Parse(root).Error.Contains("同名视频"), "duplicate safety survives combined labels");
        });
        Console.WriteLine("REAL CAPTURE RESULT " + passed + " passed, " + failed + " failed");
        return failed;
    }
}
