using System.ComponentModel;
using DesktopControlMcp.Native;
using ModelContextProtocol.Server;

namespace DesktopControlMcp.Tools;

[McpServerToolType]
public sealed class MouseTools
{
    [McpServerTool(Name = "mouse_get_position"), Description("Get the current mouse cursor position.")]
    public static string GetPosition()
    {
        var pt = NativeInput.GetCursorPosition();
        return $"{pt.X},{pt.Y}";
    }

    [McpServerTool(Name = "mouse_move"), Description("Move the mouse cursor to absolute screen coordinates. Works across all monitors.")]
    public static string Move(
        [Description("X coordinate (can be negative for left-side monitors)")] int x,
        [Description("Y coordinate")] int y)
    {
        NativeInput.MoveMouse(x, y);
        return $"Moved to {x},{y}";
    }

    [McpServerTool(Name = "mouse_click"), Description("Click the mouse at the given screen coordinates. Automatically focuses the window at that position first.")]
    public static string Click(
        [Description("X coordinate to click")] int x,
        [Description("Y coordinate to click")] int y,
        [Description("Mouse button: left, right, or middle")] string button = "left",
        [Description("Number of clicks (1=single, 2=double)")] int clicks = 1)
    {
        Win32.FocusWindowAt(x, y);
        Thread.Sleep(100);

        NativeInput.MoveMouse(x, y);
        Thread.Sleep(30);
        NativeInput.MouseClick(button, clicks);
        return $"Clicked {button} at {x},{y}";
    }

    [McpServerTool(Name = "mouse_drag"), Description("Drag the mouse from one position to another.")]
    public static string Drag(
        [Description("Starting X")] int startX,
        [Description("Starting Y")] int startY,
        [Description("Ending X")] int endX,
        [Description("Ending Y")] int endY,
        [Description("Drag duration in milliseconds")] int durationMs = 500)
    {
        NativeInput.MoveMouse(startX, startY);
        Thread.Sleep(50);

        var down = new NativeInput.INPUT[]
        {
            new() { Type = NativeInput.INPUT_MOUSE, U = new NativeInput.INPUTUNION { mi = new NativeInput.MOUSEINPUT { dwFlags = NativeInput.MOUSEEVENTF_LEFTDOWN } } }
        };
        NativeInput.SendInput(1, down, System.Runtime.InteropServices.Marshal.SizeOf<NativeInput.INPUT>());

        int steps = Math.Max(10, durationMs / 16);
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            int cx = startX + (int)((endX - startX) * t);
            int cy = startY + (int)((endY - startY) * t);
            NativeInput.MoveMouse(cx, cy);
            Thread.Sleep(durationMs / steps);
        }

        var up = new NativeInput.INPUT[]
        {
            new() { Type = NativeInput.INPUT_MOUSE, U = new NativeInput.INPUTUNION { mi = new NativeInput.MOUSEINPUT { dwFlags = NativeInput.MOUSEEVENTF_LEFTUP } } }
        };
        NativeInput.SendInput(1, up, System.Runtime.InteropServices.Marshal.SizeOf<NativeInput.INPUT>());

        return $"Dragged {startX},{startY} -> {endX},{endY}";
    }

    [McpServerTool(Name = "mouse_scroll"), Description("Scroll the mouse wheel. Positive = up, negative = down.")]
    public static string Scroll(
        [Description("Scroll increments (positive=up, negative=down)")] int amount,
        [Description("Optional X coordinate")] int? x = null,
        [Description("Optional Y coordinate")] int? y = null)
    {
        if (x.HasValue && y.HasValue)
            NativeInput.MoveMouse(x.Value, y.Value);
        NativeInput.MouseScroll(amount);
        return $"Scrolled {amount}";
    }
}
