namespace Scriptly.Services;

/// <summary>
/// Rate-limits hotkey presses to prevent window spam.
/// Wraps the hotkey callback with a configurable debounce timer and logs suppressed attempts.
/// Non-breaking: Can be integrated by wrapping the existing HotkeyPressed event without changing GlobalHotkeyService.
/// </summary>
public class HotkeyThrottleService
{
    private DateTime _lastHotkeySuppressedUtc = DateTime.MinValue;
    private int _suppressedCount = 0;
    private int _consecutiveSuppressions = 0;

    /// <summary>
    /// Debounce interval in milliseconds. Default 500ms — prevents rapid successive presses
    /// from spawning multiple action panel windows.
    /// Can be configured via environment variable SCRIPTLY_HOTKEY_THROTTLE_MS.
    /// Clamped to [100ms, 2000ms] to prevent edge cases.
    /// </summary>
    public int ThrottleTimeMs { get; private set; } = 500;

    /// <summary>
    /// Total presses suppressed since app startup (for diagnostics/telemetry).
    /// </summary>
    public int TotalSuppressed => _suppressedCount;

    public HotkeyThrottleService()
    {
        // Read optional env var override
        if (int.TryParse(Environment.GetEnvironmentVariable("SCRIPTLY_HOTKEY_THROTTLE_MS"), out var ms))
        {
            ThrottleTimeMs = Math.Clamp(ms, 100, 2000);
            DebugLogService.LogMessage($"HotkeyThrottleService: throttle time set to {ThrottleTimeMs}ms from env var");
        }
    }

    /// <summary>
    /// Call this from the hotkey press handler. Returns true if the press should be processed,
    /// false if it should be suppressed due to rate limiting.
    /// </summary>
    public bool ShouldProcess()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastHotkeySuppressedUtc).TotalMilliseconds;

        if (elapsed < ThrottleTimeMs)
        {
            _suppressedCount++;
            _consecutiveSuppressions++;

            // Log periodic summaries so debug logs don't explode with suppression noise
            if (_suppressedCount % 10 == 0)
            {
                DebugLogService.LogMessage($"HotkeyThrottleService: {_consecutiveSuppressions} consecutive presses suppressed (total: {_suppressedCount})");
            }

            return false;
        }

        _lastHotkeySuppressedUtc = now;

        // Log if we had a suppression burst, so user knows what happened
        if (_consecutiveSuppressions > 5)
        {
            DebugLogService.LogMessage($"HotkeyThrottleService: suppression burst ended ({_consecutiveSuppressions} suppressed, now processing)");
        }

        _consecutiveSuppressions = 0;
        return true;
    }

    /// <summary>
    /// Reset state (called on graceful shutdown or when hotkey config changes).
    /// </summary>
    public void Reset()
    {
        _lastHotkeySuppressedUtc = DateTime.MinValue;
        _suppressedCount = 0;
        _consecutiveSuppressions = 0;
    }
}
