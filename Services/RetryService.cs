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
    /// Respects Retry-After header from 429 responses when present.
    /// Retries on: 429, 503, timeouts.
    /// After all retries are exhausted, throws HttpRequestException with a clear message.
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
                sleepDurationProvider: (attempt, outcome, _) =>
                {
                    // Honour Retry-After header if the server sent one (OpenRouter does this).
                    if (outcome.Result != null &&
                        outcome.Result.Headers.RetryAfter?.Delta is TimeSpan retryAfter &&
                        retryAfter > TimeSpan.Zero &&
                        retryAfter <= TimeSpan.FromSeconds(30))
                    {
                        return retryAfter;
                    }

                    // Exponential backoff: 1s, 2s, 4s
                    return TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));
                },
                onRetryAsync: (outcome, timespan, retryCount, _) =>
                {
                    var reason = outcome.Exception?.GetType().Name
                        ?? outcome.Result?.StatusCode.ToString()
                        ?? "Unknown";
                    DebugLogService.LogMessage(
                        $"HTTP retry {retryCount}: waiting {timespan.TotalSeconds:0.#}s (reason: {reason})");
                    return Task.CompletedTask;
                })
            // After all retries are exhausted, convert a 429/503 into a clear exception
            // so the UI shows a meaningful error instead of "No response received".
            .WrapAsync(Policy
                .HandleResult<HttpResponseMessage>(r =>
                    (int)r.StatusCode == 429 ||
                    (int)r.StatusCode == 503)
                .FallbackAsync(
                    fallbackAction: (outcome, _, _) =>
                    {
                        var status = (int)(outcome.Result?.StatusCode ?? 0);
                        var msg = status == 429
                            ? "Rate limit reached. The AI provider is busy — please wait a moment and try again."
                            : "The AI provider is temporarily unavailable (503). Please try again shortly.";
                        throw new HttpRequestException(msg);
                    },
                    onFallbackAsync: (_, _) => Task.CompletedTask));
    }
}
