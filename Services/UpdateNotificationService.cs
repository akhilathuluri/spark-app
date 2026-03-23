using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spark.Models;

namespace Spark.Services;

public sealed class UpdateNotificationService : IDisposable
{
    private const string DefaultBaseUrl = "http://localhost:8080";
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _baseUrl;

    public UpdateNotificationService(HttpClient? httpClient = null, string? baseUrl = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? Environment.GetEnvironmentVariable("Spark_API_BASE_URL") ?? DefaultBaseUrl
            : baseUrl;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    public async Task<AppUpdateInfo?> CheckForUpdateAsync(string appId = "Spark", CancellationToken cancellationToken = default)
    {
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

            return new AppUpdateInfo
            {
                AppId = app.AppId,
                AppName = app.AppName,
                CurrentVersion = current,
                LatestVersion = latest,
                MinimumVersion = minimum,
                ReleaseNotes = app.ReleaseNotes,
                DownloadUrl = app.DownloadUrl,
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

        [JsonPropertyName("release_notes")]
        public string ReleaseNotes { get; set; } = string.Empty;
    }
}
