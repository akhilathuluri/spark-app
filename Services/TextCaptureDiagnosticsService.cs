using System.Collections.Generic;
using System.Linq;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Analyzes text capture failures and provides app-specific diagnostics and recommendations.
/// Helps debug why capture fails in Terminal, Slack, VS Code, etc.
/// Tracks failure patterns to identify root causes and suggest workarounds.
/// Non-breaking: Purely diagnostic, no changes to capture logic.
/// </summary>
public class TextCaptureDiagnosticsService
{
    private readonly List<CaptureFailureRecord> _failureHistory = new();
    private const int MaxHistoryEntries = 100;

    public record CaptureFailureRecord(
        DateTime UtcTime,
        string AppName,
        string ProcessName,
        string LastStrategy,
        string Reason);

    // App-specific known issues and workarounds
    private static readonly Dictionary<string, AppCaptureFacts> KnownApps = new()
    {
        ["conhost"] = new AppCaptureFacts(
            Name: "Windows Terminal/Command Prompt",
            IssueDocId: "terminal_copy",
            KnownIssues: "Ctrl+C intercepted by shell, may not copy to clipboard. Try right-click copy.",
            WorkaroundHint: "Use WM_COPY message or UI Automation"
        ),
        ["slack"] = new AppCaptureFacts(
            Name: "Slack",
            IssueDocId: "slack_selection",
            KnownIssues: "Rich text selection not always convertible via Ctrl+C.",
            WorkaroundHint: "Slack may need UI Automation or native message dispatch"
        ),
        ["thunderbird"] = new AppCaptureFacts(
            Name: "Thunderbird",
            IssueDocId: "email_rich_text",
            KnownIssues: "Email composition areas combine text + formatting; plain-text extraction unreliable.",
            WorkaroundHint: "Consider Rich Text format or UI Automation"
        ),
        ["winword"] = new AppCaptureFacts(
            Name: "Microsoft Word",
            IssueDocId: "word_complex_doc",
            KnownIssues: "Complex docs with fields, bookmarks may not copy cleanly.",
            WorkaroundHint: "Try Ctrl+Insert or clipboard format inspection"
        ),
        ["explorer"] = new AppCaptureFacts(
            Name: "Windows Explorer",
            IssueDocId: "explorer_paths",
            KnownIssues: "Explorer isn't primarily a text editor; selection behavior varies by view mode.",
            WorkaroundHint: "Only works in list/details view, not thumbnail view"
        ),
        ["code"] = new AppCaptureFacts(
            Name: "VS Code",
            IssueDocId: "vscode_selection",
            KnownIssues: "Electron apps sometimes lag in copy; try Ctrl+Insert.",
            WorkaroundHint: "Ensure Editor has focus, not sidebar"
        ),
        ["chrome"] = new AppCaptureFacts(
            Name: "Google Chrome",
            IssueDocId: "browser_app",
            KnownIssues: "Web apps may have custom copy handlers.",
            WorkaroundHint: "Works best on plain web pages; rich apps may fail"
        ),
        ["msedge"] = new AppCaptureFacts(
            Name: "Microsoft Edge",
            IssueDocId: "browser_app",
            KnownIssues: "Web apps may have custom copy handlers.",
            WorkaroundHint: "Works best on plain web pages; rich apps may fail"
        ),
    };

    public record AppCaptureFacts(
        string Name,
        string IssueDocId,
        string KnownIssues,
        string WorkaroundHint);

    /// <summary>
    /// Records a capture failure and analyzes it for patterns.
    /// Call from TextCaptureService after all strategies fail.
    /// </summary>
    public void RecordFailure(CaptureDiagnostics diagnostics, TextSelectionContext? context)
    {
        try
        {
            var failureReason = diagnostics.LastStrategy == "None"
                ? "No strategy completed successfully"
                : $"Failed after {diagnostics.Attempts} attempts (last: {diagnostics.LastStrategy})";

            var record = new CaptureFailureRecord(
                UtcTime: DateTime.UtcNow,
                AppName: diagnostics.ProcessName ?? "Unknown",
                ProcessName: context?.ProcessName ?? "Unknown",
                LastStrategy: diagnostics.LastStrategy ?? "None",
                Reason: failureReason);

            lock (_failureHistory)
            {
                _failureHistory.Add(record);
                if (_failureHistory.Count > MaxHistoryEntries)
                    _failureHistory.RemoveAt(0);
            }

            DebugLogService.LogMessage($"TextCaptureDiagnostics: Recorded failure for {record.AppName} - {failureReason}");
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("TextCaptureDiagnosticsService.RecordFailure", ex);
        }
    }

    /// <summary>
    /// Analyzes recent failures and returns a detailed diagnostics string.
    /// Identifies patterns (same app repeatedly failing) and suggests workarounds.
    /// </summary>
    public string GetDetailedDiagnostics()
    {
        lock (_failureHistory)
        {
            if (_failureHistory.Count == 0)
                return "No recent capture failures recorded.";

            var failuresLast5Min = _failureHistory
                .Where(f => DateTime.UtcNow - f.UtcTime < TimeSpan.FromMinutes(5))
                .ToList();

            if (failuresLast5Min.Count == 0)
                return "No failures in last 5 minutes.";

            var byApp = failuresLast5Min.GroupBy(f => f.ProcessName).ToList();
            var distinctStrategies = failuresLast5Min.Select(f => f.LastStrategy).Distinct().ToList();

            var summary = $"Failures (last 5 min): {failuresLast5Min.Count} attempts, " +
                         $"{byApp.Count} distinct app(s), " +
                         $"{distinctStrategies.Count} strategies attempted";

            if (byApp.Count == 1)
            {
                var appName = byApp[0].Key;
                var appFacts = GetAppInfo(appName);
                summary += $"\n\n**{appFacts.Name} ({appName})**\n" +
                          $"Known issue: {appFacts.KnownIssues}\n" +
                          $"Try: {appFacts.WorkaroundHint}";
            }
            else
            {
                summary += "\n\nMultiple apps affected — likely a system issue (clipboard access, permissions).";
            }

            return summary;
        }
    }

    /// <summary>
    /// Returns app-specific guidance.
    /// </summary>
    public AppCaptureFacts GetAppInfo(string processName)
    {
        processName = processName.ToLowerInvariant().Replace(".exe", "");

        if (KnownApps.TryGetValue(processName, out var facts))
            return facts;

        // Generic app: provide general guidance
        return new AppCaptureFacts(
            Name: processName,
            IssueDocId: "generic_app",
            KnownIssues: "Clipboard access may be restricted or text format incompatible.",
            WorkaroundHint: "Try Ctrl+Insert; some apps block Ctrl+C."
        );
    }

    /// <summary>
    /// Exports failure history as CSV for analysis (for support/debugging).
    /// </summary>
    public string ExportAsCSV()
    {
        lock (_failureHistory)
        {
            var lines = new List<string>
            {
                "UtcTime,AppName,ProcessName,LastStrategy,Reason"
            };

            foreach (var fail in _failureHistory)
            {
                var line = $"\"{fail.UtcTime:O}\",\"{fail.AppName}\",\"{fail.ProcessName}\",\"{fail.LastStrategy}\",\"{fail.Reason}\"";
                lines.Add(line);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Clears failure history (after analysis or on app restart).
    /// </summary>
    public void ClearHistory()
    {
        lock (_failureHistory)
        {
            _failureHistory.Clear();
        }
    }
}
