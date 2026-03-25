namespace Spark.Models;

/// <summary>
/// Describes developer-facing metadata shown in the More Info window.
/// Kept as a model to make future updates and localization straightforward.
/// </summary>
public class DeveloperInfo
{
    public string AppName { get; set; } = "Spark";
    public string DeveloperName { get; set; } = "Athuluri Akhil";
    public string DeveloperMessage { get; set; } =
        "Thank you for using Spark. Your feedback helps shape upcoming improvements and new features.";
    public string ContactUrl { get; set; } = "https://usespark.vercel.app/issues";
    public string ReportIssueUrl { get; set; } = "https://usespark.vercel.app/issues";

    public static DeveloperInfo Default => new();
}
