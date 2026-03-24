using System.IO;
using System.Text.Json;
using System.Linq;
using Spark.Models;

namespace Spark.Services;

public class SettingsService
{
    private static readonly string DefaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spark", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private const string OpenRouterApiKeyName = "openrouter.apiKey";
    private const string GroqApiKeyName = "groq.apiKey";

    private readonly ISecretStore _secretStore;
    private readonly string _settingsPath;
    private SettingsBackupService? _backupService;  // Optional: set via SetBackupService()

    public string? LastRecoveryMessage { get; private set; }

    public SettingsService()
        : this(DefaultSettingsPath, new DpapiSecretStore())
    {
    }

    public SettingsService(ISecretStore secretStore)
        : this(DefaultSettingsPath, secretStore)
    {
    }

    public SettingsService(string settingsPath, ISecretStore secretStore)
    {
        _settingsPath = settingsPath;
        _secretStore = secretStore;
    }

    /// <summary>
    /// Optional: attach a backup service to auto-create backups after each save.
    /// Non-breaking: If not set, Save() works normally without backups.
    /// (Issue #6: Settings corruption only recovers fields, add backup/restore logic)
    /// </summary>
    public void SetBackupService(SettingsBackupService backupService)
    {
        _backupService = backupService;
    }

    public AppSettings Load()
    {
        LastRecoveryMessage = null;

        try
        {
            var settings = LoadFromPrimaryOrBackup(out var restoredFromBackup);

            bool migratedFromPlaintext = HydrateSecrets(settings);
            bool migrated = false;
            bool recovered = ValidateAndRepair(settings, out var recoveryMessage);

            if (settings.SettingsVersion < 2)
            {
                settings.SettingsVersion = 2;
                migrated = true;
            }

            if (settings.SettingsVersion < 3)
            {
                settings.SettingsVersion = 3;
                migrated = true;
            }

            if (migratedFromPlaintext || migrated || recovered)
            {
                Save(settings);
            }

            if (restoredFromBackup)
            {
                LastRecoveryMessage = string.IsNullOrWhiteSpace(recoveryMessage)
                    ? "Recovered settings from the latest valid backup."
                    : $"Recovered settings from backup and repaired fields: {recoveryMessage}";
            }
            else
            {
                LastRecoveryMessage = recoveryMessage;
            }

            return settings;
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("SettingsService.Load", ex);
        }

        return new AppSettings();
    }

    private AppSettings LoadFromPrimaryOrBackup(out bool restoredFromBackup)
    {
        restoredFromBackup = false;

        if (!File.Exists(_settingsPath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            DebugLogService.LogError("SettingsService.Load.Parse", ex);

            if (_backupService == null)
                return new AppSettings();

            var restore = _backupService.TryRestoreFromBackup();
            if (!restore.Success)
            {
                DebugLogService.LogMessage($"SettingsService.Load: backup restore failed - {restore.ErrorDetails}");
                return new AppSettings();
            }

            restoredFromBackup = true;
            var restoredJson = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(restoredJson, JsonOptions) ?? new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            PersistSecrets(settings);
            var persisted = CloneWithoutSecrets(settings);

            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            var json = JsonSerializer.Serialize(persisted, JsonOptions);
            WriteAllTextAtomic(_settingsPath, json);

            // Create backup after successful save (if backup service attached)
            // Non-breaking: if no backup service, this is silently skipped
            _backupService?.CreateBackup();
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("SettingsService.Save", ex);
        }
    }

    public void ClearStoredApiKeys()
    {
        _secretStore.Remove(OpenRouterApiKeyName);
        _secretStore.Remove(GroqApiKeyName);
    }

    private bool HydrateSecrets(AppSettings settings)
    {
        bool migrated = false;

        string? openRouterFromStore = null;
        try
        {
            openRouterFromStore = _secretStore.Get(OpenRouterApiKeyName);
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("SettingsService.HydrateSecrets.OpenRouterGet", ex);
        }

        if (!string.IsNullOrWhiteSpace(openRouterFromStore))
        {
            settings.OpenRouter.ApiKey = openRouterFromStore;
        }
        else if (!string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey))
        {
            try
            {
                _secretStore.Set(OpenRouterApiKeyName, settings.OpenRouter.ApiKey);
                migrated = true;
            }
            catch (Exception ex)
            {
                DebugLogService.LogError("SettingsService.HydrateSecrets.OpenRouterSet", ex);
            }
        }

        string? groqFromStore = null;
        try
        {
            groqFromStore = _secretStore.Get(GroqApiKeyName);
        }
        catch (Exception ex)
        {
            DebugLogService.LogError("SettingsService.HydrateSecrets.GroqGet", ex);
        }

        if (!string.IsNullOrWhiteSpace(groqFromStore))
        {
            settings.Groq.ApiKey = groqFromStore;
        }
        else if (!string.IsNullOrWhiteSpace(settings.Groq.ApiKey))
        {
            try
            {
                _secretStore.Set(GroqApiKeyName, settings.Groq.ApiKey);
                migrated = true;
            }
            catch (Exception ex)
            {
                DebugLogService.LogError("SettingsService.HydrateSecrets.GroqSet", ex);
            }
        }

        return migrated;
    }

    private void PersistSecrets(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.OpenRouter.ApiKey))
        {
            try
            {
                _secretStore.Set(OpenRouterApiKeyName, settings.OpenRouter.ApiKey);
            }
            catch (Exception ex)
            {
                DebugLogService.LogError("SettingsService.PersistSecrets.OpenRouter", ex);
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.Groq.ApiKey))
        {
            try
            {
                _secretStore.Set(GroqApiKeyName, settings.Groq.ApiKey);
            }
            catch (Exception ex)
            {
                DebugLogService.LogError("SettingsService.PersistSecrets.Groq", ex);
            }
        }
    }

    private static AppSettings CloneWithoutSecrets(AppSettings settings)
    {
        return new AppSettings
        {
            SettingsVersion = 3,
            HotkeyModifiers = settings.HotkeyModifiers,
            HotkeyKey = settings.HotkeyKey,
            ActiveProvider = settings.ActiveProvider,
            OpenRouter = new OpenRouterSettings
            {
                ApiKey = string.Empty,
                Model = settings.OpenRouter.Model
            },
            Groq = new GroqSettings
            {
                ApiKey = string.Empty,
                Model = settings.Groq.Model
            },
            CustomActions = settings.CustomActions.Select(a => new CustomAction
            {
                Id = a.Id,
                Name = a.Name,
                Description = a.Description,
                Instructions = a.Instructions,
                Icon = a.Icon
            }).ToList(),
            StartWithWindows = settings.StartWithWindows,
            Theme = settings.Theme,
            Language = settings.Language,
            SafeReplacePreviewMode = settings.SafeReplacePreviewMode,
            EnableDiagnosticsBundle = settings.EnableDiagnosticsBundle
        };
    }

    private static bool ValidateAndRepair(AppSettings settings, out string? recoveryMessage)
    {
        var recovered = new List<string>();

        if (string.IsNullOrWhiteSpace(settings.HotkeyModifiers))
        {
            settings.HotkeyModifiers = "Ctrl+Shift";
            recovered.Add("hotkey modifiers");
        }

        if (string.IsNullOrWhiteSpace(settings.HotkeyKey))
        {
            settings.HotkeyKey = "Space";
            recovered.Add("hotkey key");
        }

        if (settings.ActiveProvider != "OpenRouter" && settings.ActiveProvider != "Groq")
        {
            settings.ActiveProvider = "OpenRouter";
            recovered.Add("AI provider");
        }

        settings.CustomActions ??= new List<CustomAction>();
        int beforeCustomCount = settings.CustomActions.Count;
        settings.CustomActions = settings.CustomActions
            .Where(a => a != null)
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.Instructions))
            .Select(a =>
            {
                a.Id = a.Id == Guid.Empty ? Guid.NewGuid() : a.Id;
                a.Icon = string.IsNullOrWhiteSpace(a.Icon) ? "✨" : a.Icon;
                a.Description ??= string.Empty;
                return a;
            })
            .GroupBy(a => a.Id)
            .Select(g => g.First())
            .ToList();

        if (settings.CustomActions.Count != beforeCustomCount)
            recovered.Add("custom actions");

        var language = settings.Language?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(language) || (language != "en" && language != "es"))
        {
            settings.Language = "en";
            recovered.Add("language");
        }

        if (string.IsNullOrWhiteSpace(settings.Theme))
        {
            settings.Theme = "Dark";
            recovered.Add("theme");
        }

        settings.SettingsVersion = Math.Max(settings.SettingsVersion, 3);

        if (recovered.Count == 0)
        {
            recoveryMessage = null;
            return false;
        }

        recoveryMessage = $"Recovered invalid settings: {string.Join(", ", recovered)}.";
        return true;
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.tmp");
        var backupPath = path + ".bak";

        File.WriteAllText(tempPath, content);

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: true);
            try { File.Delete(backupPath); } catch { }
            return;
        }

        File.Move(tempPath, path);
    }
}
