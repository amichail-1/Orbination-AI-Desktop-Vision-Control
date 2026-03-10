# Visual UI Comparison: HTML Design vs Flutter App

This example shows how an AI assistant can visually compare two versions of a UI and identify differences — something that's impossible without desktop vision.

## The Problem

You have a new UI design in HTML/CSS and need to replicate it in Flutter. You ask AI to convert it, but the result never matches because the AI can't see either version.

## The Solution

With desktop-control, the AI can:

1. **Open both apps** side by side
2. **Screenshot each one** at specific states
3. **Read UI elements** from both (buttons, inputs, text)
4. **Click through both** to compare interactive states
5. **Identify exact differences** and fix the code

## Workflow

### Step 1: Open Both Apps

```
AI uses: open_app("Chrome")
AI uses: navigate_to_url("file:///path/to/design.html")
AI uses: open_app("MyFlutterApp")
```

### Step 2: Scan UI Elements

```
AI uses: scan_desktop()
AI uses: get_window_details("Chrome", kindFilter: "button")
AI uses: get_window_details("MyFlutterApp", kindFilter: "button")
```

The AI now has a structured list of every button, input, and text element in both apps with exact positions.

### Step 3: Screenshot and Compare

```
AI uses: focus_window("Chrome")
AI uses: screenshot_region(0, 0, 1920, 1080, "design.png")

AI uses: focus_window("MyFlutterApp")
AI uses: screenshot_region(0, 0, 1920, 1080, "flutter.png")
```

The AI can now visually compare both screenshots.

### Step 4: Interactive Comparison

```
// Click sidebar in HTML design
AI uses: click_element("Toggle sidebar", windowLabel: "Chrome")
AI uses: screenshot_region(...)

// Click sidebar in Flutter app
AI uses: click_element("Toggle sidebar", windowLabel: "MyFlutterApp")
AI uses: screenshot_region(...)

// Compare: Does the sidebar look the same? Same width? Same items?
```

### Step 5: Read and Compare Text

```
AI uses: read_window_text("Chrome")
// Returns: ["orbination", "Search chats", "Projects", "CONVERSATIONS", ...]

AI uses: read_window_text("MyFlutterApp")
// Returns: ["orbination", "Search", "Projects", "Workspace", ...]

// AI identifies: "Flutter is missing 'CONVERSATIONS' header, has 'Workspace' instead"
```

### Step 6: Fix the Code

The AI now knows exactly what's different and can edit the Flutter source code to match. Then:

```
// Hot reload Flutter app
AI uses: keyboard_hotkey("ctrl", "shift", "f5")
AI uses: wait_seconds(3)

// Screenshot again and compare
AI uses: screenshot_region(...)
// Verify the fix worked
```

## What This Enables

- **Pixel-level UI matching** between design and implementation
- **Animation comparison** by taking screenshots at different interaction states
- **Regression testing** — detect when UI changes break the design
- **Cross-platform comparison** — compare web vs desktop vs mobile versions
