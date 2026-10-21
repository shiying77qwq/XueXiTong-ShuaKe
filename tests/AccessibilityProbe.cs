using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Accessibility;

namespace StudyVideoHelper
{
    // Debug-only: requests standard MSAA client objects from the exact target window.
    // Does not activate, click, navigate, send raw WM_GETOBJECT, or change system settings.
    internal static class AccessibilityProbe
    {
        private delegate bool ChildCallback(IntPtr handle, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int capacity);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(
            IntPtr handle, uint objectId, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);

        private static readonly Guid AccessibleId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        private static string outputDirectory;
        private static string artifactPrefix;
        private static readonly List<IAccessible> objects = new List<IAccessible>();

        private static void Log(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + text;
            Console.WriteLine(line);
            File.AppendAllText(Path.Combine(outputDirectory, artifactPrefix + "-status.txt"), line + Environment.NewLine, Encoding.UTF8);
        }

        private static string ClassName(IntPtr handle)
        {
            StringBuilder name = new StringBuilder(256);
            GetClassName(handle, name, name.Capacity);
            return name.ToString();
        }

        private static void ReadClient(IntPtr handle, uint objectId, ManualResetEventSlim stop)
        {
            Log("MSAA begin objectId=" + unchecked((int)objectId) + " class=" + ClassName(handle) + " HWND=" + handle.ToInt64());
            string result = BoundedRead.Run(delegate {
                Guid id = AccessibleId;
                IAccessible accessible;
                int status = AccessibleObjectFromWindow(handle, objectId, ref id, out accessible);
                if (status < 0 || accessible == null)
                    return "HRESULT=0x" + status.ToString("X8") + " object=" + (accessible != null);
                // Keep the normal client object alive until the independent UIA reread finishes.
                objects.Add(accessible);
                object role = accessible.get_accRole(0);
                int children = accessible.accChildCount;
                string name = accessible.get_accName(0) ?? "";
                if (name.Length > 120) name = name.Substring(0, 120);
                return "HRESULT=0x" + status.ToString("X8") + " role=" + role + " children=" + children + " name=" + name;
            }, stop, TimeSpan.FromSeconds(20));
            Log("MSAA end " + result);
        }

        [MTAThread]
        private static int Main(string[] args)
        {
            outputDirectory = args.Length > 0 ? Path.GetFullPath(args[0]) :
                Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            bool custom = args.Length > 1 && args[1] == "custom";
            artifactPrefix = custom ? "probe-custom" : "probe";
            try
            {
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, artifactPrefix + "-status.txt"), "", Encoding.UTF8);
                IntPtr target = WindowsStudyDriver.FindWindow();
                uint targetProcess;
                Native.GetWindowThreadProcessId(target, out targetProcess);
                Log("Target process=" + targetProcess + " class=" + ClassName(target) +
                    " foreground=" + (Native.GetAncestor(Native.GetForegroundWindow(), 2) == target));
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    WindowsStudyDriver before = new WindowsStudyDriver(target, stop);
                    before.Trace = Log;
                    Log("BEFORE begin");
                    File.WriteAllText(Path.Combine(outputDirectory, artifactPrefix + "-before.txt"), before.Diagnose(), Encoding.UTF8);
                    Log("BEFORE saved");

                    List<IntPtr> targets = new List<IntPtr> { target };
                    EnumChildWindows(target, delegate(IntPtr child, IntPtr unused) {
                        if (Native.IsWindowVisible(child) && Native.GetAncestor(child, 2) == target &&
                            ClassName(child).StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal))
                            targets.Add(child);
                        return true;
                    }, IntPtr.Zero);
                    Log("Visible target/client HWND count=" + targets.Count);
                    foreach (IntPtr candidate in targets) ReadClient(candidate, custom ? 1U : unchecked((uint)-4), stop);
                    if (stop.Wait(custom ? 3000 : 1500)) throw new OperationCanceledException();

                    WindowsStudyDriver after = new WindowsStudyDriver(target, stop);
                    after.Trace = Log;
                    Log("AFTER begin");
                    string diagnostic = after.Diagnose();
                    File.WriteAllText(Path.Combine(outputDirectory, artifactPrefix + "-after.txt"), diagnostic, Encoding.UTF8);
                    bool matched = diagnostic.Contains("识别结果：课程目录已识别");
                    Log("AFTER saved courseRecognized=" + matched);
                    return matched ? 0 : 2;
                }
            }
            catch (Exception error)
            {
                Log("FAIL " + error);
                return 1;
            }
            finally
            {
                foreach (IAccessible accessible in objects)
                    if (Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible);
            }
        }
    }
}
