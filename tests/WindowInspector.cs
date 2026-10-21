using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Automation;

namespace StudyVideoHelper
{
    internal static class WindowInspector
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            return Task.Factory.StartNew(delegate {
                if (args.Length == 1 && args[0] == "--course-foreground") {
                    try {
                        new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), new ManualResetEventSlim(false)).Guard();
                        Console.WriteLine("COURSE_FOREGROUND=True");
                        return 0;
                    } catch (Exception error) {
                        Console.WriteLine("COURSE_FOREGROUND=False: " + error.Message);
                        return 2;
                    }
                }
                HashSet<int> ids = new HashSet<int>();
                foreach (Process process in Process.GetProcessesByName("StudyVideoHelper")) {
                    using (process) { ids.Add(process.Id); Console.WriteLine("Helper PID=" + process.Id); }
                }
                var windows = WindowsStudyDriver.FindVisibleWindows(ids);
                bool invokeVerification = args.Length == 1 && args[0] == "--invoke-verification";
                bool invokeStart = args.Length == 1 && args[0] == "--invoke-start";
                if ((invokeVerification || invokeStart) && (ids.Count != 1 || windows.Count != 1))
                    throw new InvalidOperationException("Exactly one helper process with one visible helper window is required.");
                foreach (IntPtr handle in windows) {
                    Console.WriteLine("Visible helper window");
                    WindowsStudyDriver driver = new WindowsStudyDriver(handle, new ManualResetEventSlim(false));
                    Console.WriteLine(driver.Diagnose());
                    if (invokeVerification || invokeStart) {
                        string buttonId = invokeStart ? "start" : "verify";
                        string buttonName = invokeStart ? "开始" : "验证一次切换";
                        UiNode button = driver.LastTree.Walk().Single(n => n.Role == "Button" && n.Id == buttonId && n.Name == buttonName);
                        AutomationElement native = (AutomationElement)button.Native;
                        if (!native.Current.IsEnabled) throw new InvalidOperationException("The requested helper button is busy.");
                        ((InvokePattern)native.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        Console.WriteLine("Invoked this helper's " + (invokeStart ? "start" : "verification") + " button; inspect its new session log for the outcome.");
                    }
                }
                return 0;
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Result;
        }
    }
}
