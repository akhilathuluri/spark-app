using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Spark.Services;

/// <summary>
/// Developer-only analytics via PostHog.
/// Tracks action usage, streaming chunk counts, durations, and error types.
/// No user text, no selected content, no PII is ever sent.
///
/// To enable: open appsettings.json and set posthog.enabled = true, posthog.apiKey = your key.
/// Analytics are silently disabled (no-op) when not enabled.
/// </summary>
public sealed class AnalyticsService : IDisposable
{
    // ── State ────────────────────────────────────────────────────────────────
    private readonly HttpClient _http;
    private readonly string     _distinctId;   // anonymous per-installation GUID — no PII
    private readonly string     _appVersion;
    private readonly bool       _enabled;
    private readonly string     _posthogApiKey;
    private readonly string     _posthogHost;

    public AnalyticsService()
    {
        _http       = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _distinctId = GetOrCreateDeviceId();
        _appVersion = System.Reflection.Assembly
                          .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        // Load config from appsettings.json
        (_enabled, _posthogApiKey, _posthogHost) = LoadConfig();
    }

    private static (bool enabled, string apiKey, string host) LoadConfig()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Developer override takes priority over the public default.
            // appsettings.developer.json is gitignored and never shipped in releases.
            var devPath = Path.Combine(baseDir, "appsettings.developer.json");
            var pubPath = Path.Combine(baseDir, "appsettings.json");
            var appSettingsPath = File.Exists(devPath) ? devPath : pubPath;

            if (File.Exists(appSettingsPath))
            {
                var json = File.ReadAllText(appSettingsPath);
                using var doc = JsonDocument.Parse(json);
                
                var root = doc.RootElement;
                if (root.TryGetProperty("posthog", out var phEl))
                {
                    var enabled = phEl.TryGetProperty("enabled", out var enabledEl) && enabledEl.GetBoolean();
                    var apiKey = phEl.TryGetProperty("apiKey", out var keyEl) ? keyEl.GetString() : "";
                    var host = phEl.TryGetProperty("host", out var hostEl) ? hostEl.GetString() : "https://eu.i.posthog.com";
                    
                    return (enabled && !string.IsNullOrWhiteSpace(apiKey), apiKey ?? "", host ?? "https://eu.i.posthog.com");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("AnalyticsService.LoadConfig", ex);
        }

        return (false, "", "https://eu.i.posthog.com");
    }

    // ── Public tracking methods ──────────────────────────────────────────────

    /// <summary>Called once when the application starts successfully.</summary>
    public void TrackAppStarted() =>
        Capture("app_started", new { app_version = _appVersion });

    /// <summary>Called when an AI action begins — before the HTTP request is sent.</summary>
    public void TrackActionStarted(
        string actionId, string actionName, bool isBuiltIn,
        string provider, string model) =>
        Capture("action_started", new
        {
            action_id   = actionId,
            action_name = actionName,
            is_builtin  = isBuiltIn,
            provider,
            model,
            app_version = _appVersion
        });

    /// <summary>
    /// Called when AI streaming completes successfully.
    /// streaming_chunks ≈ number of SSE delta events (rough proxy for output token count).
    /// result_chars is the total character length of the final output.
    /// </summary>
    public void TrackActionCompleted(
        string actionId, bool isBuiltIn,
        int streamingChunks, int resultChars, long durationMs,
        string provider) =>
        Capture("action_completed", new
        {
            action_id        = actionId,
            is_builtin       = isBuiltIn,
            streaming_chunks = streamingChunks,
            result_chars     = resultChars,
            duration_ms      = durationMs,
            provider,
            app_version      = _appVersion
        });

    /// <summary>
    /// Called when an action fails with an unhandled exception.
    /// Only the exception type name is sent — never the message (which may contain user data).
    /// </summary>
    public void TrackActionError(
        string actionId, bool isBuiltIn,
        string errorType, string provider,
        Dictionary<string, string>? metadata = null) =>
        Capture("action_error", new
        {
            action_id  = actionId,
            is_builtin = isBuiltIn,
            error_type = errorType,   // e.g. "HttpRequestException" — no user text
            provider,
            metadata,
            app_version = _appVersion
        });

    public void TrackDiagnosticError(string context, string errorType, string correlationId, Dictionary<string, string>? metadata = null) =>
        Capture("diagnostic_error", new
        {
            context,
            error_type = errorType,
            correlation_id = correlationId,
            metadata,
            app_version = _appVersion
        });

    public void TrackCrashReported(string source, string errorType, string correlationId, Dictionary<string, string>? metadata = null) =>
        Capture("crash_reported", new
        {
            source,
            error_type = errorType,
            correlation_id = correlationId,
            metadata,
            app_version = _appVersion
        });

    public void TrackCaptureFailed(string correlationId, Dictionary<string, string> metadata) =>
        Capture("capture_failed", new
        {
            correlation_id = correlationId,
            metadata,
            app_version = _appVersion
        });

    // ── Internal ─────────────────────────────────────────────────────────────

    private void Capture<T>(string eventName, T properties)
    {
        if (!_enabled) return;

        // Fire-and-forget on a thread-pool thread.
        // Analytics must never throw, slow down, or block the UI.
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new
                {
                    api_key     = _posthogApiKey,
                    @event      = eventName,
                    distinct_id = _distinctId,
                    properties,
                    timestamp   = DateTime.UtcNow.ToString("O")
                };

                var body    = JsonSerializer.Serialize(payload);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                await _http.PostAsync($"{_posthogHost}/capture/", content).ConfigureAwait(false);
            }
            catch { /* analytics must never surface */ }
        });
    }

    /// <summary>
    /// Returns a stable anonymous device identifier stored in AppData.
    /// A random GUID is generated once per installation — no user-identifying data.
    /// </summary>
    private static string GetOrCreateDeviceId()
    {
        try
        {
            var dir  = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spark");
            var path = Path.Combine(dir, "device_id");
            Directory.CreateDirectory(dir);

            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (!string.IsNullOrWhiteSpace(existing)) return existing;
            }

            var id = Guid.NewGuid().ToString();
            File.WriteAllText(path, id);
            return id;
        }
        catch
        {
            return Guid.NewGuid().ToString(); // ephemeral fallback — no disk access
        }
    }

    public void Dispose() => _http.Dispose();
}
