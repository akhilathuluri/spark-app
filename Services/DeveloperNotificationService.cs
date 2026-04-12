using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Fetches active developer notifications from the backend endpoint.
/// </summary>
public sealed class DeveloperNotificationService : IDisposable
{
    private const string DefaultEndpoint = "https://www.spark-app.in/api/developer-notifications";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _endpoint;

    public DeveloperNotificationService(HttpClient? httpClient = null, string? endpoint = null)
    {
        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _endpoint = string.IsNullOrWhiteSpace(endpoint)
            ? Environment.GetEnvironmentVariable("Spark_DEV_NOTIFICATIONS_URL")?.Trim() ?? DefaultEndpoint
            : endpoint;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }

    public async Task<IReadOnlyList<DeveloperNotification>> GetActiveNotificationsAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_endpoint))
            return Array.Empty<DeveloperNotification>();

        try
        {
            using var response = await _http.GetAsync(_endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var payload = await JsonSerializer.DeserializeAsync<DeveloperNotificationsResponse>(stream, cancellationToken: cancellationToken);
            if (payload?.Data is null || payload.Data.Count == 0)
                return Array.Empty<DeveloperNotification>();

            var mapped = payload.Data
                .Where(n => n.IsActive)
                .Select(n => new DeveloperNotification
                {
                    Id = n.Id,
                    IsActive = n.IsActive,
                    Title = string.IsNullOrWhiteSpace(n.Title) ? "Notification" : n.Title.Trim(),
                    Message = string.IsNullOrWhiteSpace(n.Message) ? "" : n.Message.Trim(),
                    Priority = NormalizePriority(n.Priority),
                    Type = string.IsNullOrWhiteSpace(n.Type) ? "info" : n.Type.Trim().ToLowerInvariant(),
                    CreatedAtLocal = FromUnixMs(n.CreationTimeMs)
                })
                .OrderByDescending(n => PriorityRank(n.Priority))
                .ThenByDescending(n => n.CreatedAtLocal)
                .ToList();

            return mapped;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("DeveloperNotificationService.GetActiveNotificationsAsync", ex);
            return Array.Empty<DeveloperNotification>();
        }
    }

    private static DateTime FromUnixMs(double unixMs)
    {
        try
        {
            var asLong = (long)Math.Max(0, unixMs);
            return DateTimeOffset.FromUnixTimeMilliseconds(asLong).LocalDateTime;
        }
        catch
        {
            return DateTime.Now;
        }
    }

    private static string NormalizePriority(string? priority)
    {
        var p = (priority ?? string.Empty).Trim().ToLowerInvariant();
        return p switch
        {
            "high" => "high",
            "low" => "low",
            _ => "normal"
        };
    }

    private static int PriorityRank(string priority)
    {
        return priority switch
        {
            "high" => 3,
            "normal" => 2,
            "low" => 1,
            _ => 0
        };
    }

    private sealed class DeveloperNotificationsResponse
    {
        [JsonPropertyName("count")]
        public int Count { get; set; }

        [JsonPropertyName("data")]
        public List<DeveloperNotificationRow> Data { get; set; } = new();
    }

    private sealed class DeveloperNotificationRow
    {
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("_creationTime")]
        public double CreationTimeMs { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = "normal";

        [JsonPropertyName("type")]
        public string Type { get; set; } = "info";
    }
}
