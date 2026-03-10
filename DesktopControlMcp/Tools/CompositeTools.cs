using System.ComponentModel;
using DesktopControlMcp.Native;
using DesktopControlMcp.Services;
using ModelContextProtocol.Server;

namespace DesktopControlMcp.Tools;

[McpServerToolType]
public sealed class CompositeTools
{
    [McpServerTool(Name = "click_and_type"), Description("Click at a position then type text. Useful for filling input fields. Automatically focuses the window first.")]
    public static string ClickAndType(
        [Description("X coordinate to click")] int x,
        [Description("Y coordinate to click")] int y,
        [Description("Text to type after clicking")] string text,
        [Description("If true, clears the field (Ctrl+A, Delete) before typing")] bool clearFirst = false)
    {
        Win32.FocusWindowAt(x, y);
        Thread.Sleep(100);

        NativeInput.MoveMouse(x, y);
        Thread.Sleep(30);
        NativeInput.MouseClick("left", 1);
        Thread.Sleep(100);

        if (clearFirst)
        {
            NativeInput.Hotkey("ctrl", "a");
            Thread.Sleep(50);
            NativeInput.KeyPress(NativeInput.VK_DELETE);
            Thread.Sleep(50);
        }

        NativeInput.TypeUnicode(text);
        return $"Typed {text.Length} chars at {x},{y}";
    }

    [McpServerTool(Name = "navigate_to_url"), Description("Navigate browser to a URL: focuses Chrome, types URL in address bar, presses Enter.")]
    public static string NavigateToUrl(
        [Description("URL to navigate to")] string url,
        [Description("Optional - browser window title to target (default: Chrome)")] string windowTitle = "Chrome")
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero)
            return $"NOT FOUND: no window matching '{windowTitle}'";

        Win32.FocusWindow(hWnd);
        Thread.Sleep(150);

        var addressBar = UiAutomationHelper.FindElement("Address and search bar", windowTitle)
                         ?? UiAutomationHelper.FindElement("address", windowTitle);

        if (addressBar != null)
        {
            UiAutomationHelper.SetValue(addressBar, url);
        }
        else
        {
            NativeInput.Hotkey("ctrl", "l");
            Thread.Sleep(200);
            NativeInput.Hotkey("ctrl", "a");
            Thread.Sleep(50);
            NativeInput.TypeUnicode(url);
        }

        Thread.Sleep(100);
        NativeInput.KeyPress(NativeInput.VK_RETURN);

        return $"Navigated to {url}";
    }

    [McpServerTool(Name = "auto_scroll"), Description("Scroll a window automatically with pauses between scrolls. Great for reading.")]
    public static string AutoScroll(
        [Description("Number of scroll batches")] int times = 5,
        [Description("Seconds to pause between each scroll")] double pauseSeconds = 4,
        [Description("Scroll amount per batch (negative=down, positive=up)")] int amount = -3,
        [Description("Optional window title to focus first")] string windowTitle = "")
    {
        pauseSeconds = Math.Clamp(pauseSeconds, 0.5, 15);
        times = Math.Clamp(times, 1, 50);

        if (!string.IsNullOrEmpty(windowTitle))
        {
            var hWnd = Win32.FindWindowByTitle(windowTitle);
            if (hWnd != nint.Zero)
            {
                Win32.FocusWindow(hWnd);
                Thread.Sleep(150);

                Win32.GetWindowRect(hWnd, out var rect);
                int cx = rect.Left + rect.Width / 2;
                int cy = rect.Top + rect.Height / 2;
                NativeInput.MoveMouse(cx, cy);
                Thread.Sleep(30);
                NativeInput.MouseClick("left", 1);
                Thread.Sleep(100);
            }
        }

        for (int i = 0; i < times; i++)
        {
            NativeInput.MouseScroll(amount);
            if (i < times - 1)
                Thread.Sleep((int)(pauseSeconds * 1000));
        }

        return $"Scrolled {times}x (amount={amount})";
    }

    [McpServerTool(Name = "maximize_window"), Description("Maximize a window by title.")]
    public static string MaximizeWindow(
        [Description("Part of the window title (case-insensitive)")] string windowTitle)
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: '{windowTitle}'";

        Win32.FocusWindow(hWnd);
        Win32.ShowWindow(hWnd, 3 /* SW_MAXIMIZE */);
        return $"Maximized: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
    }

    [McpServerTool(Name = "minimize_window"), Description("Minimize a window by title.")]
    public static string MinimizeWindow(
        [Description("Part of the window title (case-insensitive)")] string windowTitle)
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: '{windowTitle}'";

        Win32.ShowWindow(hWnd, 6 /* SW_MINIMIZE */);
        return $"Minimized: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
    }

    [McpServerTool(Name = "restore_window"), Description("Restore a minimized/maximized window to normal size.")]
    public static string RestoreWindow(
        [Description("Part of the window title (case-insensitive)")] string windowTitle)
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: '{windowTitle}'";

        Win32.ShowWindow(hWnd, 9 /* SW_RESTORE */);
        Win32.FocusWindow(hWnd);
        return $"Restored: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
    }

    [McpServerTool(Name = "open_app"), Description("Open an app by name. Tries: 1) focus existing window, 2) click taskbar icon, 3) Windows search.")]
    public static string OpenApp(
        [Description("App name to open (e.g. 'Telegram', 'Spotify', 'Chrome')")] string appName)
    {
        // Strategy 1: Already open — just focus it
        var hWnd = Win32.FindWindowByTitle(appName);
        if (hWnd != nint.Zero)
        {
            Win32.FocusWindow(hWnd);
            Thread.Sleep(100);
            return $"Focused: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
        }

        // Strategy 2: Click taskbar icon
        var scene = DesktopScanner.ScanAll();
        var tbItem = scene.TaskbarElements.FirstOrDefault(t =>
            t.Text.Contains(appName, StringComparison.OrdinalIgnoreCase) && t.Role == "taskbar-app");

        if (tbItem != null)
        {
            NativeInput.MoveMouse(tbItem.Bounds.CenterX, tbItem.Bounds.CenterY);
            Thread.Sleep(30);
            NativeInput.MouseClick("left", 1);
            Thread.Sleep(1500);

            hWnd = Win32.FindWindowByTitle(appName);
            if (hWnd != nint.Zero)
            {
                Win32.FocusWindow(hWnd);
                return $"Opened via taskbar: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
            }
        }

        // Strategy 3: Windows Start search
        NativeInput.Hotkey("win");
        Thread.Sleep(800);
        NativeInput.TypeUnicode(appName);
        Thread.Sleep(1000);
        NativeInput.KeyPress(NativeInput.VK_RETURN);
        Thread.Sleep(2000);

        hWnd = Win32.FindWindowByTitle(appName);
        if (hWnd != nint.Zero)
        {
            Win32.FocusWindow(hWnd);
            return $"Opened via search: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
        }

        return $"NOT FOUND: could not open '{appName}'";
    }

    [McpServerTool(Name = "wait_seconds"), Description("Pause for a number of seconds (max 30). Useful between actions.")]
    public static string WaitSeconds(
        [Description("Seconds to wait")] double seconds)
    {
        seconds = Math.Min(seconds, 30);
        Thread.Sleep((int)(seconds * 1000));
        return $"Waited {seconds}s";
    }
}
