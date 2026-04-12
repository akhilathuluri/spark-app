using System.IO;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Persists onboarding state using a dedicated marker file to make first-run detection reliable.
/// </summary>
public sealed class OnboardingStateService
{
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "onboarding.completed");

    public bool ShouldShowOnboarding(AppSettings settings)
    {
        // Show onboarding unless both settings flag and marker confirm completion.
        return !settings.OnboardingCompleted || !File.Exists(MarkerPath);
    }

    public void MarkCompleted(SettingsService settingsService)
    {
        try
        {
            var settings = settingsService.Load();
            if (!settings.OnboardingCompleted)
            {
                settings.OnboardingCompleted = true;
                settingsService.Save(settings);
            }

            var dir = Path.GetDirectoryName(MarkerPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(MarkerPath))
                File.WriteAllText(MarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("OnboardingStateService.MarkCompleted", ex);
        }
    }
}
