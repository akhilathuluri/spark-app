using System.IO;
using System.Text.Json;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Maintains the last N processed results in memory and on disk.
/// All methods are called only from the UI thread; no locking needed.
/// </summary>
public class HistoryService
{
    private const int MaxEntries = 20;

    private static readonly string HistoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "history.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private List<HistoryEntry> _entries = new();
    private bool _loaded;

    /// <summary>Fired (on the original thread) whenever the list changes.</summary>
    public event Action? Changed;

    /// <summary>Newest-first snapshot of all saved entries.</summary>
    public IReadOnlyList<HistoryEntry> Entries
    {
        get { EnsureLoaded(); return _entries.AsReadOnly(); }
    }

    public void Add(HistoryEntry entry)
    {
        EnsureLoaded();
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries)
            _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        PersistAsync();
        Changed?.Invoke();
    }

    public void Clear()
    {
        EnsureLoaded();
        _entries.Clear();
        PersistAsync();
        Changed?.Invoke();
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("HistoryService.Load", ex);
            _entries = new();
        }
    }

    private void PersistAsync()
    {
        var snapshot = _entries.ToList();
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
                File.WriteAllText(HistoryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
            }
            catch (Exception ex)
            {
                DebugLogService.LogError("HistoryService.Persist", ex);
            }
        });
    }

    /// <summary>
    /// Synchronously persist history to disk — call this before app shutdown.
    /// Ensures no pending writes are lost.
    /// </summary>
    public void PersistSync()
    {
        try
        {
            var snapshot = _entries.ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(HistoryPath)!);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("HistoryService.PersistSync", ex);
        }
    }
}
