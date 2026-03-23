namespace Spark.Models;

public class ReplaceValidationResult
{
    public bool IsSafe { get; set; }
    public bool FocusChanged { get; set; }
    public string SourceProcessName { get; set; } = "unknown";
    public string CurrentProcessName { get; set; } = "unknown";
    public string Reason { get; set; } = string.Empty;
}
