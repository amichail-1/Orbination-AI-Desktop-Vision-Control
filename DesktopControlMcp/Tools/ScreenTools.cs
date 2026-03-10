using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ModelContextProtocol.Server;

namespace DesktopControlMcp.Tools;

[McpServerToolType]
public sealed class ScreenTools
{
    [McpServerTool(Name = "get_screen_info"), Description("Get information about all connected monitors (position, size, primary status).")]
    public static string GetScreenInfo()
    {
        var sb = new System.Text.StringBuilder();
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
}
