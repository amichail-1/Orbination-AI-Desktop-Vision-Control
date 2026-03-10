using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using DesktopControlMcp.Models;
using DesktopControlMcp.Native;
using DesktopControlMcp.Services;
using ModelContextProtocol.Server;

namespace DesktopControlMcp.Tools;

[McpServerToolType]
public sealed class VisionTools
{
    private static SceneData? _cachedScene;
    private static DateTime _lastScanTime;
    private static readonly object _lock = new();

    private static SceneData RefreshScene(bool fullScan)
    {
        lock (_lock)
        {
            _cachedScene = fullScan ? DesktopScanner.ScanAll() : DesktopScanner.ScanWindowsOnly();
            _lastScanTime = DateTime.UtcNow;
            return _cachedScene;
        }
    }

    private static SceneData GetOrScan()
    {
        lock (_lock)
        {
            // Auto-refresh if cache is older than 30 seconds
            if (_cachedScene == null || (DateTime.UtcNow - _lastScanTime).TotalSeconds > 30)
                return RefreshScene(true);
            return _cachedScene;
        }
    }

    private static string CacheAge()
    {
        var age = (int)(DateTime.UtcNow - _lastScanTime).TotalSeconds;
        return $"[cache: {age}s ago]";
    }

    [McpServerTool(Name = "scan_desktop"), Description("Full desktop scan: screens, windows, UI elements, taskbar. Returns compact plain text.")]
    public static string ScanDesktop()
    {
        var scene = RefreshScene(true);
        var sb = new StringBuilder();
        sb.AppendLine($"Screens: {scene.Screens.Count} ({string.Join(", ", scene.Screens.Select(s => $"{s.Width}x{s.Height}{(s.IsPrimary ? "*" : "")}"))})");
        sb.AppendLine($"Windows ({scene.Windows.Count}):");
        foreach (var w in scene.Windows)
            sb.AppendLine($"  - {Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) [{w.Elements.Count} elements]");
        sb.AppendLine($"Taskbar apps: {string.Join(", ", scene.TaskbarElements.Where(t => t.Role == "taskbar-app").Select(t => t.Text.Split(" - ")[0]))}");
        sb.AppendLine($"Total: {scene.Summary.TotalElements} elements");
        return sb.ToString();
    }

    [McpServerTool(Name = "list_windows"), Description("List visible windows with titles and process names.")]
    public static string ListWindows()
    {
        var scene = GetOrScan();
        var sb = new StringBuilder();
        foreach (var w in scene.Windows)
            sb.AppendLine($"{Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) [{w.Elements.Count}] @ {w.Bounds.X},{w.Bounds.Y} {w.Bounds.Width}x{w.Bounds.Height}");
        sb.Append(CacheAge());
        return sb.ToString();
    }

    [McpServerTool(Name = "focus_window"), Description("Bring a window to the foreground by title.")]
    public static string FocusWindow(
        [Description("Part of window title (case-insensitive)")] string windowTitle)
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: no window matching '{windowTitle}'";
        Win32.FocusWindow(hWnd);
        Thread.Sleep(100);
        return $"Focused: {Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}";
    }

    [McpServerTool(Name = "get_window_details"), Description("Get window's UI elements. Use kind filter (input/button/tab-item/text) to reduce output.")]
    public static string GetWindowDetails(
        [Description("Part of window title (case-insensitive)")] string windowLabel,
        [Description("Filter by kind: input, button, tab-item, text, link, checkbox")] string kindFilter = "",
        [Description("Max elements (default 30)")] int limit = 30)
    {
        var scene = GetOrScan();
        var matches = scene.Windows
            .Where(w => Win32.StripInvisibleChars(w.Title).Contains(windowLabel, StringComparison.OrdinalIgnoreCase) ||
                        w.ProcessName.Contains(windowLabel, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count == 0) return $"NOT FOUND: '{windowLabel}'. Run scan_desktop to refresh.";

        var sb = new StringBuilder();
        foreach (var w in matches)
        {
            var elems = w.Elements.AsEnumerable();
            if (!string.IsNullOrEmpty(kindFilter))
                elems = elems.Where(e => e.Kind.Contains(kindFilter, StringComparison.OrdinalIgnoreCase));

            var list = elems.Take(limit).ToList();
            sb.AppendLine($"{Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) - {w.Elements.Count} total, showing {list.Count}:");
            foreach (var e in list)
                sb.AppendLine($"  [{e.Kind}] \"{e.Text}\" @ {e.Bounds.CenterX},{e.Bounds.CenterY}");
        }
        sb.Append(CacheAge());
        return sb.ToString();
    }

    [McpServerTool(Name = "find_element"), Description("Find UI element by text. Returns click coordinates.")]
    public static string FindElement(
        [Description("Text to search (case-insensitive)")] string text,
        [Description("Limit to window (title or process)")] string windowLabel = "",
        [Description("Max results (default 5)")] int limit = 5)
    {
        var scene = GetOrScan();
        var sb = new StringBuilder();
        int count = 0;
        bool filterByWindow = !string.IsNullOrEmpty(windowLabel);

        foreach (var win in scene.Windows)
        {
            if (filterByWindow &&
                !Win32.StripInvisibleChars(win.Title).Contains(windowLabel, StringComparison.OrdinalIgnoreCase) &&
                !win.ProcessName.Contains(windowLabel, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var elem in win.Elements)
            {
                if (!elem.Text.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                    !elem.AutomationId.Contains(text, StringComparison.OrdinalIgnoreCase))
                    continue;

                sb.AppendLine($"[{elem.Kind}] \"{elem.Text}\" @ {elem.Bounds.CenterX},{elem.Bounds.CenterY} ({win.ProcessName})");
                if (++count >= limit) break;
            }
            if (count >= limit) break;
        }

        if (!filterByWindow && count < limit)
        {
            foreach (var tb in scene.TaskbarElements)
            {
                if (!tb.Text.Contains(text, StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine($"[{tb.Role}] \"{tb.Text}\" @ {tb.Bounds.CenterX},{tb.Bounds.CenterY} (taskbar)");
                if (++count >= limit) break;
            }
        }

        if (count == 0) return $"NOT FOUND: '{text}'. Run scan_desktop to refresh.";
        return sb.ToString();
    }

    [McpServerTool(Name = "click_element"), Description("Find element by text and click it via UIAutomation.")]
    public static string ClickElement(
        [Description("Text/label to click")] string text,
        [Description("Limit to window")] string windowLabel = "")
    {
        var element = UiAutomationHelper.FindElement(text, windowLabel);
        if (element == null) return $"NOT FOUND: '{text}'";
        var name = element.Current.Name ?? "";
        UiAutomationHelper.Invoke(element);
        return $"Clicked: {name}";
    }

    [McpServerTool(Name = "type_in_element"), Description("Find input element and type text. Tries ValuePattern, then clipboard paste, then click+type.")]
    public static string TypeInElement(
        [Description("Text/label of input to find")] string elementText,
        [Description("Text to type")] string value,
        [Description("Limit to window")] string windowLabel = "")
    {
        // Strategy 1: UIAutomation ValuePattern
        var element = UiAutomationHelper.FindElement(elementText, windowLabel)
                      ?? UiAutomationHelper.FindElementByAutomationId(elementText, windowLabel);

        if (element != null)
        {
            bool success = UiAutomationHelper.SetValue(element, value);
            if (success) return $"Typed in: {element.Current.Name}";
        }

        // Strategy 2: Find input in cached scan, use clipboard paste
        var scene = GetOrScan();
        UiElement? inputElem = null;
        foreach (var win in scene.Windows)
        {
            if (!string.IsNullOrEmpty(windowLabel) &&
                !Win32.StripInvisibleChars(win.Title).Contains(windowLabel, StringComparison.OrdinalIgnoreCase) &&
                !win.ProcessName.Contains(windowLabel, StringComparison.OrdinalIgnoreCase))
                continue;

            inputElem = win.Elements.FirstOrDefault(e =>
                e.Kind == "input" && e.Text.Contains(elementText, StringComparison.OrdinalIgnoreCase));
            inputElem ??= win.Elements.FirstOrDefault(e => e.Kind == "input" && e.Bounds.Width > 50);
            if (inputElem != null) break;
        }

        if (inputElem != null)
        {
            if (!string.IsNullOrEmpty(windowLabel))
            {
                var hWnd = Win32.FindWindowByTitle(windowLabel);
                if (hWnd != nint.Zero) Win32.FocusWindow(hWnd);
                Thread.Sleep(100);
            }

            NativeInput.MoveMouse(inputElem.Bounds.CenterX, inputElem.Bounds.CenterY);
            Thread.Sleep(30);
            NativeInput.MouseClick("left", 1);
            Thread.Sleep(150);
            NativeInput.Hotkey("ctrl", "a");
            Thread.Sleep(50);

            // Use clipboard paste for reliability
            NativeInput.TypeViaClipboard(value);
            return $"Typed in: {inputElem.Text}";
        }

        return $"NOT FOUND: '{elementText}'. Run scan_desktop to refresh.";
    }

    [McpServerTool(Name = "select_tab"), Description("Select a browser tab by text via UIAutomation.")]
    public static string SelectTab(
        [Description("Tab text")] string tabText,
        [Description("Limit to window")] string windowLabel = "")
    {
        var element = UiAutomationHelper.FindElement(tabText, windowLabel);
        if (element == null) return $"NOT FOUND: tab '{tabText}'";
        UiAutomationHelper.Select(element);
        return $"Selected: {element.Current.Name}";
    }

    [McpServerTool(Name = "interact"), Description("Smart interaction: finds element, auto-detects type (button→click, input→type, tab→select, checkbox→toggle). One tool for all interactions.")]
    public static string Interact(
        [Description("Text/label of element")] string elementText,
        [Description("Value to type (for inputs). Leave empty for buttons/tabs.")] string value = "",
        [Description("Limit to window")] string windowLabel = "")
    {
        var element = UiAutomationHelper.FindElement(elementText, windowLabel);
        if (element == null) return $"NOT FOUND: '{elementText}'";

        var ct = element.Current.ControlType;
        var name = element.Current.Name ?? elementText;

        // Auto-detect action based on control type
        if (ct == ControlType.Edit || ct == ControlType.ComboBox)
        {
            if (string.IsNullOrEmpty(value)) return $"Found input '{name}' but no value provided.";
            bool typed = UiAutomationHelper.SetValue(element, value);
            return typed ? $"Typed '{value}' in: {name}" : $"FAILED to type in: {name}";
        }

        if (ct == ControlType.TabItem)
        {
            UiAutomationHelper.Select(element);
            return $"Selected tab: {name}";
        }

        if (ct == ControlType.CheckBox || ct == ControlType.RadioButton)
        {
            bool toggled = UiAutomationHelper.Toggle(element);
            if (!toggled) toggled = UiAutomationHelper.Invoke(element) || true;
            return $"Toggled: {name}";
        }

        // Default: invoke/click
        UiAutomationHelper.Invoke(element);
        return $"Clicked: {name}";
    }

    [McpServerTool(Name = "fill_form"), Description("Fill multiple form fields in one call. Provide field:value pairs as JSON object.")]
    public static string FillForm(
        [Description("JSON object of field:value pairs, e.g. {\"Email\":\"user@test.com\",\"Password\":\"123\"}")] string fieldsJson,
        [Description("Limit to window")] string windowLabel = "")
    {
        Dictionary<string, string>? fields;
        try { fields = JsonSerializer.Deserialize<Dictionary<string, string>>(fieldsJson); }
        catch { return "ERROR: Invalid JSON. Use format: {\"field1\":\"value1\",\"field2\":\"value2\"}"; }

        if (fields == null || fields.Count == 0) return "ERROR: No fields provided.";

        var sb = new StringBuilder();
        foreach (var (fieldName, fieldValue) in fields)
        {
            var element = UiAutomationHelper.FindElement(fieldName, windowLabel)
                          ?? UiAutomationHelper.FindElementByAutomationId(fieldName, windowLabel);

            if (element != null)
            {
                bool success = UiAutomationHelper.SetValue(element, fieldValue);
                sb.AppendLine(success ? $"OK: {fieldName}" : $"FAILED: {fieldName}");
            }
            else
            {
                sb.AppendLine($"NOT FOUND: {fieldName}");
            }
            Thread.Sleep(100);
        }
        return sb.ToString();
    }

    [McpServerTool(Name = "refresh_window"), Description("Re-scan only one window's elements (faster than full scan_desktop).")]
    public static string RefreshWindow(
        [Description("Window title to re-scan")] string windowTitle)
    {
        var elements = DesktopScanner.ScanSingleWindow(windowTitle);
        if (elements.Count == 0) return $"NOT FOUND or no elements: '{windowTitle}'";

        // Update cache
        lock (_lock)
        {
            if (_cachedScene != null)
            {
                var win = _cachedScene.Windows.FirstOrDefault(w =>
                    Win32.StripInvisibleChars(w.Title).Contains(windowTitle, StringComparison.OrdinalIgnoreCase) ||
                    w.ProcessName.Contains(windowTitle, StringComparison.OrdinalIgnoreCase));
                if (win != null)
                    win.Elements = elements;
            }
        }

        return $"Refreshed: {elements.Count} elements in '{windowTitle}'";
    }

    [McpServerTool(Name = "read_window_text"), Description("Read all visible text from a window. Great for reading web pages, emails, documents.")]
    public static string ReadWindowText(
        [Description("Window title")] string windowLabel,
        [Description("Max text items (default 50)")] int limit = 50)
    {
        var scene = GetOrScan();
        var sb = new StringBuilder();

        foreach (var w in scene.Windows)
        {
            if (!Win32.StripInvisibleChars(w.Title).Contains(windowLabel, StringComparison.OrdinalIgnoreCase) &&
                !w.ProcessName.Contains(windowLabel, StringComparison.OrdinalIgnoreCase))
                continue;

            var texts = w.Elements
                .Where(e => e.Kind == "text" && !string.IsNullOrWhiteSpace(e.Text))
                .Take(limit)
                .ToList();

            foreach (var t in texts)
                sb.AppendLine(t.Text);

            break;
        }

        if (sb.Length == 0) return $"No text found in '{windowLabel}'. Run scan_desktop to refresh.";
        return sb.ToString();
    }
}
