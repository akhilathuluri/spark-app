using System.Text.Json;
using Spark.Services;
using Xunit;

namespace Spark.Tests.Unit;

public class SettingsMigrationTests
{
    [Fact]
    public void Load_MigratesPlaintextApiKeys_ToSecretStore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"Spark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");

        var oldJson = """
        {
          "SettingsVersion": 1,
          "HotkeyModifiers": "Ctrl+Shift",
          "HotkeyKey": "Space",
          "ActiveProvider": "OpenRouter",
          "OpenRouter": { "ApiKey": "or-secret", "Model": "openai/gpt-4o-mini" },
          "Groq": { "ApiKey": "groq-secret", "Model": "llama-3.1-8b-instant" },
          "CustomActions": [],
          "StartWithWindows": false,
          "Theme": "Dark"
        }
        """;

        File.WriteAllText(settingsPath, oldJson);
        var secretStore = new InMemorySecretStore();
        var sut = new SettingsService(settingsPath, secretStore);

        var loaded = sut.Load();

        Assert.Equal("or-secret", loaded.OpenRouter.ApiKey);
        Assert.Equal("groq-secret", loaded.Groq.ApiKey);
        Assert.Equal("or-secret", secretStore.Get("openrouter.apiKey"));
        Assert.Equal("groq-secret", secretStore.Get("groq.apiKey"));

        var persisted = JsonDocument.Parse(File.ReadAllText(settingsPath)).RootElement;
        Assert.Equal(3, persisted.GetProperty("SettingsVersion").GetInt32());
        Assert.Equal(string.Empty, persisted.GetProperty("OpenRouter").GetProperty("ApiKey").GetString());
        Assert.Equal(string.Empty, persisted.GetProperty("Groq").GetProperty("ApiKey").GetString());
    }

    [Fact]
    public void Load_RestoresFromBackup_WhenPrimarySettingsJsonIsCorrupted()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"Spark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");

        File.WriteAllText(settingsPath, "{ invalid json");

        var backupPath = Path.Combine(tempDir, "settings.backup.1.json");
        var backupJson = """
        {
          "SettingsVersion": 3,
          "HotkeyModifiers": "Ctrl+Shift",
          "HotkeyKey": "Space",
          "ActiveProvider": "OpenRouter",
          "OpenRouter": { "ApiKey": "", "Model": "openai/gpt-4o-mini" },
          "Groq": { "ApiKey": "", "Model": "llama-3.1-8b-instant" },
          "CustomActions": [],
          "StartWithWindows": false,
          "Theme": "Dark",
          "Language": "en"
        }
        """;
        File.WriteAllText(backupPath, backupJson);

        var secretStore = new InMemorySecretStore();
        var sut = new SettingsService(settingsPath, secretStore);
        sut.SetBackupService(new SettingsBackupService(settingsPath));

        var loaded = sut.Load();

        Assert.Equal("OpenRouter", loaded.ActiveProvider);
        Assert.Equal("Ctrl+Shift", loaded.HotkeyModifiers);
        Assert.False(string.IsNullOrWhiteSpace(sut.LastRecoveryMessage));
    }

    [Fact]
    public void Load_RemovesMalformedCustomActions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"Spark-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        var settingsPath = Path.Combine(tempDir, "settings.json");

        var malformedJson = """
        {
          "SettingsVersion": 3,
          "HotkeyModifiers": "Ctrl+Shift",
          "HotkeyKey": "Space",
          "ActiveProvider": "OpenRouter",
          "OpenRouter": { "ApiKey": "", "Model": "openai/gpt-4o-mini" },
          "Groq": { "ApiKey": "", "Model": "llama-3.1-8b-instant" },
          "CustomActions": [
            { "Id": "00000000-0000-0000-0000-000000000000", "Name": "", "Instructions": "x", "Icon": "" },
            { "Id": "11111111-1111-1111-1111-111111111111", "Name": "Valid", "Instructions": "Do {text}", "Icon": "" }
          ],
          "StartWithWindows": false,
          "Theme": "Dark",
          "Language": "en"
        }
        """;

        File.WriteAllText(settingsPath, malformedJson);
        var sut = new SettingsService(settingsPath, new InMemorySecretStore());

        var loaded = sut.Load();

        Assert.Single(loaded.CustomActions);
        Assert.Equal("Valid", loaded.CustomActions[0].Name);
        Assert.False(string.IsNullOrWhiteSpace(loaded.CustomActions[0].Icon));
    }
}
