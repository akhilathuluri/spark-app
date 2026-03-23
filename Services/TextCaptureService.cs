using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Captures selected text from any foreground application using several copy
/// strategies (Ctrl+C, Ctrl+Insert, WM_COPY), then restores the clipboard.
/// Also provides cursor position for popup placement.
/// </summary>
public class TextCaptureService
{
    private static readonly bool CaptureDiagnosticsEnabled =
        !string.Equals(Environment.GetEnvironmentVariable("Spark_CAPTURE_DIAGNOSTICS"), "0", StringComparison.OrdinalIgnoreCase);

    public CaptureDiagnostics LastDiagnostics { get; private set; } = new();
    public TextSelectionContext? LastSelectionContext { get; private set; }

    // P/Invoke
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    // Structures
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL, wParamH;
    }

    // Constants
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_ALT = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_C = 0x43;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_V = 0x56;
    private const uint WM_COPY = 0x0301;

    private static INPUT KeyDown(ushort vk) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk } }
    };

    private static INPUT KeyUp(ushort vk) => new INPUT
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
    };

    private static bool SendCtrlKey(ushort key)
    {
        return TrySendInput(
            new[] { KeyDown(VK_CONTROL), KeyDown(key), KeyUp(key), KeyUp(VK_CONTROL) },
            $"Ctrl+{key}");
    }

    private static bool ReleaseModifiers()
    {
        var inputs = new[]
        {
            KeyUp(VK_SHIFT),
            KeyUp(VK_ALT),
            KeyUp(VK_LWIN),
            KeyUp(VK_RWIN),
            KeyUp(VK_CONTROL)
        };
        return TrySendInput(inputs, "ReleaseModifiers");
    }

    private static bool TrySendInput(INPUT[] inputs, string context)
    {
        if (inputs.Length == 0)
            return true;

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == (uint)inputs.Length)
            return true;

        var error = Marshal.GetLastWin32Error();
        LogCapture($"SendInput failed in {context}: sent {sent}/{inputs.Length}, win32={error}");
        return false;
    }

    private static async Task<bool> WaitForModifiersReleasedAsync(int timeoutMs = 800)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            bool held = GetAsyncKeyState(VK_CONTROL) < 0 ||
                        GetAsyncKeyState(VK_SHIFT) < 0 ||
                        GetAsyncKeyState(VK_ALT) < 0 ||
                        GetAsyncKeyState(VK_LWIN) < 0 ||
                        GetAsyncKeyState(VK_RWIN) < 0;
            if (!held) return true;
            await Task.Delay(10);
        }

        return false;
    }

    private static async Task<string?> WaitForClipboardCaptureAsync(int timeoutMs)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            try
            {
                if (!Clipboard.ContainsText())
                    continue;

                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                return text;
            }
            catch { }
        }

        return null;
    }

    public async Task<string?> GetSelectedTextAsync()
    {
        LogCapture("Capture start");

        IntPtr sourceWindow = GetForegroundWindow();
        LogCapture($"Foreground window: 0x{sourceWindow.ToInt64():X}");

        var appProfile = GetAppProfile(sourceWindow);
        GetWindowThreadProcessId(sourceWindow, out var sourcePid);
        LastSelectionContext = new TextSelectionContext
        {
            WindowHandle = sourceWindow,
            ProcessId = sourcePid,
            ProcessName = appProfile.ProcessName,
            CapturedUtc = DateTime.UtcNow
        };

        LastDiagnostics = new CaptureDiagnostics
        {
            AppType = appProfile.AppType,
            ProcessName = appProfile.ProcessName,
            LastStrategy = "None",
            Attempts = 0,
            Success = false,
            UsedUiAutomationFallback = false
        };

        string? previousClipboard = null;
        System.Windows.IDataObject? previousDataObject = null;
        try
        {
            if (Clipboard.ContainsText())
                previousClipboard = Clipboard.GetText();
            previousDataObject = Clipboard.GetDataObject();
        }
        catch { }

        await WaitForModifiersReleasedAsync(500);

        var strategies = BuildStrategies(sourceWindow, appProfile.AppType);

        string? selected = null;
        for (int attempt = 0; attempt < strategies.Count; attempt++)
        {
            LastDiagnostics.Attempts = attempt + 1;
            LastDiagnostics.LastStrategy = strategies[attempt].Name;
            LogCapture($"Capture attempt {attempt + 1}/{strategies.Count} using {strategies[attempt].Name}");

            if (sourceWindow != IntPtr.Zero && !SetForegroundWindow(sourceWindow))
            {
                LogCapture($"SetForegroundWindow failed for 0x{sourceWindow.ToInt64():X}");
            }

            await Task.Delay(attempt == 0 ? 12 : 45);

            ReleaseModifiers();
            await Task.Delay(8);

            try { Clipboard.Clear(); } catch { }

            strategies[attempt].Execute();

            var captured = await WaitForClipboardCaptureAsync(320 + attempt * 180);
            if (!string.IsNullOrWhiteSpace(captured))
            {
                selected = captured;
                LastDiagnostics.Success = true;
                LogCapture($"Clipboard capture success on attempt {attempt + 1} ({selected.Length} chars)");
                break;
            }

            LogCapture($"Clipboard capture attempt {attempt + 1} yielded no text");
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            LastDiagnostics.UsedUiAutomationFallback = true;
            selected = TryGetSelectedTextViaUiAutomation();
            if (string.IsNullOrWhiteSpace(selected))
                selected = null;
            else
                LastDiagnostics.Success = true;
        }

        LogCapture(selected == null ? "Capture finished: no selected text" : $"Capture finished: success ({selected.Length} chars)");

        try
        {
            if (previousDataObject != null)
                Clipboard.SetDataObject((object)previousDataObject, true);
            else if (previousClipboard != null)
                Clipboard.SetText(previousClipboard);
            else
                Clipboard.Clear();
        }
        catch { }

        return selected;
    }

    public string GetFailureDiagnosticSummary()
    {
        var d = LastDiagnostics;
        return $"App: {d.ProcessName} ({d.AppType}), strategy: {d.LastStrategy}, UIA fallback: {(d.UsedUiAutomationFallback ? "used" : "not used")}";
    }

    private static List<CaptureStrategy> BuildStrategies(IntPtr sourceWindow, string appType)
    {
        var strategies = new List<CaptureStrategy>();

        // Terminal apps often honor Ctrl+Insert more consistently than Ctrl+C.
        if (appType == "Terminal")
        {
            strategies.Add(new CaptureStrategy("Ctrl+Insert", () => SendCtrlKey(VK_INSERT)));
            strategies.Add(new CaptureStrategy("Ctrl+C", () => SendCtrlKey(VK_C)));
        }
        else
        {
            strategies.Add(new CaptureStrategy("Ctrl+C", () => SendCtrlKey(VK_C)));
            strategies.Add(new CaptureStrategy("Ctrl+Insert", () => SendCtrlKey(VK_INSERT)));
        }

        strategies.Add(new CaptureStrategy("WM_COPY", () =>
        {
            if (sourceWindow == IntPtr.Zero)
                return;

            var result = SendMessage(sourceWindow, WM_COPY, IntPtr.Zero, IntPtr.Zero);
            if (result == IntPtr.Zero)
                LogCapture("WM_COPY sent with zero result; target may not support copy message.");
        }));

        strategies.Add(new CaptureStrategy("Ctrl+C (retry)", () => _ = SendCtrlKey(VK_C)));
        return strategies;
    }

    private static AppProfile GetAppProfile(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero)
                return new AppProfile("Unknown", "unknown");

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return new AppProfile("Unknown", "unknown");

            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            var name = process.ProcessName;
            var lower = name.ToLowerInvariant();

            if (lower.Contains("windowsterminal") || lower.Contains("powershell") || lower == "cmd" || lower == "wt")
                return new AppProfile("Terminal", name);

            if (lower.Contains("code") || lower.Contains("devenv") || lower.Contains("notepad") || lower.Contains("word"))
                return new AppProfile("Editor", name);

            if (lower.Contains("chrome") || lower.Contains("msedge") || lower.Contains("firefox") || lower.Contains("brave"))
                return new AppProfile("Browser", name);

            if (lower.Contains("teams") || lower.Contains("slack") || lower.Contains("discord") || lower.Contains("telegram"))
                return new AppProfile("Chat", name);

            return new AppProfile("Other", name);
        }
        catch
        {
            return new AppProfile("Unknown", "unknown");
        }
    }

    private static string? TryGetSelectedTextViaUiAutomation()
    {
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null)
            {
                LogCapture("UIA fallback: focused element is null");
                return null;
            }

            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out var textPatternObj) &&
                textPatternObj is TextPattern textPattern)
            {
                var ranges = textPattern.GetSelection();
                if (ranges != null && ranges.Length > 0)
                {
                    var value = ranges[0].GetText(-1);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        LogCapture($"UIA TextPattern success ({value.Length} chars)");
                        return value;
                    }
                }
            }

            if (focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj) &&
                valuePatternObj is ValuePattern valuePattern)
            {
                var value = valuePattern.Current.Value;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    LogCapture($"UIA ValuePattern fallback success ({value.Length} chars)");
                    return value;
                }
            }

            LogCapture("UIA fallback: no usable selected text from TextPattern/ValuePattern");
        }
        catch
        {
            LogCapture("UIA fallback threw an exception");
        }

        return null;
    }

    public async Task ReplaceSelectedTextAsync(string newText)
    {
        try { Clipboard.SetText(newText); }
        catch { return; }

        await Task.Delay(80);
        SendCtrlKey(VK_V);
        await Task.Delay(80);
    }

    public async Task<bool> ReplaceSelectedTextSafelyAsync(string newText, bool force)
    {
        if (!force)
        {
            await TryRestoreSourceFocusAsync();

            var validation = ValidateReplaceTarget();
            if (!validation.IsSafe)
                return false;
        }

        await ReplaceSelectedTextAsync(newText);
        return true;
    }

    public ReplaceValidationResult ValidateReplaceTarget()
    {
        var source = LastSelectionContext;
        if (source == null)
        {
            return new ReplaceValidationResult
            {
                IsSafe = false,
                FocusChanged = true,
                Reason = "Selection context is unavailable.",
                SourceProcessName = "unknown",
                CurrentProcessName = GetCurrentForegroundProcessName()
            };
        }

        var currentWindow = GetForegroundWindow();
        GetWindowThreadProcessId(currentWindow, out var currentPid);
        var currentName = GetProcessName(currentPid);

        bool sameWindow = currentWindow == source.WindowHandle;
        bool sameProcess = currentPid != 0 && currentPid == source.ProcessId;
        bool safe = sameWindow || sameProcess;

        return new ReplaceValidationResult
        {
            IsSafe = safe,
            FocusChanged = !safe,
            SourceProcessName = source.ProcessName,
            CurrentProcessName = currentName,
            Reason = safe ? "Foreground matches source." : "Foreground focus changed since text capture."
        };
    }

    public System.Windows.Point GetCursorPosition()
    {
        if (!GetCursorPos(out var p))
        {
            LogCapture("GetCursorPos failed; falling back to (0,0)");
            return new System.Windows.Point(0, 0);
        }

        return new System.Windows.Point(p.X, p.Y);
    }

    private static void LogCapture(string message)
    {
        if (!CaptureDiagnosticsEnabled) return;
        DebugLogService.LogMessage($"[CAPTURE] {message}");
    }

    private static string GetCurrentForegroundProcessName()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out var pid);
        return GetProcessName(pid);
    }

    private static string GetProcessName(uint pid)
    {
        if (pid == 0) return "unknown";

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task TryRestoreSourceFocusAsync()
    {
        var source = LastSelectionContext;
        if (source == null || source.WindowHandle == IntPtr.Zero)
            return;

        if (!SetForegroundWindow(source.WindowHandle))
            LogCapture($"SetForegroundWindow restore failed for 0x{source.WindowHandle.ToInt64():X}");

        await Task.Delay(60);
    }

    private readonly record struct CaptureStrategy(string Name, Action Execute);
    private readonly record struct AppProfile(string AppType, string ProcessName);
}
