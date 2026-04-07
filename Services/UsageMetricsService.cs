using System.IO;
using System.Text.Json;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Stores local, privacy-friendly usage estimates for latency, token volume, and spend.
/// </summary>
public sealed class UsageMetricsService
{
    private static readonly string MetricsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "usage-metrics.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();

    public void RecordAction(
        string actionId,
        string actionName,
        string provider,
        string model,
        string inputText,
        string outputText,
        int latencyMs)
    {
        try
        {
            var inputChars = inputText?.Length ?? 0;
            var outputChars = outputText?.Length ?? 0;
            var inputTokens = EstimateTokens(inputChars);
            var outputTokens = EstimateTokens(outputChars);
            var cost = EstimateCostUsd(provider, model, inputTokens, outputTokens);

            var item = new ActionUsageRecord
            {
                TimestampUtc = DateTime.UtcNow,
                ActionId = actionId,
                ActionName = actionName,
                Provider = provider,
                Model = model,
                LatencyMs = Math.Max(0, latencyMs),
                InputChars = inputChars,
                OutputChars = outputChars,
                EstimatedInputTokens = inputTokens,
                EstimatedOutputTokens = outputTokens,
                EstimatedCostUsd = cost
            };

            lock (_sync)
            {
                var records = LoadRecordsUnsafe();
                records.Add(item);

                // Keep rolling 120-day window to control file size.
                var cutoff = DateTime.UtcNow.AddDays(-120);
                records = records
                    .Where(r => r.TimestampUtc >= cutoff)
                    .OrderBy(r => r.TimestampUtc)
                    .ToList();

                SaveRecordsUnsafe(records);
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("UsageMetricsService.RecordAction", ex);
        }
    }

    public MonthlyUsageSummary GetCurrentMonthSummary()
    {
        lock (_sync)
        {
            var records = LoadRecordsUnsafe();
            var now = DateTime.UtcNow;
            var monthly = records
                .Where(r => r.TimestampUtc.Year == now.Year && r.TimestampUtc.Month == now.Month)
                .ToList();

            if (monthly.Count == 0)
            {
                return new MonthlyUsageSummary();
            }

            var actions = monthly
                .GroupBy(r => new { r.ActionId, r.ActionName })
                .Select(g => new ActionUsageSummary
                {
                    ActionId = g.Key.ActionId,
                    ActionName = g.Key.ActionName,
                    RunCount = g.Count(),
                    AvgLatencyMs = (int)Math.Round(g.Average(x => x.LatencyMs)),
                    TotalEstimatedTokens = g.Sum(x => x.EstimatedInputTokens + x.EstimatedOutputTokens),
                    TotalEstimatedCostUsd = decimal.Round(g.Sum(x => x.EstimatedCostUsd), 4)
                })
                .OrderByDescending(x => x.RunCount)
                .ThenBy(x => x.ActionName)
                .ToList();

            return new MonthlyUsageSummary
            {
                TotalRuns = monthly.Count,
                AvgLatencyMs = (int)Math.Round(monthly.Average(x => x.LatencyMs)),
                TotalEstimatedTokens = monthly.Sum(x => x.EstimatedInputTokens + x.EstimatedOutputTokens),
                EstimatedMonthlyCostUsd = decimal.Round(monthly.Sum(x => x.EstimatedCostUsd), 4),
                Actions = actions
            };
        }
    }

    public IReadOnlyList<ActionUsageRecord> GetRecentRecords(int limit = 25)
    {
        lock (_sync)
        {
            var clampedLimit = Math.Clamp(limit, 1, 200);
            return LoadRecordsUnsafe()
                .OrderByDescending(r => r.TimestampUtc)
                .Take(clampedLimit)
                .ToList();
        }
    }

    private static int EstimateTokens(int chars)
    {
        if (chars <= 0)
            return 0;

        // Conservative estimate for English-like text.
        return Math.Max(1, (int)Math.Ceiling(chars / 4.0));
    }

    private static decimal EstimateCostUsd(string provider, string model, int inputTokens, int outputTokens)
    {
        // Defaults are coarse estimates for transparency, not billing-accurate values.
        var p = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var m = (model ?? string.Empty).Trim().ToLowerInvariant();

        decimal inputPer1k;
        decimal outputPer1k;

        if (p == "groq")
        {
            inputPer1k = 0.0006m;
            outputPer1k = 0.0008m;
        }
        else
        {
            inputPer1k = 0.0015m;
            outputPer1k = 0.0020m;
        }

        if (m.Contains("70b"))
        {
            inputPer1k *= 1.4m;
            outputPer1k *= 1.4m;
        }
        else if (m.Contains("8b") || m.Contains("mini") || m.Contains("haiku") || m.Contains("flash"))
        {
            inputPer1k *= 0.8m;
            outputPer1k *= 0.8m;
        }

        var envInput = Environment.GetEnvironmentVariable("Spark_EST_INPUT_COST_PER_1K_USD");
        var envOutput = Environment.GetEnvironmentVariable("Spark_EST_OUTPUT_COST_PER_1K_USD");

        if (decimal.TryParse(envInput, out var overrideInput) && overrideInput >= 0)
            inputPer1k = overrideInput;

        if (decimal.TryParse(envOutput, out var overrideOutput) && overrideOutput >= 0)
            outputPer1k = overrideOutput;

        var inputCost = (inputTokens / 1000m) * inputPer1k;
        var outputCost = (outputTokens / 1000m) * outputPer1k;
        return decimal.Round(inputCost + outputCost, 6);
    }

    private static List<ActionUsageRecord> LoadRecordsUnsafe()
    {
        if (!File.Exists(MetricsPath))
            return new List<ActionUsageRecord>();

        try
        {
            var json = File.ReadAllText(MetricsPath);
            return JsonSerializer.Deserialize<List<ActionUsageRecord>>(json, JsonOptions) ?? new List<ActionUsageRecord>();
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("UsageMetricsService.LoadRecords", ex);
            return new List<ActionUsageRecord>();
        }
    }

    private static void SaveRecordsUnsafe(List<ActionUsageRecord> records)
    {
        var dir = Path.GetDirectoryName(MetricsPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var tempPath = MetricsPath + ".tmp";
        var backupPath = MetricsPath + ".bak";
        var json = JsonSerializer.Serialize(records, JsonOptions);
        File.WriteAllText(tempPath, json);

        if (File.Exists(MetricsPath))
        {
            File.Replace(tempPath, MetricsPath, backupPath, ignoreMetadataErrors: true);
            try { File.Delete(backupPath); } catch { }
            return;
        }

        File.Move(tempPath, MetricsPath);
    }
}
