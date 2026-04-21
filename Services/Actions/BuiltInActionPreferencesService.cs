using Spark.Models;

namespace Spark.Services.Actions;

public sealed class BuiltInActionPreferencesService
{
    public List<ActionItem> Apply(
        IReadOnlyList<ActionItem> defaults,
        IReadOnlyList<BuiltInActionPreference>? preferences)
    {
        var map = BuildPreferenceMap(preferences);
        var result = new List<ActionItem>(defaults.Count);

        foreach (var source in defaults)
        {
            var item = new ActionItem
            {
                Id = source.Id,
                Name = source.Name,
                Description = source.Description,
                Icon = source.Icon,
                Shortcut = source.Shortcut,
                Prompt = source.Prompt,
                IsBuiltIn = true
            };

            if (map.TryGetValue(item.Id, out var pref))
            {
                if (pref.IsDeleted)
                    continue;

                if (!string.IsNullOrWhiteSpace(item.Prompt) &&
                    !string.IsNullOrWhiteSpace(pref.PromptOverride))
                {
                    item.Prompt = pref.PromptOverride.Trim();
                }
            }

            result.Add(item);
        }

        return result;
    }

    public Dictionary<string, BuiltInActionPreference> BuildPreferenceMap(
        IReadOnlyList<BuiltInActionPreference>? preferences)
    {
        if (preferences is null || preferences.Count == 0)
            return new Dictionary<string, BuiltInActionPreference>(StringComparer.Ordinal);

        return preferences
            .Where(p => p != null)
            .Where(p => !string.IsNullOrWhiteSpace(p.ActionId))
            .Select(p => new BuiltInActionPreference
            {
                ActionId = p.ActionId.Trim(),
                PromptOverride = p.PromptOverride?.Trim() ?? string.Empty,
                IsDeleted = p.IsDeleted
            })
            .GroupBy(p => p.ActionId, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToDictionary(p => p.ActionId, p => p, StringComparer.Ordinal);
    }
}
