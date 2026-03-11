using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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

    [McpServerTool(Name = "scan_desktop"), Description("Full desktop scan: screens, windows (with visibility %), UI elements, desktop regions, taskbar. Returns compact plain text.")]
    public static string ScanDesktop()
    {
        var scene = RefreshScene(true);
        var sb = new StringBuilder();
        sb.AppendLine($"Screens: {scene.Screens.Count} ({string.Join(", ", scene.Screens.Select(s => $"{s.Width}x{s.Height}{(s.IsPrimary ? "*" : "")}"))})");
        sb.AppendLine($"Windows ({scene.Windows.Count}):");
        foreach (var w in scene.Windows)
        {
            var vis = w.VisibleFraction < 1.0 ? $" {w.VisibleFraction:P0} visible" : "";
            var occ = w.Occluded ? " [OCCLUDED]" : "";
            sb.AppendLine($"  - {Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) [{w.Elements.Count} elements]{vis}{occ}");
        }
        if (scene.DesktopRegions.Count > 0)
        {
            sb.AppendLine($"Desktop regions ({scene.DesktopRegions.Count} uncovered areas):");
            foreach (var r in scene.DesktopRegions)
                sb.AppendLine($"  - {r.Bounds.Width}x{r.Bounds.Height} @ {r.Bounds.X},{r.Bounds.Y} (screen {r.ScreenIndex})");
        }
        sb.AppendLine($"Taskbar apps: {string.Join(", ", scene.TaskbarElements.Where(t => t.Role == "taskbar-app").Select(t => t.Text.Split(" - ")[0]))}");
        sb.AppendLine($"Total: {scene.Summary.TotalElements} elements");
        return sb.ToString();
    }

    [McpServerTool(Name = "list_windows"), Description("List visible windows with titles, process names, and visibility percentage (occlusion detection).")]
    public static string ListWindows()
    {
        var scene = GetOrScan();
        var sb = new StringBuilder();
        foreach (var w in scene.Windows)
        {
            var vis = w.VisibleFraction < 1.0 ? $" ({w.VisibleFraction:P0} visible)" : "";
            var occ = w.Occluded ? " [OCCLUDED]" : "";
            sb.AppendLine($"{Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) [{w.Elements.Count}] @ {w.Bounds.X},{w.Bounds.Y} {w.Bounds.Width}x{w.Bounds.Height}{vis}{occ}");
        }
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

    [McpServerTool(Name = "click_element"), Description(
        "Find element by text and click it. Tries UIAutomation first (reliable, pattern-based), " +
        "then falls back to OCR text detection (works on web apps, dark themes, iframes). " +
        "Returns what was clicked and the method used.")]
    public static string ClickElement(
        [Description("Text/label to click")] string text,
        [Description("Limit to window")] string windowLabel = "")
    {
        // Strategy 1: UIAutomation (fast, reliable for native apps)
        var element = UiAutomationHelper.FindElement(text, windowLabel);
        if (element != null)
        {
            var name = element.Current.Name ?? "";
            UiAutomationHelper.Invoke(element);
            return $"Clicked: {name} (UIAutomation)";
        }

        // Strategy 2: OCR fallback (works on web apps, dark themes, custom UIs)
        var hWnd = string.IsNullOrEmpty(windowLabel)
            ? Win32.GetForegroundWindow()
            : Win32.FindWindowByTitle(windowLabel);

        if (hWnd == nint.Zero)
            return $"NOT FOUND: '{text}' — no matching window";

        Win32.GetWindowRect(hWnd, out var rect);
        int wx = rect.Left, wy = rect.Top, ww = rect.Width, wh = rect.Height;
        ww = Math.Min(ww, 4000);
        wh = Math.Min(wh, 3000);

        using var bmp = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(wx, wy, 0, 0, new Size(ww, wh));
        }

        var matches = OcrService.FindText(bmp, text, "en", wx, wy);
        if (matches.Count == 0)
            return $"NOT FOUND: '{text}' — not found by UIAutomation or OCR";

        // Pick best match: prefer exact match, then shortest text (most specific)
        var best = matches
            .OrderBy(m => m.Text.Equals(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(m => m.Text.Length)
            .First();

        NativeInput.MoveMouse(best.CenterX, best.CenterY);
        Thread.Sleep(30);
        NativeInput.MouseClick("left", 1);

        return $"Clicked: \"{best.Text}\" @ {best.CenterX},{best.CenterY} (OCR fallback)";
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

    [McpServerTool(Name = "scan_elements"), Description(
        "Numbered element map: captures a window, detects ALL interactive elements via UIAutomation + OCR, " +
        "numbers them on an annotated screenshot, and returns a numbered list with click coordinates. " +
        "Use this to understand complex UIs (web app dialogs, dark themes). " +
        "Then use mouse_click with the coordinates from the list.")]
    public static string ScanElements(
        [Description("Part of window title (case-insensitive)")] string windowTitle,
        [Description("File path to save annotated PNG with numbered elements")] string savePath,
        [Description("Max elements to show (default 60)")] int limit = 60,
        [Description("Max image width for AI readability (default 1920)")] int maxWidth = 1920)
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: no window matching '{windowTitle}'";

        Win32.FocusWindow(hWnd);
        Thread.Sleep(300);

        Win32.GetWindowRect(hWnd, out var rect);
        int wx = rect.Left, wy = rect.Top, ww = rect.Width, wh = rect.Height;
        ww = Math.Min(ww, 5000);
        wh = Math.Min(wh, 4000);

        // Capture screenshot
        using var bmp = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(wx, wy, 0, 0, new Size(ww, wh));
        }

        // Collect elements from both sources
        var numberedElements = new List<(int num, string label, string source, int cx, int cy, int x, int y, int w, int h)>();
        int idx = 1;

        // Source 1: UIAutomation elements
        var uiaElements = DesktopScanner.ScanSingleWindow(windowTitle);
        foreach (var elem in uiaElements)
        {
            if (idx > limit) break;
            if (string.IsNullOrWhiteSpace(elem.Text) && elem.Kind == "text") continue;
            var label = string.IsNullOrWhiteSpace(elem.Text) ? $"[{elem.Kind}]" : elem.Text;
            if (label.Length > 40) label = label[..40] + "...";
            numberedElements.Add((idx++, label, elem.Kind, elem.Bounds.CenterX, elem.Bounds.CenterY,
                elem.Bounds.X, elem.Bounds.Y, elem.Bounds.Width, elem.Bounds.Height));
        }

        // Source 2: OCR text (add only texts NOT already found by UIAutomation)
        var ocrLines = OcrService.RecognizeText(bmp, "en", wx, wy);
        foreach (var line in ocrLines)
        {
            if (idx > limit) break;
            // Skip if UIAutomation already found something at similar position
            bool duplicate = numberedElements.Any(e =>
                Math.Abs(e.cx - line.CenterX) < 30 && Math.Abs(e.cy - line.CenterY) < 15);
            if (duplicate) continue;

            var label = line.Text.Length > 40 ? line.Text[..40] + "..." : line.Text;
            numberedElements.Add((idx++, label, "ocr", line.CenterX, line.CenterY,
                line.X, line.Y, line.Width, line.Height));
        }

        // Draw numbered annotations on the screenshot
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var numFont = new Font("Segoe UI", 10f, FontStyle.Bold);
            var labelFont = new Font("Segoe UI", 8f, FontStyle.Regular);

            foreach (var elem in numberedElements)
            {
                // Convert to bitmap-local coords
                int ex = elem.x - wx;
                int ey = elem.y - wy;
                if (ex < 0 || ey < 0 || ex + elem.w > ww || ey + elem.h > wh) continue;

                // Color: green for UIAutomation, cyan for OCR
                var color = elem.source == "ocr" ? Color.FromArgb(200, 0, 200, 255) : Color.FromArgb(200, 0, 200, 80);
                using var pen = new Pen(color, 2f);
                g.DrawRectangle(pen, ex, ey, elem.w, elem.h);

                // Draw number badge
                var numText = elem.num.ToString();
                var numSize = g.MeasureString(numText, numFont);
                float nx = ex - 2;
                float ny = ey - numSize.Height - 2;
                if (ny < 0) ny = ey + elem.h + 2;

                using var bgBrush = new SolidBrush(Color.FromArgb(220, 255, 50, 50));
                g.FillRectangle(bgBrush, nx, ny, numSize.Width + 6, numSize.Height + 2);
                using var numBrush = new SolidBrush(Color.White);
                g.DrawString(numText, numFont, numBrush, nx + 3, ny + 1);
            }

            numFont.Dispose();
            labelFont.Dispose();
        }

        // Resize for AI readability
        float scale = 1f;
        Bitmap output;
        if (ww > maxWidth)
        {
            scale = (float)ww / maxWidth;
            int newHeight = (int)(wh / scale);
            output = new Bitmap(maxWidth, newHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, maxWidth, newHeight);
            }
        }
        else
        {
            output = (Bitmap)bmp.Clone();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
        output.Save(savePath, ImageFormat.Png);

        // Build numbered list
        var sb = new StringBuilder();
        sb.AppendLine($"Element map: {numberedElements.Count} elements ({output.Width}x{output.Height})");
        sb.AppendLine($"Window: \"{Win32.StripInvisibleChars(Win32.GetWindowTitle(hWnd))}\"");
        sb.AppendLine($"Region: x={wx}, y={wy}, w={ww}, h={wh}");
        if (scale > 1f)
            sb.AppendLine($"Scale: {scale:F2} — real_x = {wx} + (image_x * {scale:F2}), real_y = {wy} + (image_y * {scale:F2})");
        sb.AppendLine($"───────────────────────────────");

        foreach (var elem in numberedElements)
        {
            var src = elem.source == "ocr" ? " [OCR]" : $" [{elem.source}]";
            sb.AppendLine($"  #{elem.num}: \"{elem.label}\" @ {elem.cx},{elem.cy}{src}");
        }

        sb.AppendLine($"───────────────────────────────");
        sb.AppendLine($"Use: mouse_click x={numberedElements.FirstOrDefault().cx} y={numberedElements.FirstOrDefault().cy}");

        output.Dispose();
        return sb.ToString();
    }

    [McpServerTool(Name = "analyze_desktop"), Description(
        "Analyze desktop layout: which windows are truly visible vs occluded (hidden behind others), " +
        "and which screen areas show the desktop background (no window covering them). " +
        "Useful for understanding the real state of the user's screen.")]
    public static string AnalyzeDesktop()
    {
        var scene = RefreshScene(true);
        var sb = new StringBuilder();

        // Screen info
        sb.AppendLine($"═══ Desktop Analysis ═══");
        sb.AppendLine($"Screens: {scene.Screens.Count}");
        foreach (var s in scene.Screens)
            sb.AppendLine($"  [{s.Index}] {s.Width}x{s.Height} @ {s.X},{s.Y}{(s.IsPrimary ? " (primary)" : "")}");

        // Window visibility analysis
        var visibleWindows = scene.Windows.Where(w => !w.Occluded).ToList();
        var occludedWindows = scene.Windows.Where(w => w.Occluded).ToList();

        sb.AppendLine();
        sb.AppendLine($"═══ Visible Windows ({visibleWindows.Count}) ═══");
        foreach (var w in visibleWindows.OrderBy(w => w.ZOrder))
        {
            var pct = w.VisibleFraction < 1.0 ? $" ({w.VisibleFraction:P0} visible)" : " (fully visible)";
            sb.AppendLine($"  z{w.ZOrder}: {Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) {w.Bounds.Width}x{w.Bounds.Height} @ {w.Bounds.X},{w.Bounds.Y}{pct}");
        }

        if (occludedWindows.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"═══ Occluded Windows ({occludedWindows.Count}) — hidden behind others ═══");
            foreach (var w in occludedWindows.OrderBy(w => w.ZOrder))
                sb.AppendLine($"  z{w.ZOrder}: {Win32.StripInvisibleChars(w.Title)} ({w.ProcessName}) ({w.VisibleFraction:P0} visible)");
        }

        // Desktop regions
        sb.AppendLine();
        sb.AppendLine($"═══ Uncovered Desktop Regions ({scene.DesktopRegions.Count}) ═══");
        if (scene.DesktopRegions.Count == 0)
        {
            sb.AppendLine("  No uncovered desktop — all screen space is covered by windows.");
        }
        else
        {
            long totalDesktopArea = 0;
            foreach (var r in scene.DesktopRegions.OrderByDescending(r => r.AreaPixels))
            {
                totalDesktopArea += r.AreaPixels;
                sb.AppendLine($"  {r.Bounds.Width}x{r.Bounds.Height} @ {r.Bounds.X},{r.Bounds.Y} (screen [{r.ScreenIndex}], {r.AreaPixels:N0} px²)");
            }

            long totalScreenArea = scene.Screens.Sum(s => (long)s.Width * s.Height);
            double desktopPct = totalScreenArea > 0 ? (double)totalDesktopArea / totalScreenArea : 0;
            sb.AppendLine($"  Total uncovered: {desktopPct:P1} of all screen space");
        }

        return sb.ToString();
    }
}
