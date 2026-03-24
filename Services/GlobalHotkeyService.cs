using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Spark.Services;

/// <summary>
/// Detects a system-wide hotkey using a Low-Level Keyboard Hook (WH_KEYBOARD_LL).
/// Works universally in Electron apps (VS Code, Telegram, Notion),
/// Chromium browsers (Chrome, Edge), and every other application.
///
/// Critical design rules for WH_KEYBOARD_LL:
///  1. The callback MUST return within the LowLevelHooksTimeout (default 300 ms).
///     Heavy work (clipboard, WPF calls) is dispatched asynchronously so the hook
///     returns (IntPtr)1 immediately.
///  2. GetAsyncKeyState is used instead of GetKeyState for reliable real-time
///     modifier state inside the hook callback.
///  3. GetModuleHandle(null) returns the current exe handle — most reliable in .NET.
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName); // null → current exe

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // ── Constants ────────────────────────────────────────────────────────────

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN     = 0x0100;
    private const int WM_KEYUP       = 0x0101;
    private const int WM_SYSKEYDOWN  = 0x0104;
    private const int WM_SYSKEYUP    = 0x0105;

    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT   = 0x10;
    private const int VK_ALT     = 0x12;
    private const int VK_LWIN    = 0x5B;
    private const int VK_RWIN    = 0x5C;

    // Set in KBDLLHOOKSTRUCT.flags when injected by SendInput — skip our own events
    private const uint LLKHF_INJECTED = 0x10;

    // ── State ────────────────────────────────────────────────────────────────

    public event Action? HotkeyPressed;

    private LowLevelKeyboardProc? _hookProc;  // field keeps delegate alive (prevents GC)
    private IntPtr _hookHandle = IntPtr.Zero;
    private uint   _targetVk;
    private bool   _needCtrl, _needShift, _needAlt, _needWin;
    private bool   _hotkeyDown;
    private bool   _isSuspended;

    // ── Public API ───────────────────────────────────────────────────────────

    public void Initialize(Window helperWindow) { }  // kept for API compat — no longer needed

    public void SetSuspended(bool suspended)
    {
        _isSuspended = suspended;
        if (suspended)
            _hotkeyDown = false;
    }

    public bool Register(string modifiers, string key)
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        _needCtrl = _needShift = _needAlt = _needWin = false;
        foreach (var part in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.Trim().ToLowerInvariant())
            {
                case "ctrl": case "control": _needCtrl  = true; break;
                case "shift":                _needShift = true; break;
                case "alt":                  _needAlt   = true; break;
                case "win":                  _needWin   = true; break;
            }
        }

        if (!Enum.TryParse<Key>(key.Trim(), true, out var wpfKey))
            return false;

        _targetVk   = (uint)KeyInterop.VirtualKeyFromKey(wpfKey);
        _hotkeyDown = false;

        _hookProc   = HookCallback;
        // dwThreadId = 0 → system-wide; GetModuleHandle(null) = current exe module
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
        return _hookHandle != IntPtr.Zero;
    }

    // ── Hook Callback ────────────────────────────────────────────────────────
    // MUST return as fast as possible — never do clipboard or WPF work here.

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode < 0)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            if (_isSuspended)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var kb = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            // Skip events injected by our own SendInput (Ctrl+C / Ctrl+V in TextCaptureService)
            if ((kb.flags & LLKHF_INJECTED) != 0)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            if (kb.vkCode != _targetVk)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            bool isDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
            bool isUp   = wParam == (IntPtr)WM_KEYUP   || wParam == (IntPtr)WM_SYSKEYUP;

            if (isDown)
            {
                // Require configured modifiers to be present, but ignore extra pressed
                // modifiers. This reduces misses in apps with additional key states.
                bool ctrlDown  = GetAsyncKeyState(VK_CONTROL) < 0;
                bool shiftDown = GetAsyncKeyState(VK_SHIFT)   < 0;
                bool altDown   = GetAsyncKeyState(VK_ALT)     < 0;
                bool winDown   = (GetAsyncKeyState(VK_LWIN) < 0) || (GetAsyncKeyState(VK_RWIN) < 0);

                bool modMatch =
                    (!_needCtrl  || ctrlDown) &&
                    (!_needShift || shiftDown) &&
                    (!_needAlt   || altDown) &&
                    (!_needWin   || winDown);

                if (modMatch)
                {
                    if (!_hotkeyDown)  // only on first press, not auto-repeat
                    {
                        _hotkeyDown = true;
                        RaiseHotkeyPressedAsync();
                    }

                    // Swallow so target apps don't execute their own handlers.
                    return (IntPtr)1;   // swallow keydown (including auto-repeat)
                }
            }
            else if (isUp && _hotkeyDown)
            {
                _hotkeyDown = false;
                return (IntPtr)1;       // swallow matching keyup
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Hooks must fail open; never let exceptions escape callback.
            DebugLogService.LogError("GlobalHotkeyService.HookCallback", ex);
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
    }

    private void RaiseHotkeyPressedAsync()
    {
        try
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null)
                return;

            dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    try
                    {
                        HotkeyPressed?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        DebugLogService.LogError("GlobalHotkeyService.HotkeyPressed", ex);
                    }
                }));
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("GlobalHotkeyService.RaiseHotkeyPressedAsync", ex);
        }
    }

    // ── Cleanup ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
