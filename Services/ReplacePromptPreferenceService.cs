namespace Spark.Services;

/// <summary>
/// Stores in-memory unsafe replace prompt preferences for the current app session.
/// </summary>
public static class ReplacePromptPreferenceService
{
    private static bool _suppressUnsafePromptForSession;

    public static bool ShouldShowUnsafePrompt() => !_suppressUnsafePromptForSession;

    public static void SuppressUnsafePromptForSession() => _suppressUnsafePromptForSession = true;
}
