using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DesktopControlMcp.Tools;

[McpServerToolType]
public sealed class KeyboardTools
{
    [McpServerTool(Name = "keyboard_type"), Description("Type text using the keyboard. Supports Unicode.")]
    public static string Type(
        [Description("The text to type")] string text)
    {
        NativeInput.TypeUnicode(text);
        return $"Typed {text.Length} chars";
    }

    [McpServerTool(Name = "keyboard_press"), Description("Press and release a single key.")]
    public static string Press(
        [Description("Key name: enter, tab, escape, space, backspace, delete, up, down, left, right, home, end, pageup, pagedown, f1-f12, a-z, 0-9")] string key)
    {
        var vk = NativeInput.VkFromName(key);
        if (vk == 0) return $"ERROR: Unknown key '{key}'";
        NativeInput.KeyPress(vk);
        return $"Pressed {key}";
    }

    [McpServerTool(Name = "keyboard_hotkey"), Description("Press a keyboard shortcut (key combination like Ctrl+C).")]
    public static string Hotkey(
        [Description("First key (e.g. ctrl, alt, shift, win)")] string key1,
        [Description("Second key (e.g. c, v, tab)")] string key2 = "",
        [Description("Optional third key")] string key3 = "",
        [Description("Optional fourth key")] string key4 = "")
    {
        var keys = new[] { key1, key2, key3, key4 }.Where(k => !string.IsNullOrEmpty(k)).ToArray();
        NativeInput.Hotkey(keys);
        return $"Pressed {string.Join("+", keys)}";
    }

    [McpServerTool(Name = "keyboard_key_down"), Description("Press and hold a key without releasing. Use keyboard_key_up to release.")]
    public static string KeyDown(
        [Description("Key name to hold")] string key)
    {
        var vk = NativeInput.VkFromName(key);
        if (vk == 0) return $"ERROR: Unknown key '{key}'";
        NativeInput.KeyDown(vk);
        return $"Holding {key}";
    }

    [McpServerTool(Name = "keyboard_key_up"), Description("Release a held key.")]
    public static string KeyUp(
        [Description("Key name to release")] string key)
    {
        var vk = NativeInput.VkFromName(key);
        if (vk == 0) return $"ERROR: Unknown key '{key}'";
        NativeInput.KeyUp(vk);
        return $"Released {key}";
    }
}
