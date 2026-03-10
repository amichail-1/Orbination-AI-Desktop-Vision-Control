using System.Windows.Automation;
using DesktopControlMcp.Native;
using static DesktopControlMcp.Native.Win32;

namespace DesktopControlMcp.Services;

/// <summary>
/// Direct UI element interaction via UIAutomation patterns.
/// This is far more reliable than simulating mouse/keyboard because
/// it doesn't depend on window focus or cursor position.
/// Inspired by desktopvisionpro's UIAutomation element detection.
/// </summary>
public static class UiAutomationHelper
{
    /// <summary>
    /// Find an AutomationElement by text/name and optional window title.
    /// Searches all descendants of matching windows.
    /// </summary>
    public static AutomationElement? FindElement(string text, string windowTitle = "")
    {
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, Condition.TrueCondition);

        foreach (AutomationElement win in windows)
        {
            try
            {
                var title = StripInvisibleChars(win.Current.Name ?? "");
                if (!string.IsNullOrEmpty(windowTitle) &&
                    !title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip non-visible windows
                if (win.Current.BoundingRectangle.Width <= 0) continue;

                var element = FindInDescendants(win, text);
                if (element != null) return element;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Find an AutomationElement by AutomationId within a window.
    /// </summary>
    public static AutomationElement? FindElementByAutomationId(string automationId, string windowTitle = "")
    {
        var root = AutomationElement.RootElement;
        var windows = root.FindAll(TreeScope.Children, Condition.TrueCondition);

        foreach (AutomationElement win in windows)
        {
            try
            {
                var title = StripInvisibleChars(win.Current.Name ?? "");
                if (!string.IsNullOrEmpty(windowTitle) &&
                    !title.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (win.Current.BoundingRectangle.Width <= 0) continue;

                var cond = new PropertyCondition(AutomationElement.AutomationIdProperty, automationId);
                var found = win.FindFirst(TreeScope.Descendants, cond);
                if (found != null) return found;
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Find a descendant element whose Name contains the search text.
    /// </summary>
    private static AutomationElement? FindInDescendants(AutomationElement parent, string text)
    {
        var all = parent.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        AutomationElement? bestMatch = null;
        int bestLen = int.MaxValue;

        for (int i = 0; i < all.Count; i++)
        {
            try
            {
                var node = all[i];
                var name = node.Current.Name ?? "";
                var aid = node.Current.AutomationId ?? "";

                // Exact match on name or automationId
                if (name.Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    aid.Equals(text, StringComparison.OrdinalIgnoreCase))
                    return node;

                // Contains match - prefer shortest matching name (most specific)
                if (name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                    aid.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    if (name.Length < bestLen)
                    {
                        bestMatch = node;
                        bestLen = name.Length;
                    }
                }
            }
            catch { }
        }

        return bestMatch;
    }

    /// <summary>
    /// Focus an element using UIAutomation SetFocus.
    /// </summary>
    public static bool FocusElement(AutomationElement element)
    {
        try
        {
            // First focus the owning window
            var walker = TreeWalker.ControlViewWalker;
            var parent = element;
            while (parent != null && parent != AutomationElement.RootElement)
            {
                if (parent.Current.ControlType == ControlType.Window)
                {
                    var hWnd = new nint(parent.Current.NativeWindowHandle);
                    if (hWnd != nint.Zero)
                        Win32.FocusWindow(hWnd);
                    break;
                }
                parent = walker.GetParent(parent);
            }

            element.SetFocus();
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Set text on an element using ValuePattern (direct, no keyboard simulation).
    /// Falls back to legacy ValuePattern if needed.
    /// </summary>
    public static bool SetValue(AutomationElement element, string value)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
            {
                var vp = (ValuePattern)pattern;
                if (!vp.Current.IsReadOnly)
                {
                    vp.SetValue(value);
                    return true;
                }
            }
        }
        catch { }

        // Fallback: focus window + focus element + select all + type
        // This handles Chrome's OmniboxViewViews which doesn't expose ValuePattern
        try
        {
            FocusElement(element);
            Thread.Sleep(200); // let focus settle

            // Select all existing text
            NativeInput.Hotkey("ctrl", "a");
            Thread.Sleep(100);

            // Delete selection
            NativeInput.KeyPress(NativeInput.VK_DELETE);
            Thread.Sleep(50);

            // Type new value
            NativeInput.TypeUnicode(value);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Click/invoke an element using InvokePattern (direct, no mouse simulation).
    /// </summary>
    public static bool Invoke(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern))
            {
                ((InvokePattern)pattern).Invoke();
                return true;
            }
        }
        catch { }

        // Fallback: focus + click at element center
        try
        {
            FocusElement(element);
            Thread.Sleep(50);
            var rect = element.Current.BoundingRectangle;
            int cx = (int)(rect.X + rect.Width / 2);
            int cy = (int)(rect.Y + rect.Height / 2);
            NativeInput.MoveMouse(cx, cy);
            Thread.Sleep(30);
            NativeInput.MouseClick("left", 1);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Toggle a toggle/checkbox element.
    /// </summary>
    public static bool Toggle(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(TogglePattern.Pattern, out var pattern))
            {
                ((TogglePattern)pattern).Toggle();
                return true;
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Select a tab or selection item.
    /// </summary>
    public static bool Select(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern))
            {
                ((SelectionItemPattern)pattern).Select();
                return true;
            }
        }
        catch { }

        // Fallback to Invoke
        return Invoke(element);
    }

    /// <summary>
    /// Get info about what patterns/actions are supported by an element.
    /// </summary>
    public static Dictionary<string, bool> GetSupportedPatterns(AutomationElement element)
    {
        var patterns = new Dictionary<string, bool>();
        try
        {
            patterns["value"] = element.TryGetCurrentPattern(ValuePattern.Pattern, out _);
            patterns["invoke"] = element.TryGetCurrentPattern(InvokePattern.Pattern, out _);
            patterns["toggle"] = element.TryGetCurrentPattern(TogglePattern.Pattern, out _);
            patterns["selection"] = element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _);
            patterns["scroll"] = element.TryGetCurrentPattern(ScrollPattern.Pattern, out _);
            patterns["expandCollapse"] = element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
        }
        catch { }
        return patterns;
    }

    /// <summary>
    /// Get the current value of an element (for inputs, combo boxes, etc.).
    /// </summary>
    public static string? GetValue(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern))
                return ((ValuePattern)pattern).Current.Value;
        }
        catch { }
        return null;
    }
}
