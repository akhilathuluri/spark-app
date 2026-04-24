using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Diagnostics;
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

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        TOKEN_INFORMATION_CLASS tokenInformationClass,
        out TOKEN_ELEVATION tokenInformation,
        int tokenInformationLength,
        out int returnLength);

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
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    private static readonly CaptureTuning Tuning = CaptureTuning.Load();

    private sealed record CaptureTuning(
        int ModifiersReleaseTimeoutMs,
        int BaseClipboardWaitMs,
        int RetryClipboardWaitIncrementMs,
        int InterAttemptDelayMs,
        int OverallCaptureTimeoutMs)
    {
        public static CaptureTuning Load()
        {
            return new CaptureTuning(
                ModifiersReleaseTimeoutMs: ReadInt("Spark_CAPTURE_RELEASE_TIMEOUT_MS", 240, 80, 3000),
                BaseClipboardWaitMs: ReadInt("Spark_CAPTURE_BASE_WAIT_MS", 220, 80, 2000),
                RetryClipboardWaitIncrementMs: ReadInt("Spark_CAPTURE_RETRY_INC_MS", 120, 20, 1000),
                InterAttemptDelayMs: ReadInt("Spark_CAPTURE_INTER_ATTEMPT_MS", 24, 5, 500),
                OverallCaptureTimeoutMs: ReadInt("Spark_CAPTURE_OVERALL_TIMEOUT_MS", 1400, 300, 8000));
        }

        private static int ReadInt(string name, int fallback, int min, int max)
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable(name), out var parsed))
                return fallback;
            return Math.Clamp(parsed, min, max);
        }
    }

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenElevation = 20
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

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
        // Releasing modifiers that are not currently held can trigger unwanted UI modes
        // in some apps (for example, synthetic Alt-up can open keytips in Notepad).
        // Only release keys that are actually down right now.
        var inputs = new List<INPUT>(5);

        if (IsKeyHeld(VK_SHIFT))
            inputs.Add(KeyUp(VK_SHIFT));

        if (IsKeyHeld(VK_ALT))
            inputs.Add(KeyUp(VK_ALT));

        if (IsKeyHeld(VK_LWIN))
            inputs.Add(KeyUp(VK_LWIN));

        if (IsKeyHeld(VK_RWIN))
            inputs.Add(KeyUp(VK_RWIN));

        if (IsKeyHeld(VK_CONTROL))
            inputs.Add(KeyUp(VK_CONTROL));

        return TrySendInput(inputs.ToArray(), "ReleaseModifiers");
    }

    private static bool IsKeyHeld(int virtualKey) => GetAsyncKeyState(virtualKey) < 0;

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

    private static async Task<string?> WaitForClipboardCaptureAsync(int timeoutMs, uint previousSeq, string? previousClipboard)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var currentSeq = GetClipboardSequenceNumber();
                if (!Clipboard.ContainsText())
                {
                    await Task.Delay(12);
                    continue;
                }

                var text = Clipboard.GetText();
                if (string.IsNullOrWhiteSpace(text))
                {
                    await Task.Delay(12);
                    continue;
                }

                // Ignore stale clipboard content when sequence didn't change.
                if (currentSeq == previousSeq && string.Equals(text, previousClipboard, StringComparison.Ordinal))
                {
                    await Task.Delay(12);
                    continue;
                }

                return text;
            }
            catch
            {
                await Task.Delay(12);
            }
        }

        return null;
    }

    public async Task<string?> GetSelectedTextAsync()
    {
        LogCapture("Capture start");
        var started = Stopwatch.StartNew();

        IntPtr sourceWindow = GetForegroundWindow();
        LogCapture($"Foreground window: 0x{sourceWindow.ToInt64():X}");

        var appProfile = GetAppProfile(sourceWindow);
        GetWindowThreadProcessId(sourceWindow, out var sourcePid);
        var targetAssessment = AssessCaptureTarget(sourceWindow, sourcePid, appProfile);
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
            TargetRisk = targetAssessment.Risk,
            LastStrategy = "None",
            Attempts = 0,
            Success = false,
            UsedUiAutomationFallback = false,
            LastFailureReason = "Capture not attempted",
            UserHint = targetAssessment.UserHint
        };

        if (!targetAssessment.ShouldAttemptCapture)
        {
            LastDiagnostics.LastFailureReason = targetAssessment.Reason;
            LogCapture($"Capture skipped: {targetAssessment.Reason}");
            return null;
        }

        string? previousClipboard = null;
        System.Windows.IDataObject? previousDataObject = null;
        try
        {
            if (Clipboard.ContainsText())
                previousClipboard = Clipboard.GetText();
            previousDataObject = Clipboard.GetDataObject();
        }
        catch { }

        bool released = await WaitForModifiersReleasedAsync(Tuning.ModifiersReleaseTimeoutMs);
        if (!released)
            LogCapture("Modifiers not fully released before capture attempts");

        var strategies = BuildStrategies(sourceWindow, appProfile.AppType);

        string? selected = null;
        uint lastClipboardSeq = GetClipboardSequenceNumber();
        var overallDeadline = DateTime.UtcNow.AddMilliseconds(Tuning.OverallCaptureTimeoutMs);
        for (int attempt = 0; attempt < strategies.Count; attempt++)
        {
            if (DateTime.UtcNow >= overallDeadline)
            {
                LastDiagnostics.LastFailureReason = "Timed out before all capture strategies completed";
                LogCapture("Overall capture timeout reached before strategy execution");
                break;
            }

            LastDiagnostics.Attempts = attempt + 1;
            LastDiagnostics.LastStrategy = strategies[attempt].Name;
            LogCapture($"Capture attempt {attempt + 1}/{strategies.Count} using {strategies[attempt].Name}");

            if (sourceWindow != IntPtr.Zero && !SetForegroundWindow(sourceWindow))
            {
                LogCapture($"SetForegroundWindow failed for 0x{sourceWindow.ToInt64():X}");
            }

            await Task.Delay(attempt == 0 ? 12 : Tuning.InterAttemptDelayMs);

            ReleaseModifiers();
            await Task.Delay(6);

            strategies[attempt].Execute();

            int remainingMs = (int)Math.Max(0, (overallDeadline - DateTime.UtcNow).TotalMilliseconds);
            int attemptBudget = GetAttemptBudgetMs(appProfile.AppType, attempt);
            int timeoutMs = Math.Min(attemptBudget, remainingMs);
            if (timeoutMs <= 0)
            {
                LastDiagnostics.LastFailureReason = "Timed out waiting for clipboard update";
                break;
            }

            var captured = await WaitForClipboardCaptureAsync(timeoutMs, lastClipboardSeq, previousClipboard);
            if (!string.IsNullOrWhiteSpace(captured))
            {
                selected = captured;
                LastDiagnostics.Success = true;
                LastDiagnostics.LastFailureReason = string.Empty;
                LogCapture($"Clipboard capture success on attempt {attempt + 1} ({selected.Length} chars)");
                break;
            }

            LogCapture($"Clipboard capture attempt {attempt + 1} yielded no text");
            LastDiagnostics.LastFailureReason = $"{strategies[attempt].Name} did not produce non-empty clipboard text";
            lastClipboardSeq = GetClipboardSequenceNumber();
        }

        if (string.IsNullOrWhiteSpace(selected))
        {
            LastDiagnostics.UsedUiAutomationFallback = true;
            selected = TryGetSelectedTextViaUiAutomation(allowWholeValueFallback: appProfile.AppType is not "Browser" and not "Chat");
            if (string.IsNullOrWhiteSpace(selected))
            {
                selected = null;
                if (string.IsNullOrWhiteSpace(LastDiagnostics.LastFailureReason))
                    LastDiagnostics.LastFailureReason = "Clipboard and UI Automation capture both failed";
            }
            else
            {
                LastDiagnostics.Success = true;
                LastDiagnostics.LastFailureReason = string.Empty;
            }
        }

        LogCapture(selected == null
            ? $"Capture finished: no selected text ({started.ElapsedMilliseconds} ms)"
            : $"Capture finished: success ({selected.Length} chars, {started.ElapsedMilliseconds} ms)");

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
        var hint = string.IsNullOrWhiteSpace(d.UserHint) ? string.Empty : $", hint: {d.UserHint}";
        return $"App: {d.ProcessName} ({d.AppType}), risk: {d.TargetRisk}, strategy: {d.LastStrategy}, attempts: {d.Attempts}, UIA fallback: {(d.UsedUiAutomationFallback ? "used" : "not used")}, reason: {d.LastFailureReason}{hint}";
    }

    private static List<CaptureStrategy> BuildStrategies(IntPtr sourceWindow, string appType)
    {
        var strategies = new List<CaptureStrategy>();

        // Terminal and remote-desktop hosts often reserve Ctrl+C for their own semantics.
        if (appType is "Terminal" or "RemoteDesktop")
        {
            strategies.Add(new CaptureStrategy("Ctrl+Insert", () => SendCtrlKey(VK_INSERT)));
            strategies.Add(new CaptureStrategy("Ctrl+C", () => SendCtrlKey(VK_C)));
        }
        else if (appType is "Browser" or "Chat")
        {
            strategies.Add(new CaptureStrategy("Ctrl+C", () => SendCtrlKey(VK_C)));
            strategies.Add(new CaptureStrategy("Ctrl+C (delayed retry)", () => SendCtrlKey(VK_C)));
            strategies.Add(new CaptureStrategy("Ctrl+Insert", () => SendCtrlKey(VK_INSERT)));
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

    public static IReadOnlyList<string> GetCaptureStrategyPlanForDiagnostics(string appType)
    {
        return BuildStrategies(IntPtr.Zero, appType)
            .Select(strategy => strategy.Name)
            .ToList();
    }

    private static int GetAttemptBudgetMs(string appType, int attempt)
    {
        var baseWait = Tuning.BaseClipboardWaitMs;

        if (appType is "Browser" or "Chat")
            baseWait += 160;
        else if (appType is "RemoteDesktop")
            baseWait += 260;
        else if (appType is "Terminal")
            baseWait += 80;

        return baseWait + attempt * Tuning.RetryClipboardWaitIncrementMs;
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

            if (lower.Contains("credential") || lower.Contains("logonui") || lower.Contains("lockapp") || lower.Contains("lsass"))
                return new AppProfile("Protected", name);

            if (lower.Contains("mstsc") || lower.Contains("msrdc") || lower.Contains("vmconnect") || lower.Contains("teamviewer") || lower.Contains("anydesk"))
                return new AppProfile("RemoteDesktop", name);

            if (lower.Contains("windowsterminal") || lower.Contains("powershell") || lower == "cmd" || lower == "wt")
                return new AppProfile("Terminal", name);

            if (lower.Contains("code") || lower.Contains("devenv") || lower.Contains("notepad") || lower.Contains("word"))
                return new AppProfile("Editor", name);

            if (lower.Contains("chrome") || lower.Contains("msedge") || lower.Contains("firefox") || lower.Contains("brave"))
                return new AppProfile("Browser", name);

            if (lower.Contains("teams") || lower.Contains("slack") || lower.Contains("discord") || lower.Contains("telegram") || lower.Contains("whatsapp"))
                return new AppProfile("Chat", name);

            return new AppProfile("Other", name);
        }
        catch
        {
            return new AppProfile("Unknown", "unknown");
        }
    }

    private static CaptureTargetAssessment AssessCaptureTarget(IntPtr hwnd, uint processId, AppProfile appProfile)
    {
        if (hwnd == IntPtr.Zero)
        {
            return CaptureTargetAssessment.Blocked(
                "Unknown",
                "No foreground window was available.",
                "Click into the app and select text before pressing the hotkey.");
        }

        if (appProfile.AppType == "Protected")
        {
            return CaptureTargetAssessment.Blocked(
                "Protected",
                "The foreground window is a protected Windows security surface.",
                "Spark cannot capture from protected system or credential windows.");
        }

        if (TryFocusedElementIsSensitive(out var sensitiveReason))
        {
            return CaptureTargetAssessment.Blocked(
                "SensitiveField",
                sensitiveReason,
                "Spark does not capture from password or protected input fields.");
        }

        if (processId != 0 && IsProcessElevated(processId) && !IsCurrentProcessElevated())
        {
            return CaptureTargetAssessment.Blocked(
                "ElevatedTarget",
                "The target app is running as administrator while Spark is not.",
                "Run Spark as administrator too, or use Spark with non-admin apps.");
        }

        if (appProfile.AppType == "RemoteDesktop")
        {
            return CaptureTargetAssessment.Allowed(
                "RemoteDesktop",
                "Remote desktop and virtual machine windows may swallow copy hotkeys; Ctrl+Insert is tried first.");
        }

        return CaptureTargetAssessment.Allowed("Normal", string.Empty);
    }

    private static bool TryFocusedElementIsSensitive(out string reason)
    {
        reason = string.Empty;

        try
        {
            var focused = AutomationElement.FocusedElement;
            if (focused == null)
                return false;

            var isPassword = focused.Current.IsPassword;
            if (isPassword)
            {
                reason = "The focused control reports itself as a password field.";
                return true;
            }

            var automationId = focused.Current.AutomationId ?? string.Empty;
            var name = focused.Current.Name ?? string.Empty;
            var className = focused.Current.ClassName ?? string.Empty;
            var combined = $"{automationId} {name} {className}".ToLowerInvariant();

            if (combined.Contains("password") || combined.Contains("passcode") || combined.Contains("pin"))
            {
                reason = "The focused control appears to be a credential field.";
                return true;
            }
        }
        catch (Exception ex)
        {
            LogCapture($"Sensitive-field check failed: {ex.Message}");
        }

        return false;
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static bool IsProcessElevated(uint processId)
    {
        var processHandle = IntPtr.Zero;
        var tokenHandle = IntPtr.Zero;

        try
        {
            processHandle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
            if (processHandle == IntPtr.Zero)
                return false;

            if (!OpenProcessToken(processHandle, TOKEN_QUERY, out tokenHandle))
                return false;

            var elevation = new TOKEN_ELEVATION();
            return GetTokenInformation(
                    tokenHandle,
                    TOKEN_INFORMATION_CLASS.TokenElevation,
                    out elevation,
                    Marshal.SizeOf<TOKEN_ELEVATION>(),
                    out _)
                && elevation.TokenIsElevated != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (tokenHandle != IntPtr.Zero)
                CloseHandle(tokenHandle);

            if (processHandle != IntPtr.Zero)
                CloseHandle(processHandle);
        }
    }

    private static string? TryGetSelectedTextViaUiAutomation(bool allowWholeValueFallback)
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

            if (allowWholeValueFallback &&
                focused.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj) &&
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
    private readonly record struct CaptureTargetAssessment(
        bool ShouldAttemptCapture,
        string Risk,
        string Reason,
        string UserHint)
    {
        public static CaptureTargetAssessment Allowed(string risk, string hint) =>
            new(true, risk, string.Empty, hint);

        public static CaptureTargetAssessment Blocked(string risk, string reason, string hint) =>
            new(false, risk, reason, hint);
    }
}
