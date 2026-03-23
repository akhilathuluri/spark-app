using System.Net.Http;
using Polly;

namespace Spark.Services;

/// <summary>
/// Provides retry policies for resilient HTTP requests.
/// Handles transient failures: 429 (rate limit), 503 (unavailable), and timeouts.
/// </summary>
public static class RetryService
{
    /// <summary>
    /// Retry policy: 3 attempts with exponential backoff (1s, 2s, 4s).
    /// Retries on: 429, 503, timeouts.
    /// </summary>
    public static IAsyncPolicy<HttpResponseMessage> GetHttpRetryPolicy()
    {
        return Policy
            .HandleResult<HttpResponseMessage>(r => 
                (int)r.StatusCode == 429 ||  // Rate limited
                (int)r.StatusCode == 503)    // Service unavailable
            .Or<HttpRequestException>()       // Network errors
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), // 1s, 2s, 4s
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    DebugLogService.LogMessage(
                        $"HTTP retry {retryCount}: waiting {timespan.TotalSeconds}s " +
                        $"(reason: {outcome.Exception?.GetType().Name ?? outcome.Result?.StatusCode.ToString()})");
                });
    }
}
