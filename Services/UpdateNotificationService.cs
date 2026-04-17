using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spark.Models;
using Spark.Services.Updates;


namespace Spark.Services;

public sealed class UpdateNotificationService : IDisposable
{
    // Empty string = no update check configured (safe default for local/dev builds).
    // Set updateApi.baseUrl in appsettings.json, or use Spark_API_BASE_URL env var.
    private const string DefaultBaseUrl = "https://www.spark-app.in";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;
    private readonly UpdateArchitectureService _architectureService;
    private readonly UpdateDownloadResolver _downloadResolver;

    public UpdateNotificationService(HttpClient? httpClient = null, string? baseUrl = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? LoadBaseUrlFromConfig()
            : baseUrl;
        _architectureService = new UpdateArchitectureService();
        _downloadResolver = new UpdateDownloadResolver();
    }

    /// <summary>
    /// Resolves the update API base URL using the following priority:
    ///   1. appsettings.json  → updateApi.baseUrl
    ///   2. Environment variable Spark_API_BASE_URL
    ///   3. DefaultBaseUrl (empty string — disables update checks)
    /// This makes it trivial to point releases at a real API without code changes.
    /// </summary>
    private static string LoadBaseUrlFromConfig()
    {
        // Priority 1: appsettings.json
        try
        {
            var basePath = AppDomain.CurrentDomain.BaseDirectory;
            var devPath  = Path.Combine(basePath, "appsettings.developer.json");
            var pubPath  = Path.Combine(basePath, "appsettings.json");
            var cfgPath  = File.Exists(devPath) ? devPath : pubPath;

            if (File.Exists(cfgPath))
            {
                var json = File.ReadAllText(cfgPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("updateApi", out var updateEl) &&
                    updateEl.TryGetProperty("baseUrl", out var urlEl))
                {
                    var configured = urlEl.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(configured))
                        return configured;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("UpdateNotificationService.LoadBaseUrlFromConfig", ex);
        }

        // Priority 2: environment variable (runtime override)
        var envUrl = Environment.GetEnvironmentVariable("Spark_API_BASE_URL")?.Trim();
        if (!string.IsNullOrWhiteSpace(envUrl))
            return envUrl;

        // Priority 3: default (empty = disabled)
        return DefaultBaseUrl;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(string appId = "Spark", CancellationToken cancellationToken = default)
    {
        // Skip entirely when no API URL is configured — avoids wasted HTTP calls on dev builds.
        if (string.IsNullOrWhiteSpace(_baseUrl))
        {
            DebugLogService.LogMessage("UpdateNotificationService: no updateApi.baseUrl configured — update check skipped.");
            return null;
        }

        try
        {
            var endpoint = $"{_baseUrl.TrimEnd('/')}/api/app-table";
            using var response = await _http.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<AppTableResponse>(stream, cancellationToken: cancellationToken);
            if (payload?.Data is null || payload.Data.Count == 0)
                return null;

            var app = payload.Data.FirstOrDefault(x =>
                    string.Equals(x.AppId, appId, StringComparison.OrdinalIgnoreCase))
                ?? payload.Data[0];

            var current = GetCurrentAppVersion();
            var latest = NormalizeVersion(app.LatestVersion);
            var minimum = NormalizeVersion(app.MinimumVersion);

            var isUpdateAvailable = CompareVersions(latest, current) > 0;
            var isRequiredUpdate = CompareVersions(minimum, current) > 0;
            var preferredRuntimeId = _architectureService.GetPreferredRuntimeId();
            var processArchitecture = _architectureService.GetProcessArchitectureLabel();
            var osArchitecture = _architectureService.GetOsArchitectureLabel();
            var resolution = _downloadResolver.Resolve(
                preferredRuntimeId,
                BuildRuntimeDownloads(app),
                app.DownloadUrl,
                app.DownloadSha256);

            if (isUpdateAvailable && resolution.UsedFallback)
            {
                DebugLogService.LogMessage(
                    $"UpdateNotificationService: fallback download selected. preferred={preferredRuntimeId}, selected={resolution.SelectedRuntimeId}");
            }

            return new AppUpdateInfo
            {
                AppId = app.AppId,
                AppName = app.AppName,
                PreferredRuntimeId = preferredRuntimeId,
                SelectedRuntimeId = resolution.SelectedRuntimeId,
                ProcessArchitecture = processArchitecture,
                OsArchitecture = osArchitecture,
                CurrentVersion = current,
                LatestVersion = latest,
                MinimumVersion = minimum,
                ReleaseNotes = app.ReleaseNotes,
                DownloadUrl = resolution.Url,
                DownloadSha256 = resolution.Sha256,
                UsedDownloadFallback = resolution.UsedFallback,
                IsUpdateAvailable = isUpdateAvailable,
                IsRequiredUpdate = isRequiredUpdate,
                CheckedAtUtc = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("UpdateNotificationService.CheckForUpdateAsync", ex);
            return null;
        }
    }

    public static void OpenDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("UpdateNotificationService.OpenDownloadUrl", ex);
        }
    }

    private static string GetCurrentAppVersion()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        return $"{version?.Major ?? 0}.{version?.Minor ?? 0}.{version?.Build ?? 0}";
    }

    private static int CompareVersions(string left, string right)
    {
        var v1 = ParseVersion(left);
        var v2 = ParseVersion(right);
        return v1.CompareTo(v2);
    }

    private static string NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "0.0.0";

        return version.Trim().Split('-', 2)[0];
    }

    private static Version ParseVersion(string version)
    {
        if (Version.TryParse(NormalizeVersion(version), out var parsed))
            return parsed;

        return new Version(0, 0, 0);
    }

    private static IEnumerable<UpdateDownloadOption> BuildRuntimeDownloads(AppTableRow row)
    {
        var options = new Dictionary<string, UpdateDownloadOption>(StringComparer.OrdinalIgnoreCase);

        AddOption(options, "win-x64", row.DownloadUrlWinX64, row.Sha256WinX64);
        AddOption(options, "win-arm64", row.DownloadUrlWinArm64, row.Sha256WinArm64);

        if (row.Downloads is not null)
        {
            foreach (var (runtimeId, item) in row.Downloads)
            {
                var url = item.Url;
                if (string.IsNullOrWhiteSpace(url))
                    continue;

                AddOption(options, runtimeId, url, item.Sha256);
            }
        }

        return options.Values;
    }

    private static void AddOption(
        IDictionary<string, UpdateDownloadOption> options,
        string runtimeId,
        string? url,
        string? sha256)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        var normalizedRuntimeId = UpdateDownloadResolver.NormalizeRuntimeId(runtimeId);
        options[normalizedRuntimeId] = new UpdateDownloadOption(
            normalizedRuntimeId,
            url.Trim(),
            sha256?.Trim() ?? string.Empty);
    }

    private sealed class AppTableResponse
    {
        public int Count { get; set; }

        [JsonPropertyName("data")]
        public List<AppTableRow> Data { get; set; } = new();
    }

    private sealed class AppTableRow
    {
        [JsonPropertyName("app_id")]
        public string AppId { get; set; } = string.Empty;

        [JsonPropertyName("app_name")]
        public string AppName { get; set; } = string.Empty;

        [JsonPropertyName("latest_version")]
        public string LatestVersion { get; set; } = string.Empty;

        [JsonPropertyName("minimum_version")]
        public string MinimumVersion { get; set; } = string.Empty;

        [JsonPropertyName("download_url")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("download_sha256")]
        public string DownloadSha256 { get; set; } = string.Empty;

        [JsonPropertyName("download_url_win_x64")]
        public string? DownloadUrlWinX64 { get; set; }

        [JsonPropertyName("download_url_win_arm64")]
        public string? DownloadUrlWinArm64 { get; set; }

        [JsonPropertyName("sha256_win_x64")]
        public string? Sha256WinX64 { get; set; }

        [JsonPropertyName("sha256_win_arm64")]
        public string? Sha256WinArm64 { get; set; }

        [JsonPropertyName("downloads")]
        public Dictionary<string, RuntimeDownloadRow>? Downloads { get; set; }

        [JsonPropertyName("release_notes")]
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    private sealed class RuntimeDownloadRow
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }
}
