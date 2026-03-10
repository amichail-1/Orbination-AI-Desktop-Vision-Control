# Orbination AI Desktop Vision & Control

A native Windows MCP (Model Context Protocol) server that gives AI assistants full desktop automation capabilities — see the screen, read UI elements, click buttons, type text, and interact with any application.

Built for [Claude Code](https://claude.ai/code) by [Orbination](https://orbination.com).

## What It Does

This MCP server bridges the gap between AI and your desktop. Instead of working blind with just text, the AI can:

- **See** — Take screenshots of any screen region across multiple monitors
- **Read** — Detect every UI element (buttons, inputs, text, tabs, checkboxes) with exact positions via Windows UIAutomation
- **Interact** — Click elements, type text, fill forms, toggle checkboxes, select tabs — all without fragile coordinate guessing
- **Navigate** — Open apps, switch windows, maximize/minimize, scroll, navigate browser URLs
- **Understand** — Scan the entire desktop to build a structured map of all windows and their contents

## Why

AI coding assistants are blind. They generate code but can never see the result. They can't compare a design mockup to a running app. They can't click through a UI to test it. This server gives them eyes and hands.

## Architecture

```
Claude Code  <──MCP/stdio──>  DesktopControlMcp.exe
                                    │
                                    ├── Win32 API (EnumWindows, window management)
                                    ├── UIAutomation (element detection, interaction)
                                    ├── Native Input (mouse/keyboard simulation)
                                    └── GDI+ (screenshots)
```

Single native .NET 8 executable. No Python. No Node.js. No browser drivers. Direct Windows API access.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build

```bash
cd DesktopControlMcp
dotnet build -c Release
```

Or publish as a single file:

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

## Setup with Claude Code

Add the MCP server to your Claude Code configuration:

```bash
claude mcp add desktop-control -- "C:\path\to\DesktopControlMcp.exe"
```

Or add it manually to your MCP config file:

```json
{
  "mcpServers": {
    "desktop-control": {
      "command": "C:\\path\\to\\DesktopControlMcp\\bin\\Release\\net8.0-windows\\DesktopControlMcp.exe",
      "args": []
    }
  }
}
```

## Tools

### Vision & Element Detection

| Tool | Description |
|---|---|
| `scan_desktop` | Full desktop scan — screens, windows, UI elements, taskbar |
| `list_windows` | List all visible windows with titles, process names, bounds |
| `get_window_details` | Get all UI elements in a window (filter by kind: button, input, text, etc.) |
| `find_element` | Search for a UI element by text across all windows |
| `read_window_text` | Extract all visible text from a window |
| `refresh_window` | Re-scan a single window's elements (faster than full scan) |

### Interaction

| Tool | Description |
|---|---|
| `click_element` | Find element by text and click it via UIAutomation (reliable, no coordinate guessing) |
| `type_in_element` | Find an input field and type text (ValuePattern, clipboard paste, or click+type fallback) |
| `interact` | Smart interaction — auto-detects element type and performs the right action |
| `fill_form` | Fill multiple form fields in one call with JSON field:value pairs |
| `select_tab` | Select a browser or application tab by text |

### Mouse & Keyboard

| Tool | Description |
|---|---|
| `mouse_click` | Click at screen coordinates |
| `mouse_move` | Move cursor to position |
| `mouse_drag` | Drag from one position to another |
| `mouse_scroll` | Scroll the mouse wheel |
| `keyboard_type` | Type text (supports Unicode) |
| `keyboard_press` | Press a single key |
| `keyboard_hotkey` | Press key combinations (Ctrl+C, Alt+Tab, etc.) |
| `keyboard_key_down` / `keyboard_key_up` | Hold and release keys |

### Window & App Management

| Tool | Description |
|---|---|
| `focus_window` | Bring a window to the foreground |
| `maximize_window` | Maximize a window |
| `minimize_window` | Minimize a window |
| `restore_window` | Restore a minimized/maximized window |
| `open_app` | Open an app by name (focuses existing, clicks taskbar, or searches Start) |
| `navigate_to_url` | Navigate a browser to a URL |

### Screenshots

| Tool | Description |
|---|---|
| `screenshot_to_file` | Full screenshot across all monitors |
| `screenshot_region` | Screenshot a specific screen region |
| `get_screen_info` | Get monitor layout (positions, sizes, primary) |

### Utilities

| Tool | Description |
|---|---|
| `click_and_type` | Click at position then type text |
| `auto_scroll` | Scroll with pauses between batches |
| `wait_seconds` | Pause between actions |

## How UIAutomation Works

Unlike screenshot-based tools that guess what's on screen, this server reads the actual UI element tree exposed by Windows. Every button, input field, text label, tab, and checkbox is detected with:

- **Exact position and size** (bounding rectangle)
- **Text/label** (what the element says)
- **Control type** (button, input, text, checkbox, etc.)
- **Automation ID** (developer-assigned identifier)
- **Supported patterns** (can it be clicked? typed into? toggled?)

This means the AI can reliably interact with applications without pixel-perfect coordinate matching.

### Limitation: Custom-Rendered Apps

Applications that render their own UI canvas (Flutter, Electron with custom rendering, game engines) may expose fewer or no elements to UIAutomation. For these, the server falls back to screenshot + coordinate-based interaction.

## Project Structure

```
DesktopControlMcp/
├── Program.cs                    # MCP server entry point
├── NativeInput.cs                # Low-level mouse/keyboard via SendInput
├── Native/
│   └── Win32.cs                  # P/Invoke: EnumWindows, window management
├── Models/
│   └── SceneData.cs              # Data models: windows, elements, bounds
├── Services/
│   ├── DesktopScanner.cs         # Desktop scanning via Win32 + UIAutomation
│   └── UiAutomationHelper.cs     # Element interaction patterns
└── Tools/
    ├── VisionTools.cs            # scan, find, click, type, form fill
    ├── CompositeTools.cs         # navigate, open app, window management
    ├── MouseTools.cs             # Mouse control
    ├── KeyboardTools.cs          # Keyboard control
    └── ScreenTools.cs            # Screenshots
```

## License

MIT
