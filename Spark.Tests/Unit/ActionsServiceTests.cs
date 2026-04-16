using Spark.Services;
using Xunit;

namespace Spark.Tests.Unit;

public class ActionsServiceTests
{
    [Fact]
    public void GetSmartSuggestions_PrioritizesCodeActions_ForCodeText()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"Spark-test-{Guid.NewGuid()}", "settings.json");
        var settingsService = new SettingsService(settingsPath, new InMemorySecretStore());
        var sut = new ActionsService(settingsService);

        var actions = sut.GetSmartSuggestions("public class Demo { return; }");

        Assert.True(actions.Count > 2);
        Assert.Equal("explain_code", actions[0].Id);
        Assert.Equal("rewrite", actions[1].Id);
        Assert.Equal("fix_grammar", actions[2].Id);
    }

    [Fact]
    public void GetSmartSuggestions_PrioritizesEmailTone_ForOfficeAppContext()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"Spark-test-{Guid.NewGuid()}", "settings.json");
        var settingsService = new SettingsService(settingsPath, new InMemorySecretStore());
        var sut = new ActionsService(settingsService);

        var text = "Please review this update and share your thoughts by Friday.";
        var actions = sut.GetSmartSuggestions(text, "OUTLOOK");

        Assert.True(actions.Count > 3);
        Assert.Equal("change_tone", actions[0].Id);
        Assert.Equal("fix_grammar", actions[1].Id);
    }

    [Fact]
    public void GetSmartSuggestions_PrioritizesSummarize_ForLongFormText()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"Spark-test-{Guid.NewGuid()}", "settings.json");
        var settingsService = new SettingsService(settingsPath, new InMemorySecretStore());
        var sut = new ActionsService(settingsService);

        var longText = string.Join(" ", Enumerable.Repeat("This is a detailed sentence about project updates and decisions.", 40));
        var actions = sut.GetSmartSuggestions(longText, "msedge");

        Assert.True(actions.Count > 2);
        Assert.Equal("summarize", actions[0].Id);
        Assert.Equal("bullet_points", actions[1].Id);
    }
}

internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string key) => _map.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string value) => _map[key] = value;
    public void Remove(string key) => _map.Remove(key);
}
