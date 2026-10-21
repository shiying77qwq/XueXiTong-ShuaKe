using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace StudyVideoHelper
{
    internal static class NativeAccessibility
    {
        internal const uint ClientObjectId = unchecked((uint)-4);
        private static readonly Guid AccessibleId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");
        private delegate bool ChildCallback(IntPtr handle, IntPtr parameter);

        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int capacity);
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(
            IntPtr handle, uint objectId, ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] out object accessible);

        private static int ReadClient(IntPtr handle, uint objectId, out bool received)
        {
            object accessible = null;
            received = false;
            try
            {
                Guid id = AccessibleId;
                int status = AccessibleObjectFromWindow(handle, objectId, ref id, out accessible);
                received = status >= 0 && accessible != null;
                return status;
            }
            finally
            {
                if (accessible != null && Marshal.IsComObject(accessible)) Marshal.ReleaseComObject(accessible);
            }
        }

        internal static bool TryReadClient(IntPtr handle, uint objectId)
        {
            if (!Native.IsWindow(handle)) return false;
            bool received;
            ReadClient(handle, objectId, out received);
            return received;
        }

        private static string ClassName(IntPtr handle)
        {
            StringBuilder name = new StringBuilder(256);
            GetClassName(handle, name, name.Capacity);
            return name.ToString();
        }

        internal static void WarmUp(IntPtr target, ManualResetEventSlim stop, Action<string> trace)
        {
            if (stop.IsSet) throw new OperationCanceledException();
            if (!Native.IsWindow(target)) throw new InvalidOperationException("学习通窗口已关闭。");
            List<IntPtr> targets = new List<IntPtr> { target };
            EnumChildWindows(target, delegate(IntPtr child, IntPtr unused) {
                if (stop.IsSet) return false;
                if (Native.IsWindowVisible(child) && Native.GetAncestor(child, 2) == target &&
                    ClassName(child).StartsWith("Chrome_RenderWidgetHostHWND", StringComparison.Ordinal))
                    targets.Add(child);
                return true;
            }, IntPtr.Zero);
            if (stop.IsSet) throw new OperationCanceledException();
            if (trace != null) trace("UIA accessibility initialization: target/client windows=" + targets.Count);
            foreach (IntPtr candidate in targets)
            {
                if (stop.IsSet) throw new OperationCanceledException();
                if (!Native.IsWindow(candidate) || (candidate != target &&
                    (!Native.IsWindowVisible(candidate) || Native.GetAncestor(candidate, 2) != target))) continue;
                bool received;
                // Chromium defines object 1 as its on-demand accessibility request.
                // A failed HRESULT may still enable the provider; only the following
                // normal UIA capture determines whether the course is accessible.
                int status = ReadClient(candidate, 1, out received);
                if (trace != null) trace("UIA accessibility request object=1 class=" + ClassName(candidate) +
                    " HRESULT=0x" + status.ToString("X8") + " object=" + received);
            }
            if (trace != null) trace("UIA accessibility initialization: waiting 3s before semantic recheck");
            if (stop.Wait(3000)) throw new OperationCanceledException();
        }
    }
}
