using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Spark.Services.Analytics;

namespace Spark.Services;

/// <summary>
/// Developer-only analytics via PostHog.
/// Tracks action usage, streaming chunk counts, durations, and error types.
/// No user text or selected content is ever sent.
/// Device identity fields are optional and controlled by posthog.includeDeviceIdentity.
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
    private readonly bool       _includeDeviceIdentity;
    private readonly DeviceProfile _deviceProfile;

    public AnalyticsService()
    {
        _http       = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _distinctId = GetOrCreateDeviceId();
        _appVersion = System.Reflection.Assembly
                          .GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        // Load config from appsettings.json
        var config = LoadConfig();
        _enabled = config.Enabled;
        _posthogApiKey = config.ApiKey;
        _posthogHost = config.Host;
        _includeDeviceIdentity = config.IncludeDeviceIdentity;

        var deviceProfileService = new DeviceProfileService();
        _deviceProfile = _includeDeviceIdentity
            ? deviceProfileService.GetCurrentProfile(config.IncludeDeviceEmail, config.DeviceEmailOverride)
            : DeviceProfile.Empty;
    }

    private static AnalyticsConfig LoadConfig()
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
                    var includeDeviceIdentity = phEl.TryGetProperty("includeDeviceIdentity", out var includeIdentityEl) && includeIdentityEl.GetBoolean();
                    var includeDeviceEmail = phEl.TryGetProperty("includeDeviceEmail", out var includeEmailEl) && includeEmailEl.GetBoolean();
                    var deviceEmailOverride = phEl.TryGetProperty("deviceEmailOverride", out var emailOverrideEl) ? emailOverrideEl.GetString() : "";
                    
                    if (string.IsNullOrWhiteSpace(apiKey))
                    {
                        var envKey = Environment.GetEnvironmentVariable("Spark_POSTHOG_API_KEY");
                        if (!string.IsNullOrWhiteSpace(envKey))
                            apiKey = envKey;
                    }
                    
                    return new AnalyticsConfig
                    {
                        Enabled = enabled && !string.IsNullOrWhiteSpace(apiKey),
                        ApiKey = apiKey ?? "",
                        Host = host ?? "https://eu.i.posthog.com",
                        IncludeDeviceIdentity = includeDeviceIdentity,
                        IncludeDeviceEmail = includeDeviceEmail,
                        DeviceEmailOverride = deviceEmailOverride ?? ""
                    };
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("AnalyticsService.LoadConfig", ex);
        }

        return new AnalyticsConfig
        {
            Enabled = false,
            ApiKey = "",
            Host = "https://eu.i.posthog.com",
            IncludeDeviceIdentity = false,
            IncludeDeviceEmail = false,
            DeviceEmailOverride = ""
        };
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

    /// <summary>
    /// Called when the user closes ResultWindow mid-stream (OperationCanceledException).
    /// Balances the orphaned TrackActionStarted so PostHog metrics remain coherent.
    /// partial_chunks / partial_chars represent the partial response before cancellation.
    /// </summary>
    public void TrackActionCancelled(
        string actionId, bool isBuiltIn,
        int partialChunks, int partialChars, long durationMs,
        string provider) =>
        Capture("action_cancelled", new
        {
            action_id      = actionId,
            is_builtin     = isBuiltIn,
            partial_chunks = partialChunks,
            partial_chars  = partialChars,
            duration_ms    = durationMs,
            provider,
            app_version    = _appVersion
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
                    properties  = BuildPropertiesWithDeviceProfile(properties),
                    timestamp   = DateTime.UtcNow.ToString("O")
                };

                var body    = JsonSerializer.Serialize(payload);
                var content = new StringContent(body, Encoding.UTF8, "application/json");
                await _http.PostAsync($"{_posthogHost}/capture/", content).ConfigureAwait(false);
            }
            catch { /* analytics must never surface */ }
        });
    }

    private Dictionary<string, object?> BuildPropertiesWithDeviceProfile<T>(T properties)
    {
        var bag = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(properties));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                    bag[prop.Name] = prop.Value.Clone();
            }
        }
        catch
        {
            // If serialization fails for any reason, continue with base device context only.
        }

        if (_includeDeviceIdentity)
        {
            bag["device_machine_name"] = _deviceProfile.MachineName;
            bag["device_user_name"] = _deviceProfile.UserName;
            bag["device_model"] = _deviceProfile.DeviceModel;
            bag["device_display_name"] = _deviceProfile.DeviceDisplayName;

            if (!string.IsNullOrWhiteSpace(_deviceProfile.Email))
            {
                bag["device_email"] = _deviceProfile.Email;
                bag["device_email_source"] = _deviceProfile.EmailSource;
            }
        }

        return bag;
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

    private sealed class AnalyticsConfig
    {
        public bool Enabled { get; init; }
        public string ApiKey { get; init; } = string.Empty;
        public string Host { get; init; } = "https://eu.i.posthog.com";
        public bool IncludeDeviceIdentity { get; init; }
        public bool IncludeDeviceEmail { get; init; }
        public string DeviceEmailOverride { get; init; } = string.Empty;
    }
}
