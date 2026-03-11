using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using DesktopControlMcp.Models;
using DesktopControlMcp.Services;
using ModelContextProtocol.Server;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DesktopControlMcp.Tools;

[McpServerToolType]
public sealed class ScreenTools
{
    // Color scheme for different element kinds (inspired by desktopvisionpro)
    private static readonly Dictionary<string, Color> KindColors = new()
    {
        ["button"] = Color.FromArgb(180, 0, 200, 80),
        ["input"] = Color.FromArgb(180, 0, 120, 255),
        ["text"] = Color.FromArgb(120, 200, 200, 200),
        ["link"] = Color.FromArgb(180, 0, 200, 255),
        ["tab-item"] = Color.FromArgb(180, 255, 165, 0),
        ["checkbox"] = Color.FromArgb(180, 255, 0, 200),
        ["radio"] = Color.FromArgb(180, 255, 0, 200),
        ["combo-box"] = Color.FromArgb(180, 200, 100, 255),
        ["image"] = Color.FromArgb(100, 255, 255, 0),
        ["menu-item"] = Color.FromArgb(180, 255, 100, 100),
        ["list-item"] = Color.FromArgb(140, 100, 200, 255),
        ["tree-item"] = Color.FromArgb(140, 100, 200, 255),
        ["slider"] = Color.FromArgb(180, 255, 200, 0),
        ["document"] = Color.FromArgb(80, 100, 100, 255),
    };

    [McpServerTool(Name = "get_screen_info"), Description("Get information about all connected monitors (position, size, primary status).")]
    public static string GetScreenInfo()
    {
        var sb = new StringBuilder();
        var screens = System.Windows.Forms.Screen.AllScreens;
        sb.AppendLine($"Monitors: {screens.Length}");
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            sb.AppendLine($"  [{i}] {s.Bounds.Width}x{s.Bounds.Height} @ {s.Bounds.X},{s.Bounds.Y}{(s.Primary ? " (primary)" : "")}");
        }
        return sb.ToString();
    }

    [McpServerTool(Name = "screenshot_to_file"), Description("Take a full screenshot across all monitors and save to a PNG file.")]
    public static string ScreenshotToFile(
        [Description("File path to save the PNG screenshot")] string savePath)
    {
        var vx = NativeInput.GetSystemMetrics(NativeInput.SM_XVIRTUALSCREEN);
        var vy = NativeInput.GetSystemMetrics(NativeInput.SM_YVIRTUALSCREEN);
        var vw = NativeInput.GetSystemMetrics(NativeInput.SM_CXVIRTUALSCREEN);
        var vh = NativeInput.GetSystemMetrics(NativeInput.SM_CYVIRTUALSCREEN);

        using var bmp = new Bitmap(vw, vh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(vx, vy, 0, 0, new Size(vw, vh));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
        bmp.Save(savePath, ImageFormat.Png);

        return $"Saved {vw}x{vh} screenshot to {savePath}";
    }

    [McpServerTool(Name = "screenshot_region"), Description("Take a screenshot of a specific screen region and save to file.")]
    public static string ScreenshotRegion(
        [Description("Left edge X coordinate")] int x,
        [Description("Top edge Y coordinate")] int y,
        [Description("Width of capture region")] int width,
        [Description("Height of capture region")] int height,
        [Description("File path to save the PNG screenshot")] string savePath)
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
        bmp.Save(savePath, ImageFormat.Png);

        return $"Saved {width}x{height} screenshot to {savePath}";
    }

    [McpServerTool(Name = "screenshot_window"), Description(
        "Capture a specific window by title using PrintWindow API. " +
        "Works even when the window is partially obscured by other windows. " +
        "Optionally resizes to AI-readable dimensions with coordinate mapping.")]
    public static string ScreenshotWindow(
        [Description("Part of window title to capture (case-insensitive)")] string windowTitle,
        [Description("File path to save the PNG screenshot")] string savePath,
        [Description("Max width of output image (default 1920, 0=no resize)")] int maxWidth = 1920)
    {
        var hWnd = Native.Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: no window matching '{windowTitle}'";

        Native.Win32.GetWindowRect(hWnd, out var rect);
        int ww = rect.Width, wh = rect.Height;
        if (ww <= 0 || wh <= 0) return "ERROR: window has empty bounds";

        using var bmp = CaptureWindowBitmap(hWnd, ww, wh, out var method);
        if (bmp == null) return "ERROR: failed to capture window";

        // Resize if needed
        float scale = 1f;
        Bitmap output;
        if (maxWidth > 0 && ww > maxWidth)
        {
            scale = (float)ww / maxWidth;
            int newHeight = (int)(wh / scale);
            output = new Bitmap(maxWidth, newHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, maxWidth, newHeight);
            }
        }
        else
        {
            output = (Bitmap)bmp.Clone();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
        output.Save(savePath, ImageFormat.Png);

        var sb = new StringBuilder();
        sb.AppendLine($"Window screenshot saved: {output.Width}x{output.Height} (method: {method})");
        sb.AppendLine($"Window: \"{Native.Win32.StripInvisibleChars(Native.Win32.GetWindowTitle(hWnd))}\"");
        sb.AppendLine($"Region: x={rect.Left}, y={rect.Top}, w={ww}, h={wh}");
        if (scale > 1f)
        {
            sb.AppendLine($"Scale: {scale:F2}");
            sb.AppendLine($"Formula: real_x = {rect.Left} + (image_x * {scale:F2}), real_y = {rect.Top} + (image_y * {scale:F2})");
        }

        output.Dispose();
        return sb.ToString();
    }

    /// <summary>
    /// Capture a window using PrintWindow (works even when obscured), with BitBlt fallback.
    /// </summary>
    private static Bitmap? CaptureWindowBitmap(nint hWnd, int width, int height, out string method)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        method = "printwindow";

        try
        {
            bool ok = Native.Win32.PrintWindow(hWnd, hdc, Native.Win32.PW_RENDERFULLCONTENT)
                   || Native.Win32.PrintWindow(hWnd, hdc, 0);

            if (!ok)
            {
                method = "bitblt";
                var windowDc = Native.Win32.GetWindowDC(hWnd);
                if (windowDc == nint.Zero)
                {
                    bmp.Dispose();
                    return null;
                }
                try
                {
                    ok = Native.Win32.BitBlt(hdc, 0, 0, width, height, windowDc, 0, 0, Native.Win32.SRCCOPY);
                }
                finally
                {
                    Native.Win32.ReleaseDC(hWnd, windowDc);
                }

                if (!ok)
                {
                    bmp.Dispose();
                    return null;
                }
            }
        }
        finally
        {
            g.ReleaseHdc(hdc);
        }

        return bmp;
    }

    [McpServerTool(Name = "screenshot_annotated"), Description(
        "Screenshot a window with colored bounding boxes drawn around all detected UI elements. " +
        "Resizes to AI-readable dimensions and returns a scale factor for coordinate mapping. " +
        "Use: real_x = region_x + (image_x * scale), real_y = region_y + (image_y * scale). " +
        "Colors: green=button, blue=input, orange=tab, cyan=link, pink=checkbox, red=menu-item, gray=text.")]
    public static string ScreenshotAnnotated(
        [Description("Part of window title to capture (case-insensitive)")] string windowTitle,
        [Description("File path to save annotated PNG")] string savePath,
        [Description("Max width of output image for AI readability (default 1920)")] int maxWidth = 1920,
        [Description("Filter by element kind (empty=all). Options: button, input, text, link, tab-item, checkbox")] string kindFilter = "",
        [Description("Draw text labels on boxes (default true)")] bool showLabels = true)
    {
        var hWnd = Native.Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: no window matching '{windowTitle}'";

        Native.Win32.FocusWindow(hWnd);
        Thread.Sleep(300);

        Native.Win32.GetWindowRect(hWnd, out var rect);
        int wx = rect.Left, wy = rect.Top, ww = rect.Width, wh = rect.Height;
        ww = Math.Min(ww, 5000);
        wh = Math.Min(wh, 4000);

        // Capture screenshot using PrintWindow (works even when obscured)
        using var bmp = CaptureWindowBitmap(hWnd, ww, wh, out _)
                        ?? new Bitmap(ww, wh, PixelFormat.Format32bppArgb);

        // Get UI elements for this window
        var elements = DesktopScanner.ScanSingleWindow(windowTitle);

        // Draw bounding boxes on the screenshot
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var labelFont = new Font("Segoe UI", 9f, FontStyle.Bold);

            foreach (var elem in elements)
            {
                if (!string.IsNullOrEmpty(kindFilter) &&
                    !elem.Kind.Contains(kindFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Convert absolute screen coords to bitmap-local coords
                int ex = elem.Bounds.X - wx;
                int ey = elem.Bounds.Y - wy;
                int ew = elem.Bounds.Width;
                int eh = elem.Bounds.Height;

                if (ex < 0 || ey < 0 || ex + ew > ww || ey + eh > wh) continue;
                if (ew < 4 || eh < 4) continue;

                var color = KindColors.GetValueOrDefault(elem.Kind, Color.FromArgb(150, 255, 255, 255));
                using var pen = new Pen(color, 2f);
                g.DrawRectangle(pen, ex, ey, ew, eh);

                if (showLabels && !string.IsNullOrEmpty(elem.Text))
                {
                    var labelText = elem.Text.Length > 30 ? elem.Text[..30] + "..." : elem.Text;
                    var label = $"[{elem.Kind}] {labelText}";
                    var labelSize = g.MeasureString(label, labelFont);

                    // Draw label background
                    float lx = ex;
                    float ly = ey - labelSize.Height - 2;
                    if (ly < 0) ly = ey + eh + 2; // put below if no room above

                    using var bgBrush = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
                    g.FillRectangle(bgBrush, lx, ly, labelSize.Width + 4, labelSize.Height);
                    using var textBrush = new SolidBrush(color);
                    g.DrawString(label, labelFont, textBrush, lx + 2, ly);
                }
            }

            labelFont.Dispose();
        }

        // Calculate scale factor and resize
        float scale = 1f;
        Bitmap output;
        if (ww > maxWidth)
        {
            scale = (float)ww / maxWidth;
            int newHeight = (int)(wh / scale);
            output = new Bitmap(maxWidth, newHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(output))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(bmp, 0, 0, maxWidth, newHeight);
            }
        }
        else
        {
            output = (Bitmap)bmp.Clone();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
        output.Save(savePath, ImageFormat.Png);

        var sb = new StringBuilder();
        sb.AppendLine($"Annotated screenshot saved: {output.Width}x{output.Height}");
        sb.AppendLine($"Window: \"{Native.Win32.StripInvisibleChars(Native.Win32.GetWindowTitle(hWnd))}\"");
        sb.AppendLine($"Region: x={wx}, y={wy}, w={ww}, h={wh}");
        sb.AppendLine($"Scale: {scale:F2} (multiply image coords by this to get real screen coords)");
        sb.AppendLine($"Formula: real_x = {wx} + (image_x * {scale:F2}), real_y = {wy} + (image_y * {scale:F2})");
        sb.AppendLine($"Elements drawn: {elements.Count(e => string.IsNullOrEmpty(kindFilter) || e.Kind.Contains(kindFilter, StringComparison.OrdinalIgnoreCase))}");

        output.Dispose();
        return sb.ToString();
    }

    // ─── OCR Tools (using shared OcrService) ───────────────────────────────────

    [McpServerTool(Name = "ocr_screen_region"), Description("Capture a screen region and run Windows OCR on it. Returns recognized text with positions. Auto-enhances dark themes. Works on any app including custom-rendered UIs.")]
    public static string OcrScreenRegion(
        [Description("Left edge X coordinate")] int x,
        [Description("Top edge Y coordinate")] int y,
        [Description("Width of capture region")] int width,
        [Description("Height of capture region")] int height,
        [Description("Language code (default: en, options: en, el, de, fr, es, it, etc.)")] string language = "en")
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        }

        return FormatOcrResults(OcrService.RecognizeText(bmp, language, x, y));
    }

    [McpServerTool(Name = "ocr_window"), Description("Run OCR on an entire window. Auto-enhances dark themes. Returns all visible text with click coordinates.")]
    public static string OcrWindow(
        [Description("Part of window title (case-insensitive)")] string windowTitle,
        [Description("Language code (default: en)")] string language = "en")
    {
        var hWnd = Native.Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return $"NOT FOUND: no window matching '{windowTitle}'";

        Native.Win32.FocusWindow(hWnd);
        Thread.Sleep(200);

        Native.Win32.GetWindowRect(hWnd, out var rect);
        int wx = rect.Left, wy = rect.Top, ww = rect.Width, wh = rect.Height;
        ww = Math.Min(ww, 4000);
        wh = Math.Min(wh, 3000);

        using var bmp = new Bitmap(ww, wh, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(wx, wy, 0, 0, new Size(ww, wh));
        }

        return FormatOcrResults(OcrService.RecognizeText(bmp, language, wx, wy));
    }

    [McpServerTool(Name = "ocr_find_text"), Description("Use OCR to find specific text on screen and return its click coordinates. Auto-enhances dark themes.")]
    public static string OcrFindText(
        [Description("Text to search for (case-insensitive)")] string searchText,
        [Description("Left edge X of search region")] int x,
        [Description("Top edge Y of search region")] int y,
        [Description("Width of search region")] int width,
        [Description("Height of search region")] int height,
        [Description("Language code (default: en)")] string language = "en")
    {
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new Size(width, height));
        }

        var matches = OcrService.FindText(bmp, searchText, language, x, y);
        if (matches.Count == 0) return $"NOT FOUND: '{searchText}' not visible in region";

        var sb = new StringBuilder();
        foreach (var m in matches)
            sb.AppendLine($"FOUND: \"{m.Text}\" @ {m.CenterX},{m.CenterY}");
        return sb.ToString();
    }

    private static string FormatOcrResults(List<OcrService.OcrTextLine> lines)
    {
        if (lines.Count == 0) return "OCR result (0 lines)";
        var sb = new StringBuilder();
        sb.AppendLine($"OCR result ({lines.Count} lines):");
        foreach (var line in lines)
            sb.AppendLine($"  \"{line.Text}\" @ {line.CenterX},{line.CenterY}");
        return sb.ToString();
    }
}
