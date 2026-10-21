using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace StudyVideoHelper
{
    // Deliberately has no focus, mouse, playback, or chapter-navigation action.
    internal static class HotkeyCheck
    {
        private const string ExpectedHelperPath = @"D:\xuexitong——shuati\StudyVideoHelper.exe";
        private const ushort Control = 0x11, Alt = 0x12, F11 = 0x7A, F12 = 0x7B;
        private const uint Keyboard = 1, KeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx, dy;
            internal uint mouseData, flags, time;
            internal UIntPtr extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            internal ushort virtualKey, scan;
            internal uint flags, time;
            internal UIntPtr extra;
        }

        [StructLayout(LayoutKind.Explicit)]
        internal struct INPUTUNION
        {
            [FieldOffset(0)] internal MOUSEINPUT mouse;
            [FieldOffset(0)] internal KEYBDINPUT keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            internal uint type;
            internal INPUTUNION data;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        private static INPUT Key(ushort key, bool up)
        {
            INPUT input = new INPUT { type = Keyboard };
            input.data.keyboard.virtualKey = key;
            input.data.keyboard.flags = up ? KeyUp : 0;
            return input;
        }

        private static void CheckLayout()
        {
            bool x64 = IntPtr.Size == 8;
            int inputSize = Marshal.SizeOf(typeof(INPUT));
            if (inputSize != (x64 ? 40 : 28) ||
                Marshal.SizeOf(typeof(MOUSEINPUT)) != (x64 ? 32 : 24) ||
                Marshal.SizeOf(typeof(KEYBDINPUT)) != (x64 ? 24 : 16) ||
                Marshal.SizeOf(typeof(INPUTUNION)) != (x64 ? 32 : 24) ||
                Marshal.OffsetOf(typeof(INPUT), "data").ToInt32() != (x64 ? 8 : 4) ||
                Marshal.OffsetOf(typeof(KEYBDINPUT), "extra").ToInt32() != (x64 ? 16 : 12) ||
                Marshal.OffsetOf(typeof(INPUTUNION), "keyboard").ToInt32() != 0)
                throw new InvalidOperationException("Native INPUT / KEYBDINPUT layout is incorrect; no input was sent.");
            Console.WriteLine("LAYOUT {0}: INPUT={1}, unionOffset={2}, KEYBDINPUT={3}, extraOffset={4}",
                x64 ? "x64" : "x86", inputSize, Marshal.OffsetOf(typeof(INPUT), "data"),
                Marshal.SizeOf(typeof(KEYBDINPUT)), Marshal.OffsetOf(typeof(KEYBDINPUT), "extra"));
        }

        private static void CheckHelper(int expectedPid)
        {
            List<int> helperIds = new List<int>();
            foreach (Process process in Process.GetProcessesByName("StudyVideoHelper"))
                using (process) { helperIds.Add(process.Id); }
            if (helperIds.Count != 1 || helperIds[0] != expectedPid)
                throw new InvalidOperationException("Expected exactly one StudyVideoHelper process, with PID " + expectedPid + ".");
            using (Process helper = Process.GetProcessById(expectedPid))
            {
                if (helper.HasExited) throw new InvalidOperationException("The expected helper has exited.");
                string actualPath = Path.GetFullPath(helper.MainModule.FileName);
                if (!string.Equals(actualPath, Path.GetFullPath(ExpectedHelperPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Helper executable is not the expected root executable: " + actualPath);
                Console.WriteLine("CHECK helper PID={0}, root={1}", expectedPid, actualPath);
            }
        }

        private static void CheckKeysReleased(ushort functionKey)
        {
            // Check both aggregate and left/right modifier states before owning any key.
            int[] physicalKeys = { Control, 0xA2, 0xA3, Alt, 0xA4, 0xA5, functionKey };
            foreach (int key in physicalKeys)
                if ((GetAsyncKeyState(key) & 0x8000) != 0)
                    throw new InvalidOperationException("Ctrl, Alt or the selected function key is currently down; no input was sent.");
        }

        private static void RecordAccepted(INPUT[] inputs, uint accepted, bool[] owned, ushort[] keys)
        {
            // SendInput inserts these events in order. Only its accepted prefix can
            // establish ownership; an unaccepted key-down must never be cleaned up.
            int count = (int)Math.Min(accepted, (uint)inputs.Length);
            for (int i = 0; i < count; i++)
                for (int key = 0; key < keys.Length; key++)
                    if (inputs[i].data.keyboard.virtualKey == keys[key])
                        owned[key] = (inputs[i].data.keyboard.flags & KeyUp) == 0;
        }

        private static bool ReleaseOwnedKeys(bool[] owned, ushort[] keys)
        {
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    List<INPUT> releases = new List<INPUT>();
                    for (int key = keys.Length - 1; key >= 0; key--)
                        if (owned[key]) releases.Add(Key(keys[key], true));
                    if (releases.Count == 0) return true;
                    INPUT[] inputs = releases.ToArray();
                    uint accepted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                    RecordAccepted(inputs, accepted, owned, keys);
                    Console.Error.WriteLine("CLEANUP accepted {0}/{1} owned key-up events.", accepted, inputs.Length);
                }
            }
            catch (Exception error) { Console.Error.WriteLine("CLEANUP failed: " + error.Message); }
            foreach (bool down in owned)
                if (down) return false;
            return true;
        }

        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine("Usage: HotkeyCheck.exe <expected-helper-PID> [--start]");
                Console.WriteLine("Default action sends Ctrl+Alt+F12; --start sends Ctrl+Alt+F11. An explicit positive PID and the unique exact D-path helper are required.");
                return 0;
            }
            int expectedPid;
            if ((args.Length != 1 && args.Length != 2) || !int.TryParse(args[0], out expectedPid) || expectedPid <= 0 ||
                (args.Length == 2 && args[1] != "--start"))
            {
                Console.Error.WriteLine("Usage: HotkeyCheck.exe <expected-helper-PID> [--start]");
                return 2;
            }
            int result = 1;
            ushort[] keys = { Control, Alt, F12 };
            bool[] owned = new bool[keys.Length];
            try
            {
                if (args.Length == 2) keys[2] = F11;
                CheckLayout();
                CheckHelper(expectedPid);
                using (ManualResetEventSlim stop = new ManualResetEventSlim(false))
                {
                    WindowsStudyDriver driver = new WindowsStudyDriver(WindowsStudyDriver.FindWindow(), stop);
                    driver.Guard();
                    CheckKeysReleased(keys[2]);
                    INPUT[] inputs = {
                        Key(Control, false), Key(Alt, false), Key(keys[2], false),
                        Key(keys[2], true), Key(Alt, true), Key(Control, true)
                    };
                    uint accepted = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                    int nativeError = Marshal.GetLastWin32Error();
                    RecordAccepted(inputs, accepted, owned, keys);
                    if (accepted != inputs.Length)
                        throw new InvalidOperationException("SendInput accepted " + accepted + "/6 events; Win32 error=" + nativeError + ".");
                    Console.WriteLine("INPUT ACCEPTED 6/6: Ctrl+Alt+" + (keys[2] == F11 ? "F11" : "F12") + " down/up sequence. No focus or mouse action was performed.");
                    Console.WriteLine("This confirms input acceptance only. Check the real helper's session log to confirm delivery.");
                    result = 0;
                }
            }
            catch (Exception error) { Console.Error.WriteLine("FAILED: " + error.Message); }
            finally
            {
                if (!ReleaseOwnedKeys(owned, keys))
                {
                    Console.Error.WriteLine("FAILED: some injected key-downs could not be released.");
                    result = 1;
                }
            }
            return result;
        }
    }
}
