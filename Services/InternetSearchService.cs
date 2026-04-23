using System.Net.Http;
using System.Text;
using System.Text.Json;
using Spark.Models;
using Polly;

namespace Spark.Services;

public class InternetSearchService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;

    public InternetSearchService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient(new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<TavilySearchResult> SearchAsync(string query, CancellationToken ct = default)
    {
        var settings = _settingsService.Load();

        if (string.IsNullOrWhiteSpace(settings.Tavily.ApiKey))
            throw new InvalidOperationException("Tavily API key is not configured. Please open Settings and add your Tavily API key.");

        var requestBody = new
        {
            query = query,
            include_answer = "basic",
            search_depth = "advanced"
        };

        var requestBodyJson = JsonSerializer.Serialize(requestBody);

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.tavily.com/search");
            req.Headers.Add("Authorization", $"Bearer {settings.Tavily.ApiKey}");
            req.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(req, ct);
        });

        var json = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var errMsg = "Unknown error";
            try
            {
                var errDoc = JsonDocument.Parse(json);
                if (errDoc.RootElement.TryGetProperty("error", out var errEl))
                {
                    errMsg = errEl.GetString() ?? json;
                }
            }
            catch { errMsg = json; }
            throw new HttpRequestException($"Tavily API error ({(int)response.StatusCode}): {errMsg}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<TavilySearchResult>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result ?? new TavilySearchResult();
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("InternetSearchService.SearchAsync JSON parsing", ex);
            throw;
        }
    }
}

public class TavilySearchResult
{
    public string Query { get; set; } = string.Empty;
    public string? Answer { get; set; }
    public List<TavilySearchResultItem> Results { get; set; } = new();
    public List<string>? Images { get; set; }
}

public class TavilySearchResultItem
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string? Favicon { get; set; }
}
