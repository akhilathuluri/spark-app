using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Spark.Models;
using Spark.Services.Prompts;
using Polly;

namespace Spark.Services;

public class AiService : IDisposable
{
    private const int DefaultHttpTimeoutSeconds = 180;
    private const int MinHttpTimeoutSeconds = 30;
    private const int MaxHttpTimeoutSeconds = 600;

    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly UserPromptPersonalizationService _promptPersonalizationService = new();

    public AiService(SettingsService settingsService)
    {
        _settingsService = settingsService;
        _httpClient = new HttpClient(new System.Net.Http.SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
        })
        {
            Timeout = TimeSpan.FromSeconds(GetConfiguredTimeoutSeconds()),
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public void Dispose() => _httpClient.Dispose();

    private static int GetConfiguredTimeoutSeconds()
    {
        var raw = Environment.GetEnvironmentVariable("Spark_HTTP_TIMEOUT_SECONDS");
        if (!int.TryParse(raw, out var seconds))
            return DefaultHttpTimeoutSeconds;

        return Math.Clamp(seconds, MinHttpTimeoutSeconds, MaxHttpTimeoutSeconds);
    }

    public async Task<string> ProcessAsync(string prompt, string selectedText, CancellationToken ct = default)
    {
        var settings = _settingsService.Load();
        var fullPrompt = _promptPersonalizationService.BuildPrompt(prompt, selectedText, settings);

        return settings.ActiveProvider switch
        {
            "Groq" => await CallGroqAsync(settings.Groq, fullPrompt, ct),
            _ => await CallOpenRouterAsync(settings.OpenRouter, fullPrompt, ct)
        };
    }

    private async Task<string> CallOpenRouterAsync(OpenRouterSettings config, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenRouter API key is not configured. Please open Settings and add your API key.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("OpenRouter model is not configured. Please open Settings and specify a model name.");

        var requestBodyJson = JsonSerializer.Serialize(new
        {
            model = config.Model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 2048
        });

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            req.Headers.Add("HTTP-Referer", "https://Spark.app");
            req.Headers.Add("X-Title", "Spark");
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
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errMsg = msgEl.GetString() ?? json;
                }
            }
            catch { errMsg = json; }
            throw new HttpRequestException($"OpenRouter API error ({(int)response.StatusCode}): {errMsg}");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Null-safe navigation: check array length before indexing
            if (!root.TryGetProperty("choices", out var choicesEl) || choicesEl.GetArrayLength() == 0)
                return string.Empty;

            var firstChoice = choicesEl[0];
            if (!firstChoice.TryGetProperty("message", out var msgEl))
                return string.Empty;

            if (!msgEl.TryGetProperty("content", out var contentEl))
                return string.Empty;

            return contentEl.GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("CallOpenRouterAsync JSON parsing", ex);
            throw;
        }
    }

    private async Task<string> CallGroqAsync(GroqSettings config, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Groq API key is not configured. Please open Settings and add your API key.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("Groq model is not configured. Please open Settings and specify a model name.");

        var requestBodyJson = JsonSerializer.Serialize(new
        {
            model = config.Model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 2048
        });

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
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
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errMsg = msgEl.GetString() ?? json;
                }
            }
            catch { errMsg = json; }
            throw new HttpRequestException($"Groq API error ({(int)response.StatusCode}): {errMsg}");
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Null-safe navigation
            if (!root.TryGetProperty("choices", out var choicesEl) || choicesEl.GetArrayLength() == 0)
                return string.Empty;

            var firstChoice = choicesEl[0];
            if (!firstChoice.TryGetProperty("message", out var msgEl))
                return string.Empty;

            if (!msgEl.TryGetProperty("content", out var contentEl))
                return string.Empty;

            return contentEl.GetString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("CallGroqAsync JSON parsing", ex);
            throw;
        }
    }

    // ── Streaming API ────────────────────────────────────────────────────────
    // Both OpenRouter and Groq implement the OpenAI SSE streaming protocol.
    // Each SSE line is:  data: {"choices":[{"delta":{"content":"token"}}]}
    // The stream ends with:  data: [DONE]

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        string selectedText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var settings   = _settingsService.Load();
        var fullPrompt = _promptPersonalizationService.BuildPrompt(prompt, selectedText, settings);

        var tokens = settings.ActiveProvider switch
        {
            "Groq" => StreamGroqAsync(settings.Groq, fullPrompt, ct),
            _      => StreamOpenRouterAsync(settings.OpenRouter, fullPrompt, ct)
        };

        await using var enumerator = tokens.GetAsyncEnumerator(ct);
        while (await enumerator.MoveNextAsync())
            yield return enumerator.Current;
    }

    private async IAsyncEnumerable<string> StreamOpenRouterAsync(
        OpenRouterSettings config,
        string prompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenRouter API key is not configured. Please open Settings and add your API key.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("OpenRouter model is not configured. Please open Settings and specify a model name.");

        var requestBodyJson = JsonSerializer.Serialize(new
        {
            model    = config.Model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 2048,
            stream   = true
        });

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            req.Headers.Add("HTTP-Referer", "https://Spark.app");
            req.Headers.Add("X-Title", "Spark");
            req.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        });

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            var errMsg = "Unknown error";
            try
            {
                var errDoc = JsonDocument.Parse(errBody);
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errMsg = msgEl.GetString() ?? errBody;
                }
            }
            catch { errMsg = errBody; }
            throw new HttpRequestException($"OpenRouter API error ({(int)response.StatusCode}): {errMsg}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // Null-safe streaming JSON parsing
                if (root.TryGetProperty("choices", out var choicesEl) && choicesEl.GetArrayLength() > 0)
                {
                    var delta = choicesEl[0];
                    if (delta.TryGetProperty("delta", out var deltaEl) &&
                        deltaEl.TryGetProperty("content", out var contentEl))
                    {
                        token = contentEl.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogService.LogMessage($"Skipping malformed SSE: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }

    private async IAsyncEnumerable<string> StreamGroqAsync(
        GroqSettings config,
        string prompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Groq API key is not configured. Please open Settings and add your API key.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("Groq model is not configured. Please open Settings and specify a model name.");

        var requestBodyJson = JsonSerializer.Serialize(new
        {
            model    = config.Model,
            messages = new[] { new { role = "user", content = prompt } },
            max_tokens = 2048,
            stream   = true
        });

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            req.Content = new StringContent(requestBodyJson, Encoding.UTF8, "application/json");
            return await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        });

        if (!response.IsSuccessStatusCode)
        {
            var errBody = await response.Content.ReadAsStringAsync(ct);
            var errMsg = "Unknown error";
            try
            {
                var errDoc = JsonDocument.Parse(errBody);
                if (errDoc.RootElement.TryGetProperty("error", out var errEl) &&
                    errEl.TryGetProperty("message", out var msgEl))
                {
                    errMsg = msgEl.GetString() ?? errBody;
                }
            }
            catch { errMsg = errBody; }
            throw new HttpRequestException($"Groq API error ({(int)response.StatusCode}): {errMsg}");
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                // Null-safe streaming JSON parsing
                if (root.TryGetProperty("choices", out var choicesEl) && choicesEl.GetArrayLength() > 0)
                {
                    var delta = choicesEl[0];
                    if (delta.TryGetProperty("delta", out var deltaEl) &&
                        deltaEl.TryGetProperty("content", out var contentEl))
                    {
                        token = contentEl.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogService.LogMessage($"Skipping malformed SSE: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(token))
                yield return token;
        }
    }
}
