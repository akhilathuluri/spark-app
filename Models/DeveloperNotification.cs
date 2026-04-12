namespace Spark.Models;

public sealed class DeveloperNotification
{
    public string Id { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Priority { get; init; } = "normal";
    public string Type { get; init; } = "info";
    public DateTime CreatedAtLocal { get; init; } = DateTime.Now;
}
