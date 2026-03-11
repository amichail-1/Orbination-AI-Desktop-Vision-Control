using System.Diagnostics;
using System.Windows.Automation;
using DesktopControlMcp.Models;
using DesktopControlMcp.Native;

namespace DesktopControlMcp.Services;

/// <summary>
/// Native Windows desktop scanner using Win32 EnumWindows + UIAutomation.
/// Uses CacheRequest for batch property fetching (single cross-process call)
/// and filtered conditions to skip irrelevant elements.
/// </summary>
public static class DesktopScanner
{
    private static readonly HashSet<string> IgnoredClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Button"];
    private static readonly HashSet<string> IgnoredTitles = ["Program Manager", "NVIDIA GeForce Overlay"];

    // Pre-built condition: only control types we care about
    private static readonly Condition InterestingControlTypes = new OrCondition(
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Image),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Hyperlink),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TreeItem),
        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Slider)
    );

    public static SceneData ScanAll()
    {
        var scene = new SceneData();
        scene.Screens = GetScreens();
        scene.VirtualDesktop = GetVirtualDesktop();
        scene.Windows = GetWindows();
        scene.TaskbarElements = GetTaskbarElements();
        AnalyzeWindowOcclusion(scene);
        scene.DesktopRegions = FindDesktopRegions(scene);
        scene.Summary = BuildSummary(scene);
        return scene;
    }

    public static SceneData ScanWindowsOnly()
    {
        var scene = new SceneData();
        scene.Screens = GetScreens();
        scene.VirtualDesktop = GetVirtualDesktop();
        scene.Windows = EnumerateWindows(includeElements: false);
        AnalyzeWindowOcclusion(scene);
        scene.DesktopRegions = FindDesktopRegions(scene);
        scene.Summary = BuildSummary(scene);
        return scene;
    }

    /// <summary>
    /// Scan only a single window's elements (fast partial refresh).
    /// </summary>
    public static List<UiElement> ScanSingleWindow(string windowTitle)
    {
        var hWnd = Win32.FindWindowByTitle(windowTitle);
        if (hWnd == nint.Zero) return [];
        Win32.GetWindowRect(hWnd, out var rect);
        var bounds = new Bounds { X = rect.Left, Y = rect.Top, Width = rect.Width, Height = rect.Height };
        return GetWindowElements(hWnd, bounds);
    }

    // ─── Screens ────────────────────────────────────────────────────────────────

    public static List<ScreenInfo> GetScreens()
    {
        return System.Windows.Forms.Screen.AllScreens.Select((s, i) => new ScreenInfo
        {
            Index = i,
            Name = s.DeviceName,
            X = s.Bounds.X,
            Y = s.Bounds.Y,
            Width = s.Bounds.Width,
            Height = s.Bounds.Height,
            IsPrimary = s.Primary,
        }).ToList();
    }

    public static VirtualDesktopInfo GetVirtualDesktop()
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        return new VirtualDesktopInfo
        {
            Left = vs.Left,
            Top = vs.Top,
            Width = vs.Width,
            Height = vs.Height,
        };
    }

    // ─── Windows ────────────────────────────────────────────────────────────────

    public static List<WindowInfo> GetWindows() => EnumerateWindows(includeElements: true);

    private static List<WindowInfo> EnumerateWindows(bool includeElements)
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        var windows = new List<WindowInfo>();
        int zOrder = 0;

        Win32.EnumWindows((hWnd, _) =>
        {
            zOrder++;

            if (!Win32.IsWindowVisible(hWnd)) return true;
            if (Win32.IsIconic(hWnd)) return true;

            if (!Win32.GetWindowRect(hWnd, out var rect)) return true;
            if (rect.Width < 80 || rect.Height < 40) return true;

            var title = Win32.GetWindowTitle(hWnd);
            var className = Win32.GetWindowClassName(hWnd);

            if (Win32.IsCloaked(hWnd)) return true;
            if (IgnoredClasses.Contains(className)) return true;
            if (IgnoredTitles.Contains(title)) return true;
            if (string.IsNullOrWhiteSpace(title)) return true;

            var exStyle = Win32.GetExStyle(hWnd);
            if ((exStyle & Win32.WS_EX_TOOLWINDOW) != 0) return true;
            if ((exStyle & Win32.WS_EX_NOACTIVATE) != 0) return true;

            if (rect.Right <= vs.Left || rect.Left >= vs.Right ||
                rect.Bottom <= vs.Top || rect.Top >= vs.Bottom)
                return true;

            Win32.GetWindowThreadProcessId(hWnd, out var pid);
            string processName = "";
            try { processName = Process.GetProcessById((int)pid).ProcessName; } catch { }

            var win = new WindowInfo
            {
                Handle = $"0x{hWnd:X}",
                Title = title,
                ClassName = className,
                ProcessName = processName,
                ProcessId = (int)pid,
                ZOrder = zOrder,
                Bounds = new Bounds
                {
                    X = rect.Left,
                    Y = rect.Top,
                    Width = rect.Width,
                    Height = rect.Height,
                },
            };

            if (includeElements)
            {
                win.Elements = GetWindowElements(hWnd, win.Bounds);
            }

            windows.Add(win);
            return true;
        }, nint.Zero);

        return windows;
    }

    // ─── UI Elements (UIAutomation with CacheRequest) ────────────────────────────

    private static List<UiElement> GetWindowElements(nint hWnd, Bounds windowBounds)
    {
        var elements = new List<UiElement>();
        try
        {
            var windowElement = AutomationElement.FromHandle(hWnd);
            if (windowElement == null) return elements;

            // Use CacheRequest to batch-fetch all properties in a single cross-process call
            var cacheRequest = new CacheRequest();
            cacheRequest.Add(AutomationElement.NameProperty);
            cacheRequest.Add(AutomationElement.BoundingRectangleProperty);
            cacheRequest.Add(AutomationElement.ControlTypeProperty);
            cacheRequest.Add(AutomationElement.AutomationIdProperty);
            cacheRequest.Add(AutomationElement.ClassNameProperty);
            cacheRequest.TreeFilter = Automation.ControlViewCondition;

            using (cacheRequest.Activate())
            {
                // Use filtered condition instead of TrueCondition — much faster
                var all = windowElement.FindAll(TreeScope.Descendants, InterestingControlTypes);
                for (int i = 0; i < all.Count; i++)
                {
                    try
                    {
                        var node = all[i];
                        // Use .Cached instead of .Current — no cross-process calls!
                        var nodeRect = node.Cached.BoundingRectangle;
                        if (nodeRect.Width <= 0 || nodeRect.Height <= 0) continue;
                        if (nodeRect.Width > windowBounds.Width || nodeRect.Height > windowBounds.Height) continue;

                        var overlapW = Math.Min(nodeRect.Right, windowBounds.X + windowBounds.Width) - Math.Max(nodeRect.Left, windowBounds.X);
                        var overlapH = Math.Min(nodeRect.Bottom, windowBounds.Y + windowBounds.Height) - Math.Max(nodeRect.Top, windowBounds.Y);
                        if (overlapW <= 1 || overlapH <= 1) continue;

                        var kind = GetElementKind(node.Cached.ControlType);
                        if (kind == null) continue;

                        var name = node.Cached.Name;
                        if (string.IsNullOrWhiteSpace(name) && kind == "text") continue;

                        int bx = (int)Math.Round(nodeRect.X);
                        int by = (int)Math.Round(nodeRect.Y);
                        int bw = Math.Max(0, (int)Math.Round(nodeRect.Width));
                        int bh = Math.Max(0, (int)Math.Round(nodeRect.Height));
                        if (bw < 6 || bh < 6) continue;

                        elements.Add(new UiElement
                        {
                            Text = name ?? "",
                            Kind = kind,
                            Bounds = new Bounds { X = bx, Y = by, Width = bw, Height = bh },
                            AutomationId = node.Cached.AutomationId ?? "",
                            ClassName = node.Cached.ClassName ?? "",
                        });
                    }
                    catch { }
                }
            }
        }
        catch { }

        return elements;
    }

    private static string? GetElementKind(ControlType ct)
    {
        if (ct == ControlType.Button) return "button";
        if (ct == ControlType.Edit) return "input";
        if (ct == ControlType.Document) return "document";
        if (ct == ControlType.Text) return "text";
        if (ct == ControlType.Image) return "image";
        if (ct == ControlType.MenuItem) return "menu-item";
        if (ct == ControlType.TabItem) return "tab-item";
        if (ct == ControlType.ComboBox) return "combo-box";
        if (ct == ControlType.CheckBox) return "checkbox";
        if (ct == ControlType.RadioButton) return "radio";
        if (ct == ControlType.Hyperlink) return "link";
        if (ct == ControlType.ListItem) return "list-item";
        if (ct == ControlType.TreeItem) return "tree-item";
        if (ct == ControlType.Slider) return "slider";
        return null;
    }

    // ─── Taskbar ────────────────────────────────────────────────────────────────

    public static List<TaskbarElement> GetTaskbarElements()
    {
        var items = new List<TaskbarElement>();
        var seen = new HashSet<string>();
        var root = AutomationElement.RootElement;

        foreach (var trayClass in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            try
            {
                var taskbars = root.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ClassNameProperty, trayClass));

                for (int t = 0; t < taskbars.Count; t++)
                {
                    var taskbar = taskbars[t];
                    var descendants = taskbar.FindAll(TreeScope.Descendants, Condition.TrueCondition);

                    for (int i = 0; i < descendants.Count; i++)
                    {
                        try
                        {
                            var node = descendants[i];
                            var nodeRect = node.Current.BoundingRectangle;
                            if (nodeRect.Width <= 0 || nodeRect.Height <= 0) continue;
                            if (nodeRect.Width > 800 || nodeRect.Height > 300) continue;

                            var role = GetTaskbarRole(node);
                            if (role == null) continue;

                            int bx = (int)Math.Round(nodeRect.X);
                            int by = (int)Math.Round(nodeRect.Y);
                            int bw = (int)Math.Round(nodeRect.Width);
                            int bh = (int)Math.Round(nodeRect.Height);

                            var key = $"{role}:{bx}:{by}:{node.Current.AutomationId}:{node.Current.Name}";
                            if (!seen.Add(key)) continue;

                            items.Add(new TaskbarElement
                            {
                                Text = node.Current.Name ?? "",
                                Role = role,
                                Bounds = new Bounds { X = bx, Y = by, Width = bw, Height = bh },
                                AutomationId = node.Current.AutomationId ?? "",
                            });
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        return items;
    }

    private static string? GetTaskbarRole(AutomationElement node)
    {
        var aid = node.Current.AutomationId ?? "";
        var cls = node.Current.ClassName ?? "";
        var name = node.Current.Name ?? "";

        if (aid == "StartButton") return "start-button";
        if (aid == "SearchButton") return "search-box";
        if (cls == "Taskbar.TaskListButtonAutomationPeer") return "taskbar-app";
        if (aid == "SystemTrayIcon" || cls.StartsWith("SystemTray.")) return "system-tray";
        if (aid == "WidgetsButton") return "widgets";
        if (name == "Show Desktop") return "show-desktop";
        return null;
    }

    // ─── Window Occlusion Analysis ──────────────────────────────────────────────

    private const int CellSize = 24; // grid cell size in pixels
    private const double OcclusionThreshold = 0.15; // <15% visible = occluded

    /// <summary>
    /// Grid-based occlusion detection: determines which fraction of each window is
    /// actually visible (not covered by windows in front). Windows enumerated by
    /// EnumWindows come in z-order (front to back), so lower ZOrder = more in front.
    /// </summary>
    public static void AnalyzeWindowOcclusion(SceneData scene)
    {
        if (scene.Windows.Count == 0) return;

        var vd = scene.VirtualDesktop;
        int gridW = (vd.Width + CellSize - 1) / CellSize;
        int gridH = (vd.Height + CellSize - 1) / CellSize;

        // Grid tracks whether each cell is already claimed by a window in front
        var claimed = new bool[gridW * gridH];

        // Process windows in z-order (front to back = lowest ZOrder first)
        var sorted = scene.Windows.OrderBy(w => w.ZOrder).ToList();

        foreach (var win in sorted)
        {
            var b = win.Bounds;
            // Convert window bounds to grid coordinates
            int gx1 = Math.Max(0, (b.X - vd.Left) / CellSize);
            int gy1 = Math.Max(0, (b.Y - vd.Top) / CellSize);
            int gx2 = Math.Min(gridW, (b.X - vd.Left + b.Width + CellSize - 1) / CellSize);
            int gy2 = Math.Min(gridH, (b.Y - vd.Top + b.Height + CellSize - 1) / CellSize);

            int totalCells = 0;
            int visibleCells = 0;

            for (int gy = gy1; gy < gy2; gy++)
            {
                for (int gx = gx1; gx < gx2; gx++)
                {
                    totalCells++;
                    int idx = gy * gridW + gx;
                    if (!claimed[idx])
                    {
                        visibleCells++;
                        claimed[idx] = true; // this cell is now covered by this window
                    }
                }
            }

            win.VisibleFraction = totalCells > 0 ? (double)visibleCells / totalCells : 0;
            win.Occluded = win.VisibleFraction < OcclusionThreshold;
        }
    }

    // ─── Desktop Region Detection ────────────────────────────────────────────────

    /// <summary>
    /// Finds uncovered desktop regions — areas of the screen where no window is in front.
    /// Uses the same grid approach: any cell that's on a screen but not covered by a window
    /// is "desktop". Adjacent cells are merged into rectangular regions.
    /// </summary>
    public static List<DesktopRegion> FindDesktopRegions(SceneData scene)
    {
        var vd = scene.VirtualDesktop;
        int gridW = (vd.Width + CellSize - 1) / CellSize;
        int gridH = (vd.Height + CellSize - 1) / CellSize;

        // Mark cells that are on a screen
        var screenGrid = new bool[gridW * gridH];
        foreach (var scr in scene.Screens)
        {
            int gx1 = Math.Max(0, (scr.X - vd.Left) / CellSize);
            int gy1 = Math.Max(0, (scr.Y - vd.Top) / CellSize);
            int gx2 = Math.Min(gridW, (scr.X - vd.Left + scr.Width + CellSize - 1) / CellSize);
            int gy2 = Math.Min(gridH, (scr.Y - vd.Top + scr.Height + CellSize - 1) / CellSize);
            for (int gy = gy1; gy < gy2; gy++)
                for (int gx = gx1; gx < gx2; gx++)
                    screenGrid[gy * gridW + gx] = true;
        }

        // Mark cells covered by drawable (non-occluded) windows
        var coveredGrid = new bool[gridW * gridH];
        foreach (var win in scene.Windows.Where(w => !w.Occluded))
        {
            var b = win.Bounds;
            int gx1 = Math.Max(0, (b.X - vd.Left) / CellSize);
            int gy1 = Math.Max(0, (b.Y - vd.Top) / CellSize);
            int gx2 = Math.Min(gridW, (b.X - vd.Left + b.Width + CellSize - 1) / CellSize);
            int gy2 = Math.Min(gridH, (b.Y - vd.Top + b.Height + CellSize - 1) / CellSize);
            for (int gy = gy1; gy < gy2; gy++)
                for (int gx = gx1; gx < gx2; gx++)
                    coveredGrid[gy * gridW + gx] = true;
        }

        // Desktop cells = on screen AND not covered
        var desktopGrid = new bool[gridW * gridH];
        for (int i = 0; i < screenGrid.Length; i++)
            desktopGrid[i] = screenGrid[i] && !coveredGrid[i];

        // Extract rectangular regions using row-based run-length grouping
        return ExtractRegions(desktopGrid, gridW, gridH, vd, scene.Screens);
    }

    private static List<DesktopRegion> ExtractRegions(bool[] grid, int gridW, int gridH,
        VirtualDesktopInfo vd, List<ScreenInfo> screens)
    {
        var visited = new bool[gridW * gridH];
        var regions = new List<DesktopRegion>();

        for (int gy = 0; gy < gridH; gy++)
        {
            for (int gx = 0; gx < gridW; gx++)
            {
                int idx = gy * gridW + gx;
                if (!grid[idx] || visited[idx]) continue;

                // Flood-fill to find connected region, track bounding box
                int minGx = gx, minGy = gy, maxGx = gx, maxGy = gy;
                int cellCount = 0;
                var queue = new Queue<(int x, int y)>();
                queue.Enqueue((gx, gy));
                visited[idx] = true;

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    cellCount++;
                    if (cx < minGx) minGx = cx;
                    if (cy < minGy) minGy = cy;
                    if (cx > maxGx) maxGx = cx;
                    if (cy > maxGy) maxGy = cy;

                    // 4-connected neighbors
                    foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                    {
                        if (nx < 0 || nx >= gridW || ny < 0 || ny >= gridH) continue;
                        int ni = ny * gridW + nx;
                        if (!grid[ni] || visited[ni]) continue;
                        visited[ni] = true;
                        queue.Enqueue((nx, ny));
                    }
                }

                // Filter tiny regions (less than ~144x144 pixels = 6x6 cells = 36 cells)
                if (cellCount < 36) continue;

                int px = vd.Left + minGx * CellSize;
                int py = vd.Top + minGy * CellSize;
                int pw = (maxGx - minGx + 1) * CellSize;
                int ph = (maxGy - minGy + 1) * CellSize;

                // Determine which screen this region is on
                int screenIdx = -1;
                int centerX = px + pw / 2;
                int centerY = py + ph / 2;
                for (int si = 0; si < screens.Count; si++)
                {
                    var s = screens[si];
                    if (centerX >= s.X && centerX < s.X + s.Width && centerY >= s.Y && centerY < s.Y + s.Height)
                    {
                        screenIdx = si;
                        break;
                    }
                }

                regions.Add(new DesktopRegion
                {
                    Bounds = new Bounds { X = px, Y = py, Width = pw, Height = ph },
                    ScreenIndex = screenIdx,
                    AreaPixels = cellCount * CellSize * CellSize,
                });
            }
        }

        return regions;
    }

    // ─── Summary ────────────────────────────────────────────────────────────────

    private static SceneSummary BuildSummary(SceneData scene)
    {
        var byKind = new Dictionary<string, int>();
        int totalElements = 0;

        foreach (var win in scene.Windows)
        {
            totalElements += win.Elements.Count;
            foreach (var elem in win.Elements)
            {
                byKind.TryGetValue(elem.Kind, out var count);
                byKind[elem.Kind] = count + 1;
            }
        }

        return new SceneSummary
        {
            WindowCount = scene.Windows.Count,
            TotalElements = totalElements,
            TaskbarElementCount = scene.TaskbarElements.Count,
            ElementsByKind = byKind,
        };
    }
}
