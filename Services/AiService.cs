using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using OpenAI.Chat;
using Google.GenAI;
using Spark.Models;
using Spark.Services.Prompts;
using Polly;

namespace Spark.Services;

public class AiService : IDisposable
{
    private const int DefaultHttpTimeoutSeconds = 180;
    private const int MinHttpTimeoutSeconds = 30;
    private const int MaxHttpTimeoutSeconds = 600;
    private const int DefaultMaxTokens = 4096;

    private readonly HttpClient _httpClient;
    private readonly SettingsService _settingsService;
    private readonly UserPromptPersonalizationService _promptPersonalizationService = new();

    // Cached SDK clients — recreated only when the API key or model changes.
    // Creating these on every call causes unnecessary connection setup overhead (Gemini especially).
    private ChatClient? _openAiClient;
    private string? _openAiClientKey;
    private string? _openAiClientModel;

    private Client? _geminiClient;
    private string? _geminiClientKey;

    // Warmup tracking - aggressive pre-initialization
    private bool _isWarmedUp;
    private string? _lastWarmupProvider;

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

    /// <summary>
    /// Pre-warms SDK clients and HTTP connections for faster first request.
    /// Unlike expensive WarmUpAsync (which makes API calls), this only establishes
    /// TCP connections so DNS/tls handshake is cached.
    /// </summary>
    public void EnsureWarmedUp()
    {
        if (_isWarmedUp) return;

        try
        {
            var settings = _settingsService.Load();
            _lastWarmupProvider = settings.ActiveProvider;

            switch (settings.ActiveProvider)
            {
                case "Gemini" when !string.IsNullOrWhiteSpace(settings.Gemini?.ApiKey):
                    _ = GetGeminiClient(settings.Gemini);
                    break;
                case "OpenAI" when !string.IsNullOrWhiteSpace(settings.OpenAI?.ApiKey)
                                && !string.IsNullOrWhiteSpace(settings.OpenAI?.Model):
                    _ = GetOpenAIClient(settings.OpenAI);
                    break;
                case "Groq" when !string.IsNullOrWhiteSpace(settings.Groq.ApiKey):
                    _ = WarmUpTcpConnectionAsync("https://api.groq.com");
                    break;
                case "OpenRouter" when !string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey):
                    _ = WarmUpTcpConnectionAsync("https://openrouter.ai");
                    break;
            }

            _isWarmedUp = true;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("AiService.EnsureWarmedUp", ex);
        }
    }

    /// <summary>
    /// Fast warmup - just establishes TCP+TLS connection without sending any data.
    /// This caches DNS resolution and TLS handshake for subsequent requests.
    /// </summary>
    private async Task WarmUpTcpConnectionAsync(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Spark/1.0");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var _ = await _httpClient.SendAsync(req, cts.Token);
        }
        catch { }
    }

    /// <summary>
    /// Full warmup - makes a minimal API call to initialize the actual client.
    /// Only call this when user explicitly triggers it, not on startup.
    /// </summary>
    public Task WarmUpFullAsync() => Task.Run(async () =>
    {
        try
        {
            var settings = _settingsService.Load();

            switch (settings.ActiveProvider)
            {
                case "Gemini" when !string.IsNullOrWhiteSpace(settings.Gemini?.ApiKey):
                    GetGeminiClient(settings.Gemini);
                    break;
                case "OpenAI" when !string.IsNullOrWhiteSpace(settings.OpenAI?.ApiKey)
                                && !string.IsNullOrWhiteSpace(settings.OpenAI?.Model):
                    GetOpenAIClient(settings.OpenAI);
                    break;
                case "Groq" when !string.IsNullOrWhiteSpace(settings.Groq.ApiKey):
                    await WarmUpHttpConnectionAsync("https://api.groq.com/openai/v1/models", settings.Groq.ApiKey);
                    break;
                case "OpenRouter" when !string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey):
                    await WarmUpHttpConnectionAsync("https://openrouter.ai/api/v1/models", settings.OpenRouter.ApiKey);
                    break;
            }

            _isWarmedUp = true;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("AiService.WarmUpFullAsync", ex);
        }
    });

    private async Task WarmUpHttpConnectionAsync(string url, string apiKey)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("Authorization", $"Bearer {apiKey}");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var _ = await _httpClient.SendAsync(req, cts.Token);
        }
        catch { }
    }

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
            "Groq"   => await CallGroqAsync(settings.Groq, fullPrompt, ct),
            "OpenAI" => await CallOpenAIAsync(settings.OpenAI, fullPrompt, ct),
            "Gemini" => await CallGeminiAsync(settings.Gemini, fullPrompt, ct),
            _        => await CallOpenRouterAsync(settings.OpenRouter, fullPrompt, ct)
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
            max_tokens = DefaultMaxTokens,
            temperature = 0.7,
            top_p = 0.9,
            frequency_penalty = 0.0,
            presence_penalty = 0.0
        });

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            req.Headers.Add("HTTP-Referer", "https://www.spark-app.in/");
            req.Headers.Add("X-Title", "Spark");
            req.Headers.Add("X-Acceleration", "enabled");
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
            max_tokens = DefaultMaxTokens,
            temperature = 0.7,
            top_p = 0.9
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
    // OpenRouter and Groq use the OpenAI SSE streaming protocol.
    // OpenAI uses its official SDK streaming. Gemini uses the GenAI SDK streaming.
    // Each SSE line is:  data: {"choices":[{"delta":{"content":"token"}}]}
    // The stream ends with:  data: [DONE]

    // ── OpenAI (official SDK) ────────────────────────────────────────────────

    private ChatClient GetOpenAIClient(OpenAISettings config)
    {
        // Reuse the cached client if key and model haven't changed.
        if (_openAiClient != null &&
            _openAiClientKey   == config.ApiKey &&
            _openAiClientModel == config.Model)
            return _openAiClient;

        _openAiClient      = new ChatClient(model: config.Model, apiKey: config.ApiKey);
        _openAiClientKey   = config.ApiKey;
        _openAiClientModel = config.Model;
        return _openAiClient;
    }

    private Client GetGeminiClient(GeminiSettings config)
    {
        // Reuse the cached client if the key hasn't changed.
        if (_geminiClient != null && _geminiClientKey == config.ApiKey)
            return _geminiClient;

        _geminiClient    = new Client(apiKey: config.ApiKey);
        _geminiClientKey = config.ApiKey;
        return _geminiClient;
    }

    private async Task<string> CallOpenAIAsync(OpenAISettings config, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured. Please open Settings and add your API key.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("OpenAI model is not configured. Please open Settings and specify a model name.");

        try
        {
            var client = GetOpenAIClient(config);
            var completion = await client.CompleteChatAsync(
                [ChatMessage.CreateUserMessage(prompt)],
                cancellationToken: ct);
            return completion.Value.Content[0].Text ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugLogService.LogError("CallOpenAIAsync", ex);
            throw new HttpRequestException($"OpenAI API error: {ex.Message}", ex);
        }
    }

    // ── Gemini (Google GenAI SDK) ────────────────────────────────────────────

    private async Task<string> CallGeminiAsync(GeminiSettings config, string prompt, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Gemini API key is not configured. Please open Settings and add your API key.");

        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("Gemini model is not configured. Please open Settings and specify a model name.");

        try
        {
            var client = GetGeminiClient(config);
            var response = await client.Models.GenerateContentAsync(
                model: config.Model,
                contents: prompt,
                cancellationToken: ct);

            return response?.Candidates?[0]?.Content?.Parts?[0]?.Text ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            DebugLogService.LogError("CallGeminiAsync", ex);
            throw new HttpRequestException($"Gemini API error: {ex.Message}", ex);
        }
    }

    public async IAsyncEnumerable<string> StreamAsync(
        string prompt,
        string selectedText,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var settings   = _settingsService.Load();
        var fullPrompt = _promptPersonalizationService.BuildPrompt(prompt, selectedText, settings);

        var tokens = settings.ActiveProvider switch
        {
            "Groq"   => StreamGroqAsync(settings.Groq!, fullPrompt, ct),
            "OpenAI" => StreamOpenAIAsync(settings.OpenAI!, fullPrompt, ct),
            "Gemini" => StreamGeminiAsync(settings.Gemini!, fullPrompt, ct),
            _        => StreamOpenRouterAsync(settings.OpenRouter!, fullPrompt, ct)
        };

        await using var enumerator = tokens.GetAsyncEnumerator(ct);
        while (await enumerator.MoveNextAsync())
        {
            if (enumerator.Current is { } tok && !string.IsNullOrEmpty(tok))
                yield return tok;
        }
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
            max_tokens = DefaultMaxTokens,
            stream   = true
        });

        var retryPolicy = RetryService.GetHttpRetryPolicy();
        using var response = await retryPolicy.ExecuteAsync(async () =>
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
            req.Headers.Add("Authorization", $"Bearer {config.ApiKey}");
            req.Headers.Add("HTTP-Referer", "https://www.spark-app.in/");
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

        // Some models routed through OpenRouter (e.g. via SiliconFlow) respond with
        // Content-Type: application/json even when stream=true was requested — they send
        // a single complete JSON object instead of an SSE stream.
        // Detect this and fall back to parsing it as a regular chat completion response.
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            DebugLogService.LogMessage($"[OpenRouter] Non-streaming JSON response received (model: {config.Model}). Parsing as completion.");
            var text = ParseOpenRouterCompletionJson(body);
            if (!string.IsNullOrEmpty(text))
                yield return text;
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new System.IO.StreamReader(stream);

        int tokenCount = 0;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();

            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") yield break;

            // Some providers send an empty data line as a heartbeat — skip silently
            if (string.IsNullOrWhiteSpace(data)) continue;

            // Detect non-SSE: if the first data line looks like a full JSON completion
            // (has "choices" and "message" rather than "delta"), parse it directly.
            if (tokenCount == 0 && data.Contains("\"message\"") && !data.Contains("\"delta\""))
            {
                DebugLogService.LogMessage($"[OpenRouter] Detected non-streaming JSON inside SSE data line. Parsing as completion.");
                var text = ParseOpenRouterCompletionJson(data);
                if (!string.IsNullOrEmpty(text))
                {
                    yield return text;
                    tokenCount++;
                }
                continue;
            }

            string? token = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choicesEl) && choicesEl.GetArrayLength() > 0)
                {
                    var choice = choicesEl[0];

                    if (choice.TryGetProperty("delta", out var deltaEl))
                    {
                        // Only read delta.content — never delta.reasoning.
                        // Reasoning/thinking tokens are internal model scratchpad and must
                        // not be shown to the user. If a reasoning model streams only
                        // delta.reasoning with empty delta.content (e.g. it hit max_tokens
                        // while still thinking), the zero-token fallback below handles it.
                        if (deltaEl.TryGetProperty("content", out var contentEl))
                        {
                            var s = contentEl.GetString();
                            if (!string.IsNullOrEmpty(s))
                                token = s;
                        }
                    }
                    // Some providers: choices[0].text
                    else if (choice.TryGetProperty("text", out var textEl))
                    {
                        token = textEl.GetString();
                    }
                    // Fallback: full message object inside SSE (non-standard but seen in the wild)
                    else if (choice.TryGetProperty("message", out var msgEl) &&
                             msgEl.TryGetProperty("content", out var msgContentEl))
                    {
                        token = msgContentEl.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogService.LogMessage($"[OpenRouter] Skipping malformed SSE chunk: {ex.Message} | data={data[..Math.Min(120, data.Length)]}");
            }

            if (!string.IsNullOrEmpty(token))
            {
                tokenCount++;
                yield return token;
            }
        }

        // If we read the entire stream but got zero content tokens, the model produced
        // only reasoning/thinking tokens (delta.reasoning) without a final answer —
        // typically because it hit max_tokens while still in its thinking phase.
        // Try the non-streaming endpoint as a last resort; if that also returns empty,
        // throw a clear error so the user knows to try a different model.
        if (tokenCount == 0)
        {
            DebugLogService.LogMessage($"[OpenRouter] Stream ended with 0 content tokens for model '{config.Model}'. Attempting non-streaming fallback.");
            var fallbackResult = await CallOpenRouterAsync(config, prompt, ct);
            if (!string.IsNullOrEmpty(fallbackResult))
                yield return fallbackResult;
            else
                throw new InvalidOperationException(
                    $"The model '{config.Model}' returned no response content. " +
                    "This may be a reasoning model that ran out of tokens before producing an answer. " +
                    "Try a different model or a shorter prompt.");
        }
    }

    /// <summary>
    /// Parses a standard OpenAI-compatible chat completion JSON body.
    /// Used as fallback when a provider returns application/json instead of SSE.
    /// </summary>
    private static string ParseOpenRouterCompletionJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choicesEl) || choicesEl.GetArrayLength() == 0)
                return string.Empty;

            var choice = choicesEl[0];

            // Standard non-streaming: choices[0].message.content
            if (choice.TryGetProperty("message", out var msgEl) &&
                msgEl.TryGetProperty("content", out var contentEl))
                return contentEl.GetString() ?? string.Empty;

            // Some providers: choices[0].text
            if (choice.TryGetProperty("text", out var textEl))
                return textEl.GetString() ?? string.Empty;

            return string.Empty;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("ParseOpenRouterCompletionJson", ex);
            return string.Empty;
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
            max_tokens = DefaultMaxTokens,
            stream   = true,
            temperature = 0.7,
            top_p = 0.9
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

    // ── OpenAI streaming (official SDK) ─────────────────────────────────────

    private async IAsyncEnumerable<string> StreamOpenAIAsync(
        OpenAISettings config,
        string prompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured. Please open Settings and add your API key.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("OpenAI model is not configured. Please open Settings and specify a model name.");

        var client = GetOpenAIClient(config);

        await foreach (var update in client.CompleteChatStreamingAsync(
            [ChatMessage.CreateUserMessage(prompt)],
            cancellationToken: ct))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                    yield return part.Text;
            }
        }
    }

    // ── Gemini streaming (Google GenAI SDK) ──────────────────────────────────

    private async IAsyncEnumerable<string> StreamGeminiAsync(
        GeminiSettings config,
        string prompt,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException("Gemini API key is not configured. Please open Settings and add your API key.");
        if (string.IsNullOrWhiteSpace(config.Model))
            throw new InvalidOperationException("Gemini model is not configured. Please open Settings and specify a model name.");

        var client = GetGeminiClient(config);

        await foreach (var chunk in client.Models.GenerateContentStreamAsync(
            model: config.Model,
            contents: prompt,
            cancellationToken: ct))
        {
            var text = chunk?.Candidates?[0]?.Content?.Parts?[0]?.Text;
            if (!string.IsNullOrEmpty(text))
                yield return text;
        }
    }
}