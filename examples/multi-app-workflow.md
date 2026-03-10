# Multi-App Workflow Automation

Use desktop-control to orchestrate workflows across multiple applications — something no single-app automation tool can do.

## Example: Code Review with Live Preview

> "Open the PR in Chrome, run the app locally, and visually verify the changes match the design."

```
// Open PR in browser
AI uses: navigate_to_url("https://github.com/org/repo/pull/42")
AI uses: wait_seconds(3)
AI uses: read_window_text("Chrome")
// Reads the PR description and changed files

// Open the code in VS Code
AI uses: open_app("Visual Studio Code")
AI uses: keyboard_hotkey("ctrl", "shift", "p")
AI uses: keyboard_type("git checkout pr-branch")
AI uses: keyboard_press("enter")

// Run the dev server
AI uses: keyboard_hotkey("ctrl", "`")  // Open terminal
AI uses: keyboard_type("npm run dev")
AI uses: keyboard_press("enter")
AI uses: wait_seconds(5)

// Open the running app
AI uses: navigate_to_url("http://localhost:3000")

// Screenshot and compare with the design spec
AI uses: screenshot_region(...)
```

## Example: Database + App Testing

> "Insert test data into the database tool, then verify it shows up in the app."

```
// Open database tool
AI uses: open_app("DBeaver")
AI uses: wait_seconds(2)

// Run a query
AI uses: click_element("SQL Editor")
AI uses: keyboard_type("INSERT INTO users (name, email) VALUES ('Test User', 'test@example.com');")
AI uses: keyboard_hotkey("ctrl", "enter")
AI uses: wait_seconds(1)

// Switch to the app
AI uses: focus_window("MyApp")
AI uses: click_element("Refresh")
AI uses: wait_seconds(1)

// Verify the new user appears
AI uses: find_element("Test User", windowLabel: "MyApp")
AI uses: screenshot_region(...)
```

## Example: Design Tool to Code

> "Look at the Figma design, then check if the implemented page matches."

```
// Open Figma in browser
AI uses: navigate_to_url("https://figma.com/file/...")
AI uses: wait_seconds(3)
AI uses: screenshot_region(...)  // Capture design

// Open the running app in another tab
AI uses: navigate_to_url("http://localhost:3000/page")
AI uses: wait_seconds(2)
AI uses: screenshot_region(...)  // Capture implementation

// AI visually compares both screenshots
// Identifies: spacing differences, color mismatches, missing elements
// Then edits the CSS/code to fix them
```

## Why This Matters

No other tool can:
- Read from a **browser** AND a **desktop app** AND a **terminal** in one workflow
- Understand UI elements across **different application frameworks**
- Take actions in one app based on what it reads in another
- Do all of this from **natural language instructions**
