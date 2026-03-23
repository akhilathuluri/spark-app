# Spark
A native Windows desktop app that transforms selected text with AI — from any application, without breaking your flow.
Select text anywhere → press the hotkey → choose an action → get the result in seconds.

---

## How It Works

1. Select any text in any application (VS Code, Chrome, Notepad, Word, etc.)
2. Press the global hotkey (`Ctrl+Shift+Space` by default)
3. A floating action panel appears near your cursor
4. Pick an action (keyboard or mouse)
5. The AI result appears in a result window
6. Click **Replace** to swap the original text in-place, or **Copy** to grab it

Spark lives entirely in the system tray — no taskbar entry, no open windows when idle.

---

## Features

- **Global hotkey** — works while any app is in focus
- **Smart action ordering** — detects whether you selected code, an email, a long paragraph, or a short sentence and reorders actions accordingly
- **12 built-in actions** — Fix Grammar, Summarize, Translate, Expand, Shorten, Change Tone, Rewrite, Explain, Bullet Points, Casual Tone, Improve Writing, Explain Code
- **Custom actions** — create your own actions with a name, icon, description, and a prompt using `{text}` as the placeholder
- **Two AI providers** — OpenRouter (access to hundreds of models) or Groq (fast inference)
- **In-place Replace** — sends Ctrl+V back to the source app to replace the original selection
- **Keyboard navigation** — ↑/↓ arrows to browse actions, Enter to execute, Escape to dismiss
- **GPU-accelerated animations** — smooth open/close transitions on the floating panels
- **Per-monitor DPI aware** — sharp on any display scaling

---

## Requirements

- Windows 10 or later (x64)
- [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows Desktop Runtime)
- An API key from [OpenRouter](https://openrouter.ai) or [Groq](https://console.groq.com)

---

## Getting Started

### 1. Clone and build

```bash
git clone https://github.com/your-username/Spark.git
cd Spark/Spark
dotnet build -c Release
```

### 2. Run

```bash
dotnet run
# or after build:
.\bin\Release\net10.0-windows\Spark.exe
```

The app starts silently in the system tray (look for the **S** icon near the clock).

### 3. Configure

Right-click the tray icon → **Settings**

| Setting | Description |
|---|---|
| **Provider** | Choose OpenRouter or Groq |
| **API Key** | Paste your API key for the selected provider |
| **Model** | Enter the model name (e.g. `openai/gpt-4o-mini` for OpenRouter, `llama-3.3-70b-versatile` for Groq) |
| **Hotkey** | Choose modifier keys (Ctrl, Shift, Alt, Win) and a trigger key |

### 4. Use it

- Select any text in any app
- Press your hotkey (`Ctrl+Shift+Space` by default)
- Choose an action from the panel
- The result window shows a loading animation while the AI processes
- Click **Copy** or **Replace**, or press **Escape** to dismiss

---

## Built-in Actions

| Icon | Action | Shortcut | What it does |
|------|--------|----------|--------------|
| ✏️ | Fix Grammar | G | Fixes spelling and grammar mistakes |
| ≡ | Summarize | S | Creates a concise summary |
| × | Translate | T | Translates to English (or Spanish if already English) |
| ↕ | Expand Text | E | Elaborates and adds depth |
| ↔ | Shorten Text | H | Makes the text shorter while preserving meaning |
| 🎭 | Change Tone | — | Rewrites in a professional, polished tone |
| ↺ | Rewrite | R | Rewrites for clarity and effectiveness |
| 💡 | Explain | X | Explains in simple terms |
| • | Bullet Points | B | Converts to bullet points |
| 😊 | Casual Tone | — | Rewrites in a casual, friendly tone |
| ⬆ | Improve Writing | I | Improves clarity, flow, and impact |
| 🖥 | Explain Code | C | Explains what the selected code does |

---

## Custom Actions

1. Open **Settings** → scroll to **Custom Actions** → click **Add Action**
2. Fill in:
   - **Icon** — any emoji
   - **Name** — displayed in the action list
   - **Description** — shown as the subtitle
   - **Instructions** — the prompt sent to the AI; use `{text}` where your selected text should go
3. Click **Save**

**Example custom action:**

| Field | Value |
|---|---|
| Icon | 🐛 |
| Name | Find Bugs |
| Description | Identify potential bugs in this code |
| Instructions | `Review the following code and list any bugs or potential issues. Return only the list:\n\n{text}` |

---

## AI Provider Setup

### OpenRouter

1. Sign up at [openrouter.ai](https://openrouter.ai)
2. Go to **Keys** → create a new API key
3. Choose a model — recommended starting points:
   - `openai/gpt-4o-mini` — fast and cheap
   - `anthropic/claude-3.5-haiku` — excellent writing quality
   - `google/gemini-flash-1.5` — very fast, good quality

### Groq

1. Sign up at [console.groq.com](https://console.groq.com)
2. Go to **API Keys** → create a new key
3. Choose a model — recommended:
   - `llama-3.3-70b-versatile` — best quality
   - `llama-3.1-8b-instant` — fastest

---

## Settings File

Settings are stored as JSON at:

```\n%AppData%\Spark\settings.json
```

You can edit this file directly if needed. Delete it to reset to defaults.

## Logs

Spark writes debug logs to:

```\n%AppData%\Spark\debug.log
```

Capture diagnostics logs (`[CAPTURE] ...`) are enabled by default for public builds.
To disable capture diagnostics, set this environment variable before launching Spark:

```powershell
$env:Spark_CAPTURE_DIAGNOSTICS = "0"
```

---

## Project Structure

```
Spark/
├── Models/
│   └── AppSettings.cs          # AppSettings, OpenRouterSettings, GroqSettings, CustomAction, ActionItem
├── Services/
│   ├── AiService.cs            # OpenRouter + Groq API calls
│   ├── ActionsService.cs       # Built-in actions + smart ordering + custom actions
│   ├── GlobalHotkeyService.cs  # Win32 RegisterHotKey via WndProc hook
│   ├── SettingsService.cs      # JSON load/save to %AppData%\Spark
│   ├── TextCaptureService.cs   # Ctrl+C capture + Ctrl+V replace via SendInput
│   └── TrayService.cs          # System tray icon (WinForms NotifyIcon)
├── Windows/
│   ├── ActionPanelWindow.xaml  # Floating command palette
│   ├── ResultWindow.xaml       # AI result with thinking animation + Copy/Replace
│   ├── SettingsWindow.xaml     # Settings UI
│   ├── CustomActionDialog.xaml # Create/edit custom action
│   └── HotkeyWindow.cs         # Hidden window used as Win32 message pump target
├── App.xaml / App.xaml.cs      # Entry point, service wiring, hotkey handler
├── GlobalUsings.cs             # WPF/WinForms namespace alias resolution
├── app.manifest                # PerMonitorV2 DPI awareness declaration
└── Spark.csproj             # .NET 10, WPF + WinForms, Microsoft.Extensions.Http
```

---

## Tech Stack

| Component | Technology |
|---|---|
| UI Framework | WPF (.NET 10) |
| System Tray | Windows Forms `NotifyIcon` |
| Global Hotkey | Win32 `RegisterHotKey` / `WndProc` |
| Text Capture | Win32 `SendInput` (Ctrl+C / Ctrl+V) |
| AI Requests | `HttpClient` via `Microsoft.Extensions.Http` |
| Animations | WPF `DoubleAnimation` + `CubicEase` + `BitmapCache` (GPU) |
| Settings | `System.Text.Json` → `%AppData%\Spark\settings.json` |
| DPI | Per-monitor v2 via `app.manifest` |

---

## Known Limitations

- **Elevated (admin) apps** — Spark cannot send keystrokes to apps running as administrator (e.g. Task Manager) unless Spark itself is also run as admin. This is a Windows UIPI restriction.
- **Some apps intercept Ctrl+C** — terminal emulators or apps with custom keyboard handling may not copy to the clipboard on Ctrl+C.
- **Replace only works if text is still selected** — if the source app loses its selection before the result window closes, Replace will paste at the cursor instead.

---

## License

MIT
