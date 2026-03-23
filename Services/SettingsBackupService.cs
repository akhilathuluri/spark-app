using System.IO;
using System.Text.Json;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Auto-creates and manages backups of settings.json to prevent total data loss from corruption.
/// Works alongside SettingsService without modifying its save logic.
/// Provides recovery mechanism if JSON is malformed.
/// 
/// Backup strategy:
///  - Creates numbered backup files (settings.backup.1.json, settings.backup.2.json, etc.)
///  - Keeps up to 5 backups (configurable via env var Spark_BACKUP_COUNT)
///  - On each save, rotates backups (new backup = highest number)
///  - On corruption detection, can restore from newest valid backup
/// </summary>
public class SettingsBackupService
{
    private readonly string _settingsPath;
    private readonly int _maxBackups;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsBackupService(string settingsPath)
    {
        _settingsPath = settingsPath;

        int backupCount = 5;
        if (int.TryParse(Environment.GetEnvironmentVariable("Spark_BACKUP_COUNT"), out var count))
        {
            backupCount = Math.Clamp(count, 2, 20);
        }
        _maxBackups = backupCount;
    }

    /// <summary>
    /// Call from SettingsService.Save() after successfully writing settings.
    /// Creates a backup and rotates old backups.
    /// Silently handles any backup errors so they never break save operations.
    /// </summary>
    public void CreateBackup()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                DebugLogService.LogMessage("SettingsBackupService: settings.json not yet written, skipping backup");
                return;
            }

            string settingsDir = Path.GetDirectoryName(_settingsPath) ?? "";
            if (string.IsNullOrEmpty(settingsDir))
                return;

            // Rotate existing backups: move .1 → .2, .2 → .3, etc., delete oldest
            for (int i = _maxBackups - 1; i >= 1; i--)
            {
                string oldBackup = Path.Combine(settingsDir, $"settings.backup.{i}.json");
                string newBackup = Path.Combine(settingsDir, $"settings.backup.{i + 1}.json");

                if (File.Exists(oldBackup))
                {
                    if (File.Exists(newBackup))
                        File.Delete(newBackup);
                    File.Move(oldBackup, newBackup);
                }
            }

            // Create new backup as .1
            string backupPath = Path.Combine(settingsDir, "settings.backup.1.json");
            File.Copy(_settingsPath, backupPath, overwrite: true);

            DebugLogService.LogMessage($"SettingsBackupService: Backup created at {backupPath}");
        }
        catch (Exception ex)
        {
            // Never throw from backup — just log and continue
            DebugLogService.LogError("SettingsBackupService.CreateBackup", ex);
        }
    }

    /// <summary>
    /// Validates the current settings.json for basic JSON integrity.
    /// Returns (isValid, errorMessage).
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateCurrentSettings()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return (false, "settings.json does not exist");

            string json = File.ReadAllText(_settingsPath);
            if (string.IsNullOrWhiteSpace(json))
                return (false, "settings.json is empty");

            // Try to deserialize — catches malformed JSON
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (settings == null)
                return (false, "settings.json deserialized to null");

            return (true, null);
        }
        catch (JsonException ex)
        {
            return (false, $"JSON parse error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Validation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Attempts to restore from the newest valid backup.
    /// Returns (success, backupUsed, errorDetails).
    /// If successful, restores that backup over the corrupted settings.json.
    /// </summary>
    public (bool Success, string? BackupUsed, string? ErrorDetails) TryRestoreFromBackup()
    {
        try
        {
            string settingsDir = Path.GetDirectoryName(_settingsPath) ?? "";
            if (string.IsNullOrEmpty(settingsDir))
                return (false, null, "Cannot determine settings directory");

            // Try backups in reverse order (newest first)
            for (int i = 1; i <= _maxBackups; i++)
            {
                string backupPath = Path.Combine(settingsDir, $"settings.backup.{i}.json");
                if (!File.Exists(backupPath))
                    continue;

                // Validate this backup
                try
                {
                    string json = File.ReadAllText(backupPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        // Found a valid backup — restore it
                        File.Copy(backupPath, _settingsPath, overwrite: true);
                        DebugLogService.LogMessage($"SettingsBackupService: Restored from {backupPath}");
                        return (true, backupPath, null);
                    }
                }
                catch { /* this backup is also corrupt, try next */ }
            }

            return (false, null, "No valid backups found to restore from");
        }
        catch (Exception ex)
        {
            return (false, null, $"Restore failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes all backups (for testing or manual cleanup).
    /// </summary>
    public void ClearAllBackups()
    {
        try
        {
            string settingsDir = Path.GetDirectoryName(_settingsPath) ?? "";
            for (int i = 1; i <= _maxBackups; i++)
            {
                string backupPath = Path.Combine(settingsDir, $"settings.backup.{i}.json");
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
            }
            DebugLogService.LogMessage("SettingsBackupService: All backups cleared");
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("SettingsBackupService.ClearAllBackups", ex);
        }
    }
}
