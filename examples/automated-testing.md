# Automated UI Testing with AI

Use desktop-control to let AI assistants test any application by clicking through it like a real user.

## The Problem

Traditional UI testing (Selenium, Playwright, Appium) requires writing test scripts for specific frameworks. Desktop apps, Flutter apps, and Electron apps each need different tools. And someone has to write all the selectors.

## The Solution

With desktop-control, the AI:
- Works with **any** Windows application
- Finds elements by **visible text** (no CSS selectors or XPath)
- Understands the UI **visually** via screenshots
- Can test flows described in **plain English**

## Example: Testing a Login Flow

Tell the AI:

> "Test the login flow of MyApp. Try valid credentials, then invalid ones. Check error messages appear correctly."

The AI will:

```
// Open the app
AI uses: open_app("MyApp")
AI uses: scan_desktop()

// Find and fill the login form
AI uses: fill_form({"Email": "user@test.com", "Password": "correct123"}, windowLabel: "MyApp")
AI uses: click_element("Log in", windowLabel: "MyApp")
AI uses: wait_seconds(2)

// Verify success
AI uses: screenshot_region(...)
AI uses: read_window_text("MyApp")
// Checks for "Welcome" or "Dashboard" text

// Test invalid credentials
AI uses: click_element("Log out")
AI uses: fill_form({"Email": "user@test.com", "Password": "wrong"}, windowLabel: "MyApp")
AI uses: click_element("Log in")
AI uses: wait_seconds(2)

// Verify error message
AI uses: find_element("Invalid", windowLabel: "MyApp")
AI uses: screenshot_region(...)
// Confirms error message is visible and correctly styled
```

## Example: Testing Navigation

> "Click through every menu item and verify each page loads without errors."

```
// Get all buttons/tabs
AI uses: get_window_details("MyApp", kindFilter: "button")
// Returns: ["Home", "Settings", "Profile", "Help", "About"]

// Click each one and verify
for each button:
    AI uses: click_element(button)
    AI uses: wait_seconds(1)
    AI uses: screenshot_region(...)
    AI uses: read_window_text("MyApp")
    // Verify: no error messages, expected content is visible
```

## Example: Form Validation Testing

> "Try submitting the registration form with various invalid inputs."

```
// Empty form submission
AI uses: click_element("Register")
AI uses: find_element("required", windowLabel: "MyApp")
// Verify validation messages appear

// Invalid email
AI uses: fill_form({"Email": "notanemail"})
AI uses: click_element("Register")
AI uses: find_element("valid email", windowLabel: "MyApp")

// Password too short
AI uses: fill_form({"Email": "user@test.com", "Password": "123"})
AI uses: click_element("Register")
AI uses: find_element("at least", windowLabel: "MyApp")
```

## Key Advantage

The AI doesn't need pre-written test scripts. Describe what to test in English and it figures out the clicks, inputs, and assertions on its own. It adapts if the UI changes because it finds elements by text, not by fragile selectors.
