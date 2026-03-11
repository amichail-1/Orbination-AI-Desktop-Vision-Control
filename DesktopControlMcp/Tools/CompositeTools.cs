using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Automation;
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

    [McpServerTool(Name = "set_clipboard"), Description("Set the system clipboard text without pasting. Useful for preparing large text (XML, code, JSON) to paste manually or with Ctrl+V.")]
    public static string SetClipboard(
        [Description("Text to put on clipboard")] string text)
    {
        NativeInput.SetClipboardText(text);
        return $"Clipboard set: {text.Length} chars";
    }

    [McpServerTool(Name = "paste_text"), Description("Set clipboard to the given text and paste via Ctrl+V. Reliable for large text like XML, code, or multi-line content that keyboard_type can't handle.")]
    public static string PasteText(
        [Description("Text to paste")] string text)
    {
        NativeInput.TypeViaClipboard(text);
        return $"Pasted: {text.Length} chars";
    }

    [McpServerTool(Name = "focus_and_hotkey"), Description("Click at coordinates to ensure focus (e.g. inside an iframe), wait, then send a keyboard shortcut. Solves iframe/web app focus issues where separate click + hotkey calls fail.")]
    public static string FocusAndHotkey(
        [Description("X coordinate to click for focus")] int x,
        [Description("Y coordinate to click for focus")] int y,
        [Description("First key (e.g. ctrl, alt, shift)")] string key1,
        [Description("Second key")] string key2 = "",
        [Description("Optional third key")] string key3 = "",
        [Description("Optional fourth key")] string key4 = "",
        [Description("Delay in ms between click and hotkey (default 200, range 50-2000)")] int delayMs = 200)
    {
        delayMs = Math.Clamp(delayMs, 50, 2000);

        // Focus the window at the click point
        Win32.FocusWindowAt(x, y);
        Thread.Sleep(100);

        // Click to give the iframe/web content focus
        NativeInput.MoveMouse(x, y);
        Thread.Sleep(30);
        NativeInput.MouseClick("left", 1);
        Thread.Sleep(delayMs);

        // Send the hotkey
        var keys = new[] { key1, key2, key3, key4 }.Where(k => !string.IsNullOrEmpty(k)).ToArray();
        NativeInput.Hotkey(keys);

        return $"Clicked {x},{y} then pressed {string.Join("+", keys)}";
    }

    [McpServerTool(Name = "wait_for_element"), Description("Poll for a UI element to appear by text with configurable timeout. Useful after actions that trigger dialogs, menus, or page loads.")]
    public static string WaitForElement(
        [Description("Element text to search for")] string text,
        [Description("Optional window filter")] string windowTitle = "",
        [Description("Timeout in seconds (default 5, max 30)")] double timeoutSeconds = 5,
        [Description("Poll interval in ms (default 500)")] double pollIntervalMs = 500)
    {
        timeoutSeconds = Math.Clamp(timeoutSeconds, 1, 30);
        pollIntervalMs = Math.Clamp(pollIntervalMs, 200, 2000);

        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < timeoutSeconds)
        {
            var element = UiAutomationHelper.FindElement(text, windowTitle);
            if (element != null)
            {
                var rect = element.Current.BoundingRectangle;
                var name = element.Current.Name ?? "";
                var ct = element.Current.ControlType.ProgrammaticName.Replace("ControlType.", "");
                int cx = (int)(rect.X + rect.Width / 2);
                int cy = (int)(rect.Y + rect.Height / 2);
                return $"FOUND [{ct}] \"{name}\" @ {cx},{cy} (after {sw.Elapsed.TotalSeconds:F1}s)";
            }
            Thread.Sleep((int)pollIntervalMs);
        }

        return $"TIMEOUT: '{text}' not found after {timeoutSeconds}s";
    }

    [McpServerTool(Name = "click_menu_item"), Description("Navigate a menu: click parent item, smoothly move to child item, click it. Uses smooth mouse movement to keep submenus open. Single tool call for web-rendered menus.")]
    public static string ClickMenuItem(
        [Description("Text of parent menu to click first")] string parentText,
        [Description("Text of child/submenu item to click")] string childText,
        [Description("Optional window filter")] string windowTitle = "",
        [Description("Wait ms for submenu to appear (default 600)")] int waitMs = 600)
    {
        waitMs = Math.Clamp(waitMs, 200, 3000);

        // Step 1: Find and click parent menu
        var parent = UiAutomationHelper.FindElement(parentText, windowTitle);
        if (parent == null) return $"NOT FOUND: parent menu '{parentText}'";

        var parentRect = parent.Current.BoundingRectangle;
        int px = (int)(parentRect.X + parentRect.Width / 2);
        int py = (int)(parentRect.Y + parentRect.Height / 2);

        NativeInput.MoveMouse(px, py);
        Thread.Sleep(30);
        NativeInput.MouseClick("left", 1);
        Thread.Sleep(waitMs);

        // Step 2: Find child item
        var child = UiAutomationHelper.FindElement(childText, windowTitle);
        if (child == null)
        {
            // Retry once — web menus may take longer to render in the automation tree
            Thread.Sleep(500);
            child = UiAutomationHelper.FindElement(childText, windowTitle);
        }

        if (child == null)
            return $"NOT FOUND: child item '{childText}' after clicking '{parentText}'";

        // Step 3: Smooth move from parent to child (keeps submenu open)
        var childRect = child.Current.BoundingRectangle;
        int cx = (int)(childRect.X + childRect.Width / 2);
        int cy = (int)(childRect.Y + childRect.Height / 2);

        NativeInput.MoveMouseSmooth(cx, cy, 250);
        Thread.Sleep(50);
        NativeInput.MouseClick("left", 1);

        return $"Clicked menu: {parentText} > {childText}";
    }

    [McpServerTool(Name = "run_sequence"), Description(
        "Execute multiple UI actions in a single call. Massively faster than individual tool calls. " +
        "Each line is one action. Actions:\n" +
        "  click \"text\"           — click element by text (UIAutomation + OCR fallback)\n" +
        "  click x,y              — click at screen coordinates\n" +
        "  doubleclick x,y        — double-click at coordinates\n" +
        "  type \"text\"            — type text via keyboard\n" +
        "  paste \"text\"           — paste via clipboard (for large/XML text)\n" +
        "  hotkey ctrl+a          — press key combination\n" +
        "  wait 500               — wait N milliseconds\n" +
        "  focus \"window title\"   — focus a window\n" +
        "  tab \"tab text\"         — select a browser tab\n" +
        "  ocr_click \"text\"       — find text via OCR and click it (skips UIAutomation)\n" +
        "  screenshot \"path\"      — take screenshot of focused window\n" +
        "Example: focus \"Chrome\"\\nwait 300\\nclick \"Extras\"\\nwait 500\\nclick \"Edit Diagram\"")]
    public static string RunSequence(
        [Description("Actions to execute, one per line")] string actions,
        [Description("Window to focus before starting (optional)")] string windowTitle = "")
    {
        var results = new StringBuilder();
        int step = 0;
        nint activeWindow = nint.Zero;

        // Focus initial window if specified
        if (!string.IsNullOrEmpty(windowTitle))
        {
            activeWindow = Win32.FindWindowByTitle(windowTitle);
            if (activeWindow != nint.Zero)
            {
                Win32.FocusWindow(activeWindow);
                Thread.Sleep(150);
                results.AppendLine($"[0] Focused: {Win32.StripInvisibleChars(Win32.GetWindowTitle(activeWindow))}");
            }
            else
            {
                results.AppendLine($"[0] WARNING: window '{windowTitle}' not found");
            }
        }

        var lines = actions.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            step++;
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//") || line.StartsWith("#")) continue;

            try
            {
                var result = ExecuteAction(line, ref activeWindow);
                results.AppendLine($"[{step}] {result}");
            }
            catch (Exception ex)
            {
                results.AppendLine($"[{step}] ERROR: {ex.Message}");
            }
        }

        results.AppendLine($"Done: {step} actions executed");
        return results.ToString();
    }

    private static string ExecuteAction(string line, ref nint activeWindow)
    {
        // Parse action and argument
        var (action, arg) = ParseAction(line);

        switch (action.ToLowerInvariant())
        {
            case "click":
                return DoClick(arg, activeWindow);

            case "doubleclick":
                if (TryParseCoords(arg, out int dx, out int dy))
                {
                    NativeInput.MoveMouse(dx, dy);
                    Thread.Sleep(20);
                    NativeInput.MouseClick("left", 2);
                    return $"Double-clicked {dx},{dy}";
                }
                return "ERROR: doubleclick requires x,y";

            case "rightclick":
                if (TryParseCoords(arg, out int rx, out int ry))
                {
                    NativeInput.MoveMouse(rx, ry);
                    Thread.Sleep(20);
                    NativeInput.MouseClick("right", 1);
                    return $"Right-clicked {rx},{ry}";
                }
                return "ERROR: rightclick requires x,y";

            case "type":
                NativeInput.TypeUnicode(StripQuotes(arg));
                return $"Typed {StripQuotes(arg).Length} chars";

            case "paste":
                NativeInput.TypeViaClipboard(StripQuotes(arg));
                return $"Pasted {StripQuotes(arg).Length} chars";

            case "hotkey":
                var keys = arg.Split('+', StringSplitOptions.RemoveEmptyEntries)
                              .Select(k => k.Trim().ToLowerInvariant()).ToArray();
                NativeInput.Hotkey(keys);
                return $"Pressed {string.Join("+", keys)}";

            case "wait":
                int ms = int.TryParse(arg, out var parsed) ? Math.Clamp(parsed, 10, 10000) : 300;
                Thread.Sleep(ms);
                return $"Waited {ms}ms";

            case "focus":
                var title = StripQuotes(arg);
                var hWnd = Win32.FindWindowByTitle(title);
                if (hWnd == nint.Zero) return $"NOT FOUND: '{title}'";
                Win32.FocusWindow(hWnd);
                activeWindow = hWnd;
                Thread.Sleep(150);
                return $"Focused: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";

            case "tab":
                var tabElem = UiAutomationHelper.FindElement(StripQuotes(arg));
                if (tabElem != null)
                {
                    UiAutomationHelper.Select(tabElem);
                    return $"Selected tab: {StripQuotes(arg)}";
                }
                return $"NOT FOUND: tab '{StripQuotes(arg)}'";

            case "ocr_click":
                return DoOcrClick(StripQuotes(arg), activeWindow);

            case "screenshot":
                return DoScreenshot(StripQuotes(arg), activeWindow);

            case "scroll":
                int amount = int.TryParse(arg, out var sa) ? sa : -3;
                NativeInput.MouseScroll(amount);
                return $"Scrolled {amount}";

            case "select_all":
                NativeInput.Hotkey("ctrl", "a");
                return "Selected all";

            default:
                return $"UNKNOWN: '{action}'";
        }
    }

    private static string DoClick(string arg, nint activeWindow)
    {
        // Check if it's coordinates: "123,456"
        if (TryParseCoords(arg, out int cx, out int cy))
        {
            NativeInput.MoveMouse(cx, cy);
            Thread.Sleep(20);
            NativeInput.MouseClick("left", 1);
            return $"Clicked {cx},{cy}";
        }

        // Text-based click: try UIAutomation first, then OCR
        var text = StripQuotes(arg);
        var windowTitle = activeWindow != nint.Zero ? Win32.GetWindowTitle(activeWindow) : "";

        var element = UiAutomationHelper.FindElement(text, windowTitle);
        if (element != null)
        {
            UiAutomationHelper.Invoke(element);
            return $"Clicked: \"{element.Current.Name}\" (UIA)";
        }

        // OCR fallback
        return DoOcrClick(text, activeWindow);
    }

    private static string DoOcrClick(string text, nint activeWindow)
    {
        // Always use foreground window for accurate coordinates
        nint hWnd = Win32.GetForegroundWindow();
        if (hWnd == nint.Zero)
            hWnd = activeWindow != nint.Zero ? activeWindow : nint.Zero;
        if (hWnd == nint.Zero) return $"NOT FOUND: '{text}' — no active window";

        Win32.GetWindowRect(hWnd, out var rect);
        int wx = rect.Left, wy = rect.Top, ww = rect.Width, wh = rect.Height;

        // Clamp to reasonable size and ensure positive dimensions
        if (ww <= 0 || wh <= 0) return $"NOT FOUND: '{text}' — invalid window rect ({wx},{wy} {ww}x{wh})";
        ww = Math.Min(ww, 4000);
        wh = Math.Min(wh, 3000);

        using var bmp = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(wx, wy, 0, 0, new Size(ww, wh));
        }

        var matches = OcrService.FindText(bmp, text, "en", wx, wy);
        if (matches.Count == 0) return $"NOT FOUND: '{text}' (OCR)";

        // Score matches: exact > starts with > contains. Prefer shorter text (closer to search term).
        var best = matches
            .OrderBy(m => m.Text.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0
                        : m.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
                        : m.Text.EndsWith(text, StringComparison.OrdinalIgnoreCase) ? 1
                        : 2)
            .ThenBy(m => Math.Abs(m.Text.Length - text.Length))
            .First();

        NativeInput.MoveMouse(best.CenterX, best.CenterY);
        Thread.Sleep(20);
        NativeInput.MouseClick("left", 1);
        return $"Clicked: \"{best.Text}\" @ {best.CenterX},{best.CenterY} (OCR)";
    }

    private static string DoScreenshot(string savePath, nint activeWindow)
    {
        nint hWnd = activeWindow != nint.Zero ? activeWindow : Win32.GetForegroundWindow();
        if (hWnd == nint.Zero) return "ERROR: no active window";

        Win32.GetWindowRect(hWnd, out var rect);
        int ww = Math.Min(rect.Width, 4000), wh = Math.Min(rect.Height, 3000);

        using var bmp = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(ww, wh));
        }

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(savePath))!);
        bmp.Save(savePath, ImageFormat.Png);
        return $"Screenshot saved: {ww}x{wh} → {savePath}";
    }

    // ─── Parsing Helpers ─────────────────────────────────────────────────────────

    private static (string action, string arg) ParseAction(string line)
    {
        // Handle quoted arguments: click "some text"
        int firstSpace = line.IndexOf(' ');
        if (firstSpace < 0) return (line, "");
        return (line[..firstSpace], line[(firstSpace + 1)..].Trim());
    }

    private static string StripQuotes(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s[1..^1];
        if (s.Length >= 2 && s[0] == '\'' && s[^1] == '\'') return s[1..^1];
        return s;
    }

    private static bool TryParseCoords(string s, out int x, out int y)
    {
        x = y = 0;
        s = s.Trim();
        var parts = s.Split(',');
        if (parts.Length != 2) return false;
        return int.TryParse(parts[0].Trim(), out x) && int.TryParse(parts[1].Trim(), out y);
    }
}
