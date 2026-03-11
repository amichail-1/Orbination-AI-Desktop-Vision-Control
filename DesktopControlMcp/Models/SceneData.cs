namespace DesktopControlMcp.Models;

/// <summary>
/// Complete desktop scene snapshot — all coordinates in absolute screen space.
/// </summary>
public sealed class SceneData
{
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public List<ScreenInfo> Screens { get; set; } = [];
    public VirtualDesktopInfo VirtualDesktop { get; set; } = new();
    public List<WindowInfo> Windows { get; set; } = [];
    public List<TaskbarElement> TaskbarElements { get; set; } = [];
    public List<DesktopRegion> DesktopRegions { get; set; } = [];
    public SceneSummary Summary { get; set; } = new();
}

public sealed class ScreenInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsPrimary { get; set; }
}

public sealed class VirtualDesktopInfo
{
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class Bounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

public sealed class WindowInfo
{
    public string Handle { get; set; } = "";
    public string Title { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public int ProcessId { get; set; }
    public int ZOrder { get; set; }
    public Bounds Bounds { get; set; } = new();
    public List<UiElement> Elements { get; set; } = [];
    public int ElementCount => Elements.Count;

    /// <summary>Fraction of window area actually visible (0.0–1.0), considering occlusion by windows in front.</summary>
    public double VisibleFraction { get; set; } = 1.0;
    /// <summary>True if less than 15% of the window is visible (mostly hidden behind other windows).</summary>
    public bool Occluded { get; set; }
}

/// <summary>A region of the screen where the desktop background is visible (no window covering it).</summary>
public sealed class DesktopRegion
{
    public Bounds Bounds { get; set; } = new();
    /// <summary>Which screen index this region is on (-1 if spanning).</summary>
    public int ScreenIndex { get; set; } = -1;
    /// <summary>Area in pixels.</summary>
    public int AreaPixels { get; set; }
}

public sealed class UiElement
{
    public string Text { get; set; } = "";
    public string Kind { get; set; } = "";  // button, input, text, link, tab-item, image, etc.
    public Bounds Bounds { get; set; } = new();
    public string AutomationId { get; set; } = "";
    public string ClassName { get; set; } = "";
}

public sealed class TaskbarElement
{
    public string Text { get; set; } = "";
    public string Role { get; set; } = "";  // taskbar, start-button, search-box, taskbar-app, system-tray
    public Bounds Bounds { get; set; } = new();
    public string AutomationId { get; set; } = "";
}

public sealed class SceneSummary
{
    public int WindowCount { get; set; }
    public int TotalElements { get; set; }
    public int TaskbarElementCount { get; set; }
    public Dictionary<string, int> ElementsByKind { get; set; } = [];
}
