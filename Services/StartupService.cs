using Microsoft.Win32;

namespace Spark.Services;

/// <summary>
/// Manages the Windows "Start with login" registry entry under
/// HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run.
/// Modular static class — no state, safe to call from any thread.
/// </summary>
public static class StartupService
{
    private const string AppName  = "Spark";
    private const string RunKey   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>Returns true if the run-on-login registry value currently exists.</summary>
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }

    /// <summary>
    /// Creates or removes the registry value to match the requested state.
    /// Uses the current process path so the correct exe is registered even
    /// when running from different directories (debug vs release).
    /// </summary>
    public static void Apply(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath
                    ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? string.Empty;

                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch { /* silently ignore if registry is not accessible */ }
    }
}
