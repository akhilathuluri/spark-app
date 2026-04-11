using System.IO;
using System.Text.Json;
using Spark.Models;

namespace Spark.Services;

public class DiagnosticsService
{
    private readonly SettingsService _settingsService;
    private readonly AnalyticsService _analyticsService;

    // Cache the bundle-enabled flag so TryWriteBundle never calls _settingsService.Load()
    // inside an error/crash handler. If Load() itself throws and triggers a crash report,
    // calling Load() again inside the handler causes infinite reentry.
    // RefreshSettings() is called at the TOP of each public Report method (before any error
    // path is entered), so the cache stays reasonably fresh without risking recursion.
    private bool _bundleEnabled;
    private bool _inReportMethod; // reentry guard

    public DiagnosticsService(SettingsService settingsService, AnalyticsService analyticsService)
    {
        _settingsService  = settingsService;
        _analyticsService = analyticsService;
        TryRefreshSettings(); // prime the cache on construction
    }

    public string NewCorrelationId() => Guid.NewGuid().ToString("N");

    public void ReportHandledError(string context, Exception ex, string correlationId, Dictionary<string, string>? metadata = null)
    {
        if (_inReportMethod) return;  // reentry guard: never nest Report calls
        _inReportMethod = true;
        try
        {
            TryRefreshSettings(); // refresh cache BEFORE entering error paths
            var evt = new DiagnosticEvent
            {
                EventType     = "handled_error",
                CorrelationId = correlationId,
                Context       = context,
                ErrorType     = ex.GetType().Name,
                Message       = ex.Message,
                Metadata      = metadata
            };
            DebugLogService.LogDiagnosticEvent(evt);
            _analyticsService.TrackDiagnosticError(context, ex.GetType().Name, correlationId, metadata);
            TryWriteBundle(evt);
        }
        finally { _inReportMethod = false; }
    }

    public void ReportCrash(string source, Exception ex, string correlationId)
    {
        if (_inReportMethod) return;
        _inReportMethod = true;
        try
        {
            TryRefreshSettings();
            var evt = new DiagnosticEvent
            {
                EventType     = "crash",
                CorrelationId = correlationId,
                Context       = source,
                ErrorType     = ex.GetType().Name,
                Message       = ex.Message
            };
            DebugLogService.LogDiagnosticEvent(evt);
            _analyticsService.TrackCrashReported(source, ex.GetType().Name, correlationId, null);
            TryWriteBundle(evt);
        }
        finally { _inReportMethod = false; }
    }

    public void ReportCaptureFailure(string context, string correlationId, CaptureDiagnostics diagnostics, TextSelectionContext? selectionContext)
    {
        if (_inReportMethod) return;
        _inReportMethod = true;
        try
        {
            TryRefreshSettings();
            var metadata = new Dictionary<string, string>
            {
                ["app_type"]    = diagnostics.AppType,
                ["process_name"]= diagnostics.ProcessName,
                ["strategy"]    = diagnostics.LastStrategy,
                ["attempts"]    = diagnostics.Attempts.ToString(),
                ["uia_fallback"]= diagnostics.UsedUiAutomationFallback ? "1" : "0",
                ["reason"]      = diagnostics.LastFailureReason
            };

            if (selectionContext != null)
            {
                metadata["source_process"] = selectionContext.ProcessName;
                metadata["source_pid"]     = selectionContext.ProcessId.ToString();
            }

            var evt = new DiagnosticEvent
            {
                EventType     = "capture_failure",
                CorrelationId = correlationId,
                Context       = context,
                ErrorType     = "CaptureFailure",
                Message       = diagnostics.LastFailureReason,
                Metadata      = metadata
            };
            DebugLogService.LogDiagnosticEvent(evt);
            _analyticsService.TrackCaptureFailed(correlationId, metadata);
            TryWriteBundle(evt);
        }
        finally { _inReportMethod = false; }
    }

    /// <summary>
    /// Refreshes the cached EnableDiagnosticsBundle setting.
    /// Called at the TOP of every public Report method, before any error-path code executes.
    /// This means it never runs inside a catch block where Load() itself may be broken.
    /// If Load() fails here, the exception is swallowed and the last cached value is kept.
    /// </summary>
    private void TryRefreshSettings()
    {
        try { _bundleEnabled = _settingsService.Load().EnableDiagnosticsBundle; }
        catch { /* keep last cached value — never throw from a diagnostic helper */ }
    }

    private void TryWriteBundle(DiagnosticEvent evt)
    {
        try
        {
            // Use cached flag — never call Load() here (risk of recursive crash loop).
            if (!_bundleEnabled)
                return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spark", "diagnostic-bundles");
            Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, $"bundle-{evt.CorrelationId}.json");
            var payload  = JsonSerializer.Serialize(evt, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, payload);
        }
        catch
        {
            // Diagnostics must never break the main app flow.
        }
    }
}
