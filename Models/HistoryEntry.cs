using System.Text.Json.Serialization;

namespace Spark.Models;

public class HistoryEntry
{
    public DateTime Timestamp   { get; set; } = DateTime.Now;
    public string ActionId      { get; set; } = string.Empty;
    public string ActionName    { get; set; } = string.Empty;
    public string ActionIcon    { get; set; } = string.Empty;
    public string SelectedText  { get; set; } = string.Empty;
    public string Result        { get; set; } = string.Empty;

    [JsonIgnore]
    public string RelativeTime
    {
        get
        {
            var diff = DateTime.Now - Timestamp;
            if (diff.TotalSeconds < 60)  return "just now";
            if (diff.TotalMinutes < 60)  return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours   < 24)  return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays    < 7)   return $"{(int)diff.TotalDays}d ago";
            return Timestamp.ToString("MMM d");
        }
    }

    [JsonIgnore]
    public string SelectedTextPreview =>
        SelectedText.Length > 80 ? SelectedText[..80].Trim() + "…" : SelectedText;

    [JsonIgnore]
    public string ResultPreview =>
        Result.Length > 120 ? Result[..120].Trim() + "…" : Result;
}
