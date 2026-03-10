using System.Runtime.InteropServices;

namespace DesktopControlMcp;

/// <summary>
/// Native Windows API for mouse, keyboard, and screen control.
/// </summary>
internal static class NativeInput
{
    // ─── Structs ────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // ─── Constants ──────────────────────────────────────────────────────────────

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;

    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    public const uint KEYEVENTF_KEYDOWN = 0x0000;
    public const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    // Virtual key codes
    public const ushort VK_BACK = 0x08;
    public const ushort VK_TAB = 0x09;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12; // Alt
    public const ushort VK_ESCAPE = 0x1B;
    public const ushort VK_SPACE = 0x20;
    public const ushort VK_PRIOR = 0x21; // Page Up
    public const ushort VK_NEXT = 0x22;  // Page Down
    public const ushort VK_END = 0x23;
    public const ushort VK_HOME = 0x24;
    public const ushort VK_LEFT = 0x25;
    public const ushort VK_UP = 0x26;
    public const ushort VK_RIGHT = 0x27;
    public const ushort VK_DOWN = 0x28;
    public const ushort VK_DELETE = 0x2E;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_F1 = 0x70;
    public const ushort VK_F12 = 0x7B;

    // ─── Imports ────────────────────────────────────────────────────────────────

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;
    public const int SM_XVIRTUALSCREEN = 76;
    public const int SM_YVIRTUALSCREEN = 77;

    // ─── Helpers ────────────────────────────────────────────────────────────────

    public static POINT GetCursorPosition()
    {
        GetCursorPos(out var pt);
        return pt;
    }

    public static void MoveMouse(int x, int y)
    {
        SetCursorPos(x, y);
    }

    public static void MouseClick(string button = "left", int clicks = 1)
    {
        uint downFlag, upFlag;
        switch (button.ToLowerInvariant())
        {
            case "right":
                downFlag = MOUSEEVENTF_RIGHTDOWN;
                upFlag = MOUSEEVENTF_RIGHTUP;
                break;
            case "middle":
                downFlag = MOUSEEVENTF_MIDDLEDOWN;
                upFlag = MOUSEEVENTF_MIDDLEUP;
                break;
            default:
                downFlag = MOUSEEVENTF_LEFTDOWN;
                upFlag = MOUSEEVENTF_LEFTUP;
                break;
        }

        for (int i = 0; i < clicks; i++)
        {
            var inputs = new INPUT[]
            {
                new() { Type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = downFlag } } },
                new() { Type = INPUT_MOUSE, U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = upFlag } } },
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
            if (i < clicks - 1)
                Thread.Sleep(50);
        }
    }

    public static void MouseScroll(int amount)
    {
        var inputs = new INPUT[]
        {
            new()
            {
                Type = INPUT_MOUSE,
                U = new INPUTUNION { mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_WHEEL, mouseData = amount * 120 } }
            },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static ushort VkFromName(string key)
    {
        return key.ToLowerInvariant() switch
        {
            "enter" or "return" => VK_RETURN,
            "tab" => VK_TAB,
            "escape" or "esc" => VK_ESCAPE,
            "space" => VK_SPACE,
            "backspace" => VK_BACK,
            "delete" or "del" => VK_DELETE,
            "shift" => VK_SHIFT,
            "ctrl" or "control" => VK_CONTROL,
            "alt" => VK_MENU,
            "win" or "windows" or "lwin" => VK_LWIN,
            "left" => VK_LEFT,
            "right" => VK_RIGHT,
            "up" => VK_UP,
            "down" => VK_DOWN,
            "home" => VK_HOME,
            "end" => VK_END,
            "pageup" or "pgup" => VK_PRIOR,
            "pagedown" or "pgdn" => VK_NEXT,
            "f1" => VK_F1, "f2" => (ushort)(VK_F1 + 1), "f3" => (ushort)(VK_F1 + 2),
            "f4" => (ushort)(VK_F1 + 3), "f5" => (ushort)(VK_F1 + 4), "f6" => (ushort)(VK_F1 + 5),
            "f7" => (ushort)(VK_F1 + 6), "f8" => (ushort)(VK_F1 + 7), "f9" => (ushort)(VK_F1 + 8),
            "f10" => (ushort)(VK_F1 + 9), "f11" => (ushort)(VK_F1 + 10), "f12" => VK_F12,
            _ when key.Length == 1 => (ushort)char.ToUpperInvariant(key[0]),
            _ => 0,
        };
    }

    public static bool IsExtendedKey(ushort vk)
    {
        return vk is VK_LEFT or VK_RIGHT or VK_UP or VK_DOWN
            or VK_HOME or VK_END or VK_PRIOR or VK_NEXT
            or VK_DELETE or VK_LWIN;
    }

    public static void KeyPress(ushort vk)
    {
        uint flags = IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0;
        var inputs = new INPUT[]
        {
            new() { Type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } },
            new() { Type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags | KEYEVENTF_KEYUP } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void KeyDown(ushort vk)
    {
        uint flags = IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0;
        var inputs = new INPUT[]
        {
            new() { Type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void KeyUp(ushort vk)
    {
        uint flags = (IsExtendedKey(vk) ? KEYEVENTF_EXTENDEDKEY : 0u) | KEYEVENTF_KEYUP;
        var inputs = new INPUT[]
        {
            new() { Type = INPUT_KEYBOARD, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags } } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static void TypeUnicode(string text)
    {
        var inputs = new List<INPUT>();
        foreach (var ch in text)
        {
            inputs.Add(new INPUT
            {
                Type = INPUT_KEYBOARD,
                U = new INPUTUNION { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } }
            });
            inputs.Add(new INPUT
            {
                Type = INPUT_KEYBOARD,
                U = new INPUTUNION { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
            });
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    public static void Hotkey(params string[] keys)
    {
        var vks = keys.Select(VkFromName).Where(v => v != 0).ToArray();
        // Press all down
        foreach (var vk in vks)
            KeyDown(vk);
        Thread.Sleep(30);
        // Release in reverse
        foreach (var vk in vks.Reverse())
            KeyUp(vk);
    }

    /// <summary>
    /// Type text by placing it on the clipboard and pressing Ctrl+V.
    /// More reliable than TypeUnicode for apps with custom input handling (e.g. Telegram, Electron apps).
    /// Must run on STA thread since Clipboard requires it.
    /// </summary>
    [System.STAThread]
    public static void TypeViaClipboard(string text)
    {
        // Clipboard must be accessed from an STA thread
        var done = new System.Threading.ManualResetEventSlim(false);
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                System.Windows.Forms.Clipboard.SetText(text);
            }
            catch (Exception e) { ex = e; }
            finally { done.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        done.Wait(3000);

        if (ex != null) throw ex;

        Thread.Sleep(50);
        Hotkey("ctrl", "v");
        Thread.Sleep(100);
    }
}
