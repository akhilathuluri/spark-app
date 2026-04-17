namespace Spark.Models;

public sealed class AppUpdateInfo
{
    public string AppId { get; init; } = "Spark";
    public string AppName { get; init; } = "Spark";
    public string PreferredRuntimeId { get; init; } = "win-x64";
    public string SelectedRuntimeId { get; init; } = "win-x64";
    public string ProcessArchitecture { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public string CurrentVersion { get; init; } = "0.0.0";
    public string LatestVersion { get; init; } = "0.0.0";
    public string MinimumVersion { get; init; } = "0.0.0";
    public string ReleaseNotes { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string DownloadSha256 { get; init; } = string.Empty;
    public bool UsedDownloadFallback { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public bool IsRequiredUpdate { get; init; }
    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
}
