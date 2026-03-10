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
        scene.Summary = BuildSummary(scene);
        return scene;
    }

    public static SceneData ScanWindowsOnly()
    {
        var scene = new SceneData();
        scene.Screens = GetScreens();
        scene.VirtualDesktop = GetVirtualDesktop();
        scene.Windows = EnumerateWindows(includeElements: false);
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
