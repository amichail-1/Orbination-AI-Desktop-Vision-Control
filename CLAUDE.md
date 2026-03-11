# Desktop Control MCP - AI Usage Guide

## Observation Priority (HOW TO SEE THE SCREEN)

Use text-based tools FIRST — they return exact text, coordinates, and element types. Screenshots are a LAST RESORT.

1. **`ocr_window`** — Read ALL visible text from a window with click coordinates. Use this FIRST to understand what's on screen. Returns exact text strings you can use in click_element, run_sequence, etc.
2. **`get_window_details`** — Get UI elements (buttons, inputs, tabs) with types and coordinates. Use with kindFilter for specific element types.
3. **`list_windows`** — See all open windows with visibility %. Use to find the right window title.
4. **`scan_desktop`** — Full desktop overview: windows, elements, taskbar. Use at session start.
5. **`screenshot_to_file`** — Visual screenshot. Use ONLY when text tools don't give enough context (e.g. visual layout, images, charts) or for final verification.

## Action Priority (HOW TO INTERACT)

1. **`click_element` / `interact`** — Find by text, auto-detect type. Has UIAutomation + OCR fallback. Most reliable.
2. **`click_menu_item`** — Navigate parent > child menus in one call.
3. **`run_sequence`** — Batch multiple hotkeys, waits, focus changes in ONE call. For clicking buttons, prefer `click_element` over `run_sequence`'s `ocr_click`.
4. **`set_clipboard` + `keyboard_hotkey` (ctrl+v)** or **`paste_text`** — For large text (XML, code, JSON).
5. **`mouse_click x,y`** — Direct coordinate click. ONLY when text-based tools fail. Get coords from `ocr_window` or `get_window_details` first.

## Workflow Pattern
1. `ocr_window` or `get_window_details` — understand what's on screen (text + coordinates)
2. `click_element` / `click_menu_item` — click buttons and menus by text
3. `run_sequence` — batch keyboard actions (hotkey, wait, type, paste)
4. `ocr_window` — verify result by reading text (NOT screenshot)
5. `screenshot_to_file` — only for final visual verification if needed

## Anti-Patterns (DO NOT)
- Do NOT use `screenshot_to_file` to understand UI — use `ocr_window` or `get_window_details` instead
- Do NOT screenshot after every action — use `ocr_window` to check state when needed
- Do NOT guess button text — OCR the window first, then use exact text from OCR results
- Do NOT use `run_sequence` `ocr_click` for buttons — use `click_element` which has better matching
- Do NOT use `keyboard_type` for large text — use `paste_text` or `set_clipboard` + ctrl+v
- Do NOT use `mouse_click` with guessed coordinates — get coords from `ocr_window`/`get_window_details` first
