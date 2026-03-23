namespace Spark.Models;

public class DiagnosticEvent
{
    public string EventType { get; set; } = "error";
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public string Context { get; set; } = string.Empty;
    public string ErrorType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string>? Metadata { get; set; }
}
