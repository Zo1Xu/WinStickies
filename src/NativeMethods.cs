using System;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WinStickies
{
    internal static class Native
    {
        internal const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        internal const long WS_CHILD = 0x40000000L, WS_POPUP = 0x80000000L, WS_EX_TOPMOST = 8;
        internal const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOZORDER = 4, SWP_NOACTIVATE = 16,
            SWP_FRAMECHANGED = 32, SWP_SHOWWINDOW = 64;
        internal static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        internal delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; public Rectangle Rectangle { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } } }
        [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
        [DllImport("user32.dll")] internal static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc callback, IntPtr param);
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] internal static extern bool ScreenToClient(IntPtr hwnd, ref POINT point);
        [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int GetWindowLong32(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] private static extern IntPtr GetWindowLong64(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] private static extern IntPtr SetWindowLong64(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string message);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        internal static long GetStyle(IntPtr hwnd, int index) { return IntPtr.Size == 8 ? GetWindowLong64(hwnd, index).ToInt64() : (long)(uint)GetWindowLong32(hwnd, index); }
        internal static void SetStyle(IntPtr hwnd, int index, long style)
        {
            if (IntPtr.Size == 8) SetWindowLong64(hwnd, index, new IntPtr(style));
            else SetWindowLong32(hwnd, index, unchecked((int)style));
        }

        // Use the shell window that CONTAINS desktop icons. A wallpaper WorkerW behind
        // SHELLDLL_DefView would make editable notes invisible or unclickable.
        internal static IntPtr DesktopHost()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                if (FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                { found = window; return false; }
                return true;
            }, IntPtr.Zero);
            // Windows 11 can nest DefView below a WorkerW child of Progman.
            if (found == IntPtr.Zero)
            {
                IntPtr shell = GetShellWindow();
                if (shell == IntPtr.Zero) shell = FindWindow("Progman", null);
                if (shell != IntPtr.Zero)
                    EnumChildWindows(shell, delegate(IntPtr child, IntPtr unused)
                    {
                        var name = new StringBuilder(128); GetClassName(child, name, name.Capacity);
                        if (name.ToString() == "SHELLDLL_DefView") { found = GetParent(child); return false; }
                        return true;
                    }, IntPtr.Zero);
            }
            return found;
        }

        internal static void MatchShellDpi()
        {
            try
            {
                IntPtr host = DesktopHost();
                if (host != IntPtr.Zero) SetThreadDpiAwarenessContext(GetWindowDpiAwarenessContext(host));
            }
            catch (EntryPointNotFoundException) { }
        }

        internal static Rectangle Bounds(IntPtr hwnd) { RECT rect; GetWindowRect(hwnd, out rect); return rect.Rectangle; }

        internal static void Place(IntPtr hwnd, Rectangle screenBounds, IntPtr parent)
        {
            var point = new POINT(screenBounds.X, screenBounds.Y);
            if (parent != IntPtr.Zero) ScreenToClient(parent, ref point);
            if (!SetWindowPos(hwnd, IntPtr.Zero, point.X, point.Y, screenBounds.Width, screenBounds.Height,
                SWP_NOZORDER | SWP_NOACTIVATE)) throw new Win32Exception();
        }

        internal static void SetMode(IntPtr hwnd, NoteMode mode, Rectangle bounds)
        {
            IntPtr host = mode == NoteMode.Desktop ? DesktopHost() : IntPtr.Zero;
            if (mode == NoteMode.Desktop && host == IntPtr.Zero)
                throw new InvalidOperationException("暂时找不到 Windows 桌面，请等待资源管理器启动后重试。");
            if (host != IntPtr.Zero)
            {
                try
                {
                    if (!AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(hwnd), GetWindowDpiAwarenessContext(host)))
                        throw new InvalidOperationException("桌面的显示缩放设置已变化，请重启便签后再附着。");
                }
                catch (EntryPointNotFoundException) { }
            }
            IntPtr oldParent = (GetStyle(hwnd, GWL_STYLE) & WS_CHILD) != 0 ? GetParent(hwnd) : IntPtr.Zero;
            long oldStyle = GetStyle(hwnd, GWL_STYLE);
            bool wasTopmost = (GetStyle(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
            try
            {
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                if (host != IntPtr.Zero)
                {
                    SetStyle(hwnd, GWL_STYLE, (oldStyle & ~WS_POPUP) | WS_CHILD);
                    SetParent(hwnd, host);
                    if (GetParent(hwnd) != host) throw new Win32Exception();
                }
                else
                {
                    // Clear WS_CHILD only AFTER detaching, as required by SetParent.
                    SetParent(hwnd, IntPtr.Zero);
                    SetStyle(hwnd, GWL_STYLE, (oldStyle & ~WS_CHILD) | WS_POPUP);
                    if (GetParent(hwnd) != IntPtr.Zero) throw new Win32Exception();
                }
                Place(hwnd, bounds, host);
                SetWindowPos(hwnd, mode == NoteMode.Topmost ? HWND_TOPMOST : IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
            }
            catch
            {
                SetParent(hwnd, oldParent);
                SetStyle(hwnd, GWL_STYLE, oldStyle);
                Place(hwnd, bounds, oldParent);
                SetWindowPos(hwnd, wasTopmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                throw;
            }
        }

        internal static void RoundCorners(IntPtr hwnd)
        {
            try { int rounded = 2; DwmSetWindowAttribute(hwnd, 33, ref rounded, 4); }
            catch (DllNotFoundException) { }
        }
    }
}
