using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopControlMcp.Native;

/// <summary>
/// Win32 API declarations for window enumeration and management.
/// </summary>
internal static class Win32
{
    /// <summary>
    /// Strip invisible Unicode characters (LTR/RTL marks, zero-width spaces, etc.)
    /// that apps like Telegram embed in window titles, breaking string matching.
    /// </summary>
    public static string StripInvisibleChars(string text)
        => Regex.Replace(text, @"[\u200B-\u200F\u202A-\u202E\u2060-\u2069\uFEFF]", "");

    public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(nint hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, nint pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    public static extern nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(nint hWnd, nint hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern nint GetWindowDC(nint hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(nint hWnd, nint hdc);

    [DllImport("gdi32.dll")]
    public static extern bool BitBlt(nint hdcDest, int xDest, int yDest, int width, int height, nint hdcSrc, int xSrc, int ySrc, uint rop);

    public const uint PW_RENDERFULLCONTENT = 0x00000002;
    public const uint SRCCOPY = 0x00CC0020;

    public const byte VK_MENU = 0x12; // Alt key
    public const uint KEYEVENTF_KEYUP_FLAG = 0x02;
    public const uint SPI_SETFOREGROUNDLOCKTIMEOUT = 0x2001;
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;
    public const int ASFW_ANY = -1;
    public const int GWL_EXSTYLE = -20;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public static string GetWindowTitle(nint hWnd)
    {
        var sb = new StringBuilder(1024);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClassName(nint hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsCloaked(nint hWnd)
    {
        int cloaked = 0;
        int hr = DwmGetWindowAttribute(hWnd, 14, out cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint hwnd, uint gaFlags);

    public const uint GA_ROOTOWNER = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    public static long GetExStyle(nint hWnd) => GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();

    /// <summary>
    /// Focus the top-level window at the given screen coordinates.
    /// </summary>
    public static bool FocusWindowAt(int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        var child = WindowFromPoint(pt);
        if (child == nint.Zero) return false;
        var root = GetAncestor(child, GA_ROOTOWNER);
        if (root == nint.Zero) root = child;
        return FocusWindow(root);
    }

    /// <summary>
    /// Reliably bring a window to the foreground using multiple bypass techniques:
    /// 1. AllowSetForegroundWindow to unlock foreground setting
    /// 2. Alt key trick to bypass foreground lock (simulates user input)
    /// 3. AttachThreadInput to borrow foreground thread permissions
    /// 4. Disable foreground lock timeout via SystemParametersInfo
    /// Windows normally prevents background processes from stealing focus —
    /// this method uses all known techniques to overcome that.
    /// </summary>
    public static bool FocusWindow(nint hWnd)
    {
        if (hWnd == nint.Zero) return false;

        // If minimized, restore it first
        if (IsIconic(hWnd))
            ShowWindow(hWnd, SW_RESTORE);

        var foreground = GetForegroundWindow();
        if (foreground == hWnd) return true; // already focused

        // Technique 1: Allow any process to set foreground
        AllowSetForegroundWindow(ASFW_ANY);

        // Technique 2: Disable foreground lock timeout
        SystemParametersInfo(SPI_SETFOREGROUNDLOCKTIMEOUT, 0, nint.Zero, 0);

        // Technique 3: Alt key trick — pressing Alt simulates user input,
        // which tricks Windows into thinking we're an interactive foreground app
        keybd_event(VK_MENU, 0, 0, nint.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP_FLAG, nint.Zero);

        // Technique 4: Attach our thread to the foreground window's thread
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var currentThread = GetCurrentThreadId();

        bool attached = false;
        if (foregroundThread != currentThread)
            attached = AttachThreadInput(currentThread, foregroundThread, true);

        // Now do the actual focus
        BringWindowToTop(hWnd);
        ShowWindow(hWnd, SW_SHOW);
        SetForegroundWindow(hWnd);

        if (attached)
            AttachThreadInput(currentThread, foregroundThread, false);

        // Verify — if still not focused, try one more time with SendMessage
        if (GetForegroundWindow() != hWnd)
        {
            // WM_ACTIVATE trick
            SendMessage(hWnd, 0x0006 /*WM_ACTIVATE*/, 1, 0);
            SetForegroundWindow(hWnd);
        }

        Thread.Sleep(50);
        return GetForegroundWindow() == hWnd;
    }

    /// <summary>
    /// Find first visible window whose title contains the search string.
    /// </summary>
    public static nint FindWindowByTitle(string search)
    {
        nint found = nint.Zero;
        var cleanSearch = StripInvisibleChars(search);
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (IsCloaked(hWnd)) return true;
            var title = StripInvisibleChars(GetWindowTitle(hWnd));
            if (title.Contains(cleanSearch, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumeration
            }
            return true;
        }, nint.Zero);
        return found;
    }

    /// <summary>
    /// Find all visible windows whose title contains the search string.
    /// Returns list of (hWnd, title) pairs.
    /// </summary>
    public static List<(nint hWnd, string title)> FindAllWindowsByTitle(string search)
    {
        var results = new List<(nint, string)>();
        var cleanSearch = StripInvisibleChars(search);
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            if (IsCloaked(hWnd)) return true;
            var title = GetWindowTitle(hWnd);
            if (StripInvisibleChars(title).Contains(cleanSearch, StringComparison.OrdinalIgnoreCase))
                results.Add((hWnd, title));
            return true;
        }, nint.Zero);
        return results;
    }
}
