namespace Spark.Models;

public class TextSelectionContext
{
    public IntPtr WindowHandle { get; set; }
    public uint ProcessId { get; set; }
    public string ProcessName { get; set; } = "unknown";
    public DateTime CapturedUtc { get; set; } = DateTime.UtcNow;
}
