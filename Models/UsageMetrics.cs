namespace Spark.Models;

public sealed class ActionUsageRecord
{
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string ActionId { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int LatencyMs { get; set; }
    public int InputChars { get; set; }
    public int OutputChars { get; set; }
    public int EstimatedInputTokens { get; set; }
    public int EstimatedOutputTokens { get; set; }
    public decimal EstimatedCostUsd { get; set; }
}

public sealed class ActionUsageSummary
{
    public string ActionId { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public int RunCount { get; set; }
    public int AvgLatencyMs { get; set; }
    public int TotalEstimatedTokens { get; set; }
    public decimal TotalEstimatedCostUsd { get; set; }
}

public sealed class MonthlyUsageSummary
{
    public int TotalRuns { get; set; }
    public int AvgLatencyMs { get; set; }
    public int TotalEstimatedTokens { get; set; }
    public decimal EstimatedMonthlyCostUsd { get; set; }
    public IReadOnlyList<ActionUsageSummary> Actions { get; set; } = Array.Empty<ActionUsageSummary>();
}
