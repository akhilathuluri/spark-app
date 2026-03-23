namespace Spark.Models;

public class CaptureDiagnostics
{
    public string AppType { get; set; } = "Unknown";
    public string ProcessName { get; set; } = "unknown";
    public string LastStrategy { get; set; } = "None";
    public bool UsedUiAutomationFallback { get; set; } = false;
    public bool Success { get; set; } = false;
    public int Attempts { get; set; } = 0;
}
