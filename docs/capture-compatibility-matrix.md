# Capture Compatibility Matrix

This matrix tracks real-world capture reliability by app type and fallback strategy.

## App Type Expectations

| App Type | Typical Apps | Primary Strategy | Secondary Strategy | Fallback | Notes |
|---|---|---|---|---|---|
| Editor | VS Code, Visual Studio, Notepad, Word | Ctrl+C | Ctrl+Insert | WM_COPY, UI Automation | Usually reliable with selected text. |
| Browser | Chrome, Edge, Firefox, Brave | Ctrl+C | Ctrl+Insert | WM_COPY, UI Automation | Rich web apps may override keyboard behavior. |
| Terminal | Windows Terminal, PowerShell, cmd | Ctrl+Insert | Ctrl+C | WM_COPY, UI Automation | Ctrl+C can be intercepted by terminal copy/interrupt behavior. |
| Chat | Teams, Slack, Discord, Telegram | Ctrl+C | Ctrl+Insert | WM_COPY, UI Automation | Embedded editors can vary by app version. |
| Other | Misc desktop apps | Ctrl+C | Ctrl+Insert | WM_COPY, UI Automation | Validate app-specific behavior manually. |

## Failure Diagnostics

When capture fails, Spark now reports:
- Foreground process name
- Detected app type
- Last strategy used
- Whether UI Automation fallback was attempted

Use this data from tray warnings and debug logs to expand this matrix over time.

## Test Checklist Per App

1. Select plain text and capture.
2. Select multiline text and capture.
3. Verify result replace behavior.
4. Trigger capture while modifier keys are held.
5. Record strategy and outcome in this matrix.
