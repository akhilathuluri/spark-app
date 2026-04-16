using Spark.Models;

namespace Spark.Services.Recommendations;

public sealed class ActionRecommendationEngine
{
    private readonly IReadOnlyList<IActionRecommendationRule> _rules;

    public ActionRecommendationEngine()
    {
        _rules = new IActionRecommendationRule[]
        {
            new CodeContextRule(),
            new EmailContextRule(),
            new LongFormRule(),
            new BrowserResearchRule(),
            new ChatContextRule(),
            new PromptIntentRule(),
            new ShortSnippetRule(),
            new ListFormattingRule(),
            new DefaultFallbackRule()
        };
    }

    public List<ActionItem> RankActions(IReadOnlyList<ActionItem> actions, ActionRecommendationContext context)
    {
        if (actions.Count == 0)
            return new List<ActionItem>();

        var originalOrder = actions
            .Select((item, index) => new { item.Id, index })
            .ToDictionary(x => x.Id, x => x.index, StringComparer.Ordinal);

        var knownIds = new HashSet<string>(actions.Select(a => a.Id), StringComparer.Ordinal);
        var scoreById = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var rule in _rules)
        {
            if (!rule.Applies(context))
                continue;

            var rankedIds = rule.GetActionIds(context);
            var rank = rankedIds.Count;

            foreach (var id in rankedIds)
            {
                if (!knownIds.Contains(id))
                    continue;

                scoreById.TryGetValue(id, out var score);
                scoreById[id] = score + (rank * rule.Weight);
                rank--;
            }
        }

        return actions
            .OrderByDescending(a => scoreById.TryGetValue(a.Id, out var score) ? score : 0)
            .ThenBy(a => originalOrder[a.Id])
            .ToList();
    }

    private sealed class CodeContextRule : IActionRecommendationRule
    {
        public int Weight => 12;

        public bool Applies(ActionRecommendationContext context) =>
            context.IsCodeLike ||
            context.AppCategory == SourceAppCategory.CodeEditor ||
            context.AppCategory == SourceAppCategory.Terminal;

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "explain_code", "rewrite", "fix_grammar", "improve", "ask_ai" };
    }

    private sealed class EmailContextRule : IActionRecommendationRule
    {
        public int Weight => 10;

        public bool Applies(ActionRecommendationContext context) =>
            context.IsEmailLike ||
            (context.AppCategory == SourceAppCategory.Office && context.WordCount >= 6);

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "change_tone", "fix_grammar", "shorten", "improve", "translate" };
    }

    private sealed class LongFormRule : IActionRecommendationRule
    {
        public int Weight => 9;

        public bool Applies(ActionRecommendationContext context) =>
            context.WordCount >= 120 ||
            (context.CharCount >= 700 && context.LineCount >= 6);

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "summarize", "bullet_points", "shorten", "improve", "rewrite" };
    }

    private sealed class BrowserResearchRule : IActionRecommendationRule
    {
        public int Weight => 8;

        public bool Applies(ActionRecommendationContext context) =>
            context.AppCategory == SourceAppCategory.Browser &&
            (context.HasUrl || context.WordCount >= 80 || context.IsQuestionLike);

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "summarize", "bullet_points", "ask_ai", "translate", "explain" };
    }

    private sealed class ChatContextRule : IActionRecommendationRule
    {
        public int Weight => 7;

        public bool Applies(ActionRecommendationContext context) =>
            context.AppCategory == SourceAppCategory.Chat;

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "rewrite", "casual_tone", "shorten", "fix_grammar", "translate" };
    }

    private sealed class PromptIntentRule : IActionRecommendationRule
    {
        public int Weight => 6;

        public bool Applies(ActionRecommendationContext context) =>
            context.IsQuestionLike || context.ContainsPromptIntent;

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "ask_ai", "enhance_prompt", "rewrite", "improve", "explain" };
    }

    private sealed class ShortSnippetRule : IActionRecommendationRule
    {
        public int Weight => 5;

        public bool Applies(ActionRecommendationContext context) =>
            !context.IsCodeLike &&
            !context.IsEmailLike &&
            context.AppCategory != SourceAppCategory.Office &&
            context.WordCount > 0 &&
            context.WordCount <= 35;

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "fix_grammar", "rewrite", "improve", "expand" };
    }

    private sealed class ListFormattingRule : IActionRecommendationRule
    {
        public int Weight => 4;

        public bool Applies(ActionRecommendationContext context) => context.IsListLike;

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "bullet_points", "summarize", "improve", "shorten" };
    }

    private sealed class DefaultFallbackRule : IActionRecommendationRule
    {
        public int Weight => 1;

        public bool Applies(ActionRecommendationContext context) => true;

        public IReadOnlyList<string> GetActionIds(ActionRecommendationContext context) =>
            new[] { "rewrite", "summarize", "ask_ai" };
    }
}
