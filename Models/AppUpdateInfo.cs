namespace Spark.Models;

public sealed class AppUpdateInfo
{
    public string AppId { get; init; } = "Spark";
    public string AppName { get; init; } = "Spark";
    public string CurrentVersion { get; init; } = "0.0.0";
    public string LatestVersion { get; init; } = "0.0.0";
    public string MinimumVersion { get; init; } = "0.0.0";
    public string ReleaseNotes { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public bool IsUpdateAvailable { get; init; }
    public bool IsRequiredUpdate { get; init; }
    public DateTime CheckedAtUtc { get; init; } = DateTime.UtcNow;
}
