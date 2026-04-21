using Spark.Models;
using Spark.Services.Actions;
using Spark.Services.Recommendations;

namespace Spark.Services;

public class ActionsService
{
    private readonly SettingsService _settingsService;
    private readonly IconService _iconService = new();
    private readonly BuiltInActionCatalogService _builtInCatalog = new();
    private readonly BuiltInActionPreferencesService _builtInPreferences = new();
    private readonly ActionRecommendationEngine _recommendationEngine = new();
    private AppSettings? _settingsCache;

    // Called by App.xaml.cs after the user saves settings, so the next
    // hotkey press picks up any newly-added custom actions.
    public void InvalidateCache() => _settingsCache = null;

    public ActionsService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public List<ActionItem> GetAllActions()
    {
        _settingsCache ??= _settingsService.Load();
        var builtIns = _builtInCatalog.GetDefaults();
        builtIns = _builtInPreferences.Apply(builtIns, _settingsCache.BuiltInActionPreferences);
        var all = new List<ActionItem>(builtIns);

        foreach (var custom in _settingsCache.CustomActions)
        {
            all.Add(new ActionItem
            {
                Id = custom.Id.ToString(),
                Name = custom.Name,
                Description = custom.Description,
                Icon = _iconService.NormalizeCustomActionIcon(custom.Icon),
                Shortcut = "",
                Prompt = custom.Instructions,
                IsBuiltIn = false
            });
        }

        // Keep icon selection centralized for all built-in actions.
        foreach (var action in all.Where(a => a.IsBuiltIn))
            action.Icon = _iconService.GetActionGlyph(action.Id, action.Icon);

        return all;
    }

    public List<ActionItem> GetSmartSuggestions(string text, string? sourceProcessName = null)
    {
        var all = GetAllActions();

        var context = ActionRecommendationContext.Create(text, sourceProcessName);
        return _recommendationEngine.RankActions(all, context);
    }
}
