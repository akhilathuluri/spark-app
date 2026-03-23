using System.IO;
using System.Text.Json;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Simple file-based logging for debugging errors and issues.
/// Logs to %APPDATA%\Spark\debug.log — used only for troubleshooting, never shown to users.
/// Thread-safe via lock; logs are silently dropped if file write fails.
/// </summary>
public static class DebugLogService
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "debug.log");

    private static readonly string DiagnosticPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "diagnostics.jsonl");

    private static readonly object _lock = new();

    /// <summary>
    /// Logs an exception with context. Safe to call from any thread.
    /// </summary>
    public static void LogError(string context, Exception ex)
    {
        LogMessage($"[ERROR] {context}: {ex.GetType().Name} — {ex.Message}\n{ex.StackTrace}");
    }

    /// <summary>
    /// Logs a general message. Safe to call from any thread.
    /// </summary>
    public static void LogMessage(string message)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logLine = $"[{timestamp}] {message}\n";

                // Append to file; keep it under 5 MB to avoid bloat
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 5_000_000)
                    File.Delete(LogPath); // rotate on size

                File.AppendAllText(LogPath, logLine);
            }
            catch { /* silently ignore log failures — must never throw */ }
        }
    }

    /// <summary>
    /// Logs a machine-readable JSON line for diagnostics and support analysis.
    /// </summary>
    public static void LogDiagnosticEvent(DiagnosticEvent evt)
    {
        lock (_lock)
        {
            try
            {
                var dir = Path.GetDirectoryName(DiagnosticPath);
                if (dir != null)
                    Directory.CreateDirectory(dir);

                var line = JsonSerializer.Serialize(evt) + "\n";
                File.AppendAllText(DiagnosticPath, line);
            }
            catch
            {
                // Diagnostics must never impact app execution.
            }
        }
    }
}
