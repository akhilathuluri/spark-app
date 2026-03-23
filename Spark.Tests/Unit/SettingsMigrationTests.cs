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
}
