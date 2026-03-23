using System.IO;
using System.Text.Json;
using Spark.Models;

namespace Spark.Services;

public class DiagnosticsService
{
    private readonly SettingsService _settingsService;
    private readonly AnalyticsService _analyticsService;

    public DiagnosticsService(SettingsService settingsService, AnalyticsService analyticsService)
    {
        _settingsService = settingsService;
        _analyticsService = analyticsService;
    }

    public string NewCorrelationId() => Guid.NewGuid().ToString("N");

    public void ReportHandledError(string context, Exception ex, string correlationId, Dictionary<string, string>? metadata = null)
    {
        var evt = new DiagnosticEvent
        {
            EventType = "handled_error",
            CorrelationId = correlationId,
            Context = context,
            ErrorType = ex.GetType().Name,
            Message = ex.Message,
            Metadata = metadata
        };

        DebugLogService.LogDiagnosticEvent(evt);
        _analyticsService.TrackDiagnosticError(context, ex.GetType().Name, correlationId);
        TryWriteBundle(evt);
    }

    public void ReportCrash(string source, Exception ex, string correlationId)
    {
        var evt = new DiagnosticEvent
        {
            EventType = "crash",
            CorrelationId = correlationId,
            Context = source,
            ErrorType = ex.GetType().Name,
            Message = ex.Message
        };

        DebugLogService.LogDiagnosticEvent(evt);
        _analyticsService.TrackCrashReported(source, ex.GetType().Name, correlationId);
        TryWriteBundle(evt);
    }

    private void TryWriteBundle(DiagnosticEvent evt)
    {
        try
        {
            var settings = _settingsService.Load();
            if (!settings.EnableDiagnosticsBundle)
                return;

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spark", "diagnostic-bundles");
            Directory.CreateDirectory(dir);

            var filePath = Path.Combine(dir, $"bundle-{evt.CorrelationId}.json");
            var payload = JsonSerializer.Serialize(evt, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, payload);
        }
        catch
        {
            // Diagnostics must never break the main app flow.
        }
    }
}
