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
}

internal sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string key) => _map.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string value) => _map[key] = value;
    public void Remove(string key) => _map.Remove(key);
}
