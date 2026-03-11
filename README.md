# Orbination AI Desktop Vision & Control

[![Release](https://img.shields.io/github/v/release/amichail-1/Orbination-AI-Desktop-Vision-Control?style=flat-square)](https://github.com/amichail-1/Orbination-AI-Desktop-Vision-Control/releases)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-purple?style=flat-square)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![MCP](https://img.shields.io/badge/MCP-Compatible-green?style=flat-square)](https://modelcontextprotocol.io)
[![Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=flat-square&logo=windows)](https://www.microsoft.com/windows)

**Give AI assistants eyes and hands.** A native Windows MCP server that lets AI see the screen, read UI elements, click buttons, type text, and control any application — with built-in OCR, dark theme support, window occlusion detection, and batch action sequencing.

Built for [Claude Code](https://claude.ai/code) by [Leia Enterprise Solutions](https://leia.gr) for the [Orbination](https://orbination.com) project.

> AI coding assistants are blind. They generate code but can never see the result. They can't compare a design mockup to a running app. They can't click through a UI to test it. **This server fixes that.**

## What It Does

This MCP server bridges the gap between AI and your desktop. Instead of working blind with just text, the AI can:

- **See** — Take screenshots, run OCR on any window (auto-enhances dark themes), detect window occlusion
- **Read** — Detect every UI element (buttons, inputs, text, tabs, checkboxes) with exact positions via Windows UIAutomation
- **Interact** — Click elements by text (UIAutomation + OCR fallback), navigate menus, fill forms, type and paste text
- **Navigate** — Open apps, switch windows, focus tabs, navigate browser URLs
- **Understand** — Scan the entire desktop: window visibility %, occlusion detection, uncovered desktop regions
- **Batch** — Execute multi-step UI workflows in a single call with `run_sequence`

## What's New in v2.0

- **Window Occlusion Detection** — Grid-based analysis showing which windows are truly visible (visibility %) and which are hidden behind others
- **Desktop Region Detection** — Flood-fill algorithm to find uncovered screen areas
- **Shared OcrService** — Centralized OCR with automatic dark theme enhancement (invert + contrast boost) — single-pass, not two
- **PrintWindow API** — Capture window content even when obscured by other windows
- **`click_element` OCR Fallback** — UIAutomation first, then OCR for dark themes, web apps, iframes
- **`run_sequence`** — Batch multiple UI actions (click, type, paste, hotkey, wait, focus, OCR click) in a single MCP call
- **`click_menu_item`** — Navigate parent > child menus with smooth mouse movement to keep submenus open
- **DPI Awareness** — Per-monitor DPI for correct coordinates on multi-monitor setups with mixed scaling
- **Embedded AI Instructions** — Server sends tool usage guidelines on MCP connection, teaching AI to prefer OCR over screenshots

## Architecture

```
AI Client (Claude Code / Claude Desktop)
         │
         │  MCP / stdio
         ▼
    ┌─────────────────────────────┐
    │       MCP Server            │
    │   (ServerInstructions)      │
    └─────────┬───────────────────┘
              │
    ┌─────────┼──────────────────────────────────────┐
    │         │         │          │          │       │
    ▼         ▼         ▼          ▼          ▼       │
 Mouse    Keyboard   Screen    Vision    Composite   │
 Tools     Tools     Tools     Tools      Tools      │
                       │          │          │       │
              ┌────────┼──────────┼──────────┘       │
              ▼        ▼          ▼                  │
          Win32     UIAuto-    OcrService            │
          Native    mation     (dark theme)          │
              │        │                             │
              ▼        ▼                             │
         DesktopScanner    NativeInput               │
         (occlusion,       (SendInput,               │
          regions)          clipboard)               │
              │               │                      │
              └───────┬───────┘                      │
                      ▼                              │
               Windows OS                            │
               (Desktop, Windows, Apps)              │
    └────────────────────────────────────────────────┘
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

## Tools (45+)

### Vision & Element Detection

| Tool | Description |
|---|---|
| `scan_desktop` | Full desktop scan — screens, windows with visibility %, UI elements, desktop regions, taskbar |
| `list_windows` | List all visible windows with titles, process names, visibility %, occlusion status |
| `get_window_details` | Get all UI elements in a window (filter by kind: button, input, text, etc.) |
| `find_element` | Search for a UI element by text across all windows |
| `read_window_text` | Extract all visible text from a window |
| `refresh_window` | Re-scan a single window's elements (faster than full scan) |

### Interaction

| Tool | Description |
|---|---|
| `click_element` | Find element by text and click — UIAutomation first, OCR fallback for dark themes/web apps |
| `type_in_element` | Find an input field and type text (ValuePattern, clipboard paste, or click+type fallback) |
| `interact` | Smart interaction — auto-detects element type and performs the right action |
| `fill_form` | Fill multiple form fields in one call with JSON field:value pairs |
| `select_tab` | Select a browser or application tab by text |
| `click_menu_item` | Navigate menus: click parent, smooth-move to child, click — single call |

### Batch & Composite Actions

| Tool | Description |
|---|---|
| `run_sequence` | Execute multiple UI actions in ONE call: click, type, paste, hotkey, wait, focus, OCR click, screenshot |
| `click_and_type` | Click at position then type text |
| `focus_and_hotkey` | Click to focus (e.g. iframe) then send keyboard shortcut atomically |

### Mouse & Keyboard

| Tool | Description |
|---|---|
| `mouse_click` | Click at screen coordinates |
| `mouse_move` | Move cursor to position |
| `mouse_move_smooth` | Move mouse smoothly (keeps menus/submenus open) |
| `mouse_drag` | Drag from one position to another |
| `mouse_scroll` | Scroll the mouse wheel |
| `mouse_get_position` | Get current cursor position |
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

### Screenshots & OCR

| Tool | Description |
|---|---|
| `screenshot_to_file` | Full screenshot across all monitors |
| `screenshot_region` | Screenshot a specific screen region |
| `screenshot_window` | Capture a window via PrintWindow API (works even when obscured) |
| `get_screen_info` | Get monitor layout (positions, sizes, primary) |
| `ocr_screen_region` | Capture a region and run OCR — auto-enhances dark themes |
| `ocr_window` | Run OCR on an entire window — reads all text with click coordinates |
| `ocr_find_text` | Search for specific text on screen using OCR — returns click coordinates |

### Utilities

| Tool | Description |
|---|---|
| `set_clipboard` | Set clipboard text without pasting |
| `paste_text` | Paste large text via clipboard (XML, code, multi-line) |
| `auto_scroll` | Scroll with pauses between batches |
| `wait_seconds` | Pause between actions |
| `wait_for_element` | Poll for UI element to appear with timeout |

## Embedded AI Instructions

The server sends **tool usage guidelines** automatically on every MCP connection via `ServerInstructions`. This teaches AI clients the optimal workflow without requiring any configuration files:

**Observation Priority:** `ocr_window` > `get_window_details` > `list_windows` > `scan_desktop` > `screenshot_to_file`

**Action Priority:** `click_element` > `click_menu_item` > `run_sequence` > `paste_text` > `mouse_click`

The key insight: **OCR and UIAutomation return exact text and coordinates** — the AI knows exactly what to click. Screenshots require vision processing and guessing. OCR-first workflows are faster, cheaper, and more reliable.

## Window Occlusion Detection

The server uses a **grid-based occlusion analysis** (24px cells) to determine which windows are truly visible:

```
Chrome (chrome) [71] @ -2060,-1461 3456x1403        ← 100% visible
VS Code (Code) [45] @ -1500,-800 1200x900           ← 65% visible
Explorer (explorer) [20] @ -1400,-700 800x600        ← 0% visible [OCCLUDED]
```

The AI knows which windows it can interact with and which are hidden. Combined with **desktop region detection** (flood-fill to find uncovered screen areas), the AI has a complete spatial understanding of the desktop.

## Dark Theme OCR Enhancement

Many modern apps use dark themes where standard OCR fails. The server automatically detects dark backgrounds and enhances images before OCR:

1. **Sample pixel luminance** across the image
2. If average luminance < 100 → dark theme detected
3. **Invert colors** + **boost contrast (1.4x)** — single pass
4. Run OCR on enhanced image

This works automatically on `ocr_window`, `ocr_screen_region`, `ocr_find_text`, and `click_element`'s OCR fallback.

## Multi-Monitor Support

Full multi-monitor support out of the box with **per-monitor DPI awareness**:

- **Auto-detects all monitors** — positions, sizes, primary screen via `get_screen_info`
- **Virtual desktop mapping** — coordinates span the full virtual desktop, including negative coordinates for left/top monitors
- **DPI-aware** — correct coordinates on mixed-scaling setups (e.g. 100% on one monitor, 150% on another)
- **Cross-monitor screenshots** — `screenshot_to_file` captures all screens, `screenshot_region` targets any region
- **Window-aware** — windows on any monitor are detected with correct positions
- **Taskbar scanning** — reads both `Shell_TrayWnd` (primary) and `Shell_SecondaryTrayWnd` (secondary monitors)

## How UIAutomation Works

Unlike screenshot-based tools that guess what's on screen, this server reads the actual UI element tree exposed by Windows. Every button, input field, text label, tab, and checkbox is detected with:

- **Exact position and size** (bounding rectangle)
- **Text/label** (what the element says)
- **Control type** (button, input, text, checkbox, etc.)
- **Automation ID** (developer-assigned identifier)
- **Supported patterns** (can it be clicked? typed into? toggled?)

### UIAutomation + OCR Fallback

`click_element` combines both strategies. UIAutomation first (fast, structured), OCR fallback (universal):

```
click_element "Save"
  → UIAutomation: found "Save" button → click via Invoke pattern ✓

click_element "OK"  (dark web dialog)
  → UIAutomation: not found
  → OCR: capture window → enhance dark theme → find "OK" text → click center ✓
```

### Limitation: Custom-Rendered Apps

Applications that render their own UI canvas (Flutter, Electron with custom rendering, game engines) may expose fewer elements to UIAutomation. The OCR fallback handles these cases automatically.

## Token-Efficient by Design

Every MCP tool call costs tokens. This server is engineered to minimize token usage:

### Structured Data Instead of Screenshots

Most desktop automation tools send full screenshots for every action — each one costs **thousands of tokens**. This server returns **compact structured text**:

```
[button] "Save" @ 450,320
[input] "Search..." @ 200,60
[tab-item] "Settings" @ 120,35
```

### Batch Operations

- `run_sequence` executes multiple actions in one call (click, type, paste, hotkey, wait, focus)
- `fill_form` fills multiple form fields in a single call
- `scan_desktop` returns screens + windows + elements + taskbar in one response
- `click_menu_item` navigates parent > child menus in one call

### Smart Caching

Scan results are cached for 30 seconds. Individual windows can be refreshed with `refresh_window` instead of a full `scan_desktop`. The scanner uses UIAutomation's `CacheRequest` to batch-fetch all properties in a single cross-process call.

## Project Structure

```
DesktopControlMcp/
├── Program.cs                    # MCP server entry + DPI awareness + ServerInstructions
├── NativeInput.cs                # Low-level mouse/keyboard via SendInput
├── Native/
│   └── Win32.cs                  # P/Invoke: EnumWindows, PrintWindow, window management
├── Models/
│   └── SceneData.cs              # Data models: windows (with occlusion), elements, regions
├── Services/
│   ├── DesktopScanner.cs         # Desktop scanning + occlusion analysis + region detection
│   ├── OcrService.cs             # Shared OCR engine with dark theme auto-enhancement
│   └── UiAutomationHelper.cs     # Element interaction patterns
└── Tools/
    ├── VisionTools.cs            # scan, find, click (with OCR fallback), list windows
    ├── CompositeTools.cs         # run_sequence, click_menu_item, navigate, open app
    ├── MouseTools.cs             # Mouse control
    ├── KeyboardTools.cs          # Keyboard control
    └── ScreenTools.cs            # Screenshots, OCR tools, PrintWindow capture
```

## Examples

See the [`examples/`](examples/) folder for real-world workflows:

- **[Visual UI Comparison](examples/visual-ui-comparison.md)** — AI opens an HTML design and a Flutter app side by side, clicks through both, and identifies every visual difference
- **[Automated UI Testing](examples/automated-testing.md)** — AI tests login flows, form validation, and navigation by clicking through any app — no test scripts needed
- **[Multi-App Workflows](examples/multi-app-workflow.md)** — AI orchestrates across browser, code editor, database tool, and desktop apps in a single workflow

## Quick Install

**Option A: Download pre-built binary**

1. Download from [Releases](https://github.com/amichail-1/Orbination-AI-Desktop-Vision-Control/releases)
2. Extract the zip
3. Add to Claude Code:
```bash
claude mcp add desktop-control -- "C:\path\to\DesktopControlMcp.exe"
```

**Option B: Build from source**

```bash
git clone https://github.com/amichail-1/Orbination-AI-Desktop-Vision-Control.git
cd Orbination-AI-Desktop-Vision-Control/DesktopControlMcp
dotnet build -c Release
claude mcp add desktop-control -- "bin\Release\net8.0-windows\DesktopControlMcp.exe"
```

## Contributing

Contributions welcome. Open an issue or submit a PR.

## License

MIT
