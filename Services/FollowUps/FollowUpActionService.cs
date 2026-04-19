using Spark.Models;

namespace Spark.Services.FollowUps;

public sealed class FollowUpActionService
{
    private static readonly IReadOnlyList<FollowUpActionOption> DefaultOptions = new List<FollowUpActionOption>
    {
        new()
        {
            Id = "shorter",
            Name = "Shorter",
            IconActionId = "shorten",
            PromptTemplate = "You are an expert at concise writing. Make the following text shorter while preserving the full meaning and tone. Remove redundancy and filler. Return only the rewritten text:\n\n{text}"
        },
        new()
        {
            Id = "more_formal",
            Name = "More Formal",
            IconActionId = "change_tone",
            PromptTemplate = "Rewrite the following text in a more formal and professional tone. Preserve meaning and key details. Return only the rewritten text:\n\n{text}"
        },
        new()
        {
            Id = "add_bullets",
            Name = "Add Bullets",
            IconActionId = "bullet_points",
            PromptTemplate = "Convert the following text into concise bullet points. Preserve key information and avoid adding new facts. Return only bullet points:\n\n{text}"
        },
        new()
        {
            Id = "translate",
            Name = "Translate",
            IconActionId = "translate",
            PromptTemplate = "Detect the language of the following text. If it is not English, translate it to natural English. If it is English, translate it to Spanish. Return only the translated text:\n\n{text}"
        },
        new()
        {
            Id = "length_50_words",
            Name = "50 Words",
            IconActionId = "shorten",
            PromptTemplate = "Rewrite the following text in approximately 50 words (target range: 45 to 55 words). Keep meaning and tone. Return only the rewritten text:\n\n{text}"
        },
        new()
        {
            Id = "tweet_length",
            Name = "Tweet Length",
            IconActionId = "shorten",
            PromptTemplate = "Rewrite the following text so it fits in 280 characters or less. Keep the core message clear. Return only the rewritten text:\n\n{text}"
        },
        new()
        {
            Id = "one_paragraph",
            Name = "One Paragraph",
            IconActionId = "rewrite",
            PromptTemplate = "Rewrite the following text as one cohesive paragraph while preserving its meaning. Return only the rewritten paragraph:\n\n{text}"
        }
    };

    public IReadOnlyList<FollowUpActionOption> GetSuggestedOptions(ActionItem? sourceAction, string resultText)
    {
        if (string.IsNullOrWhiteSpace(resultText))
            return Array.Empty<FollowUpActionOption>();

        var sourceId = sourceAction?.Id ?? string.Empty;
        var options = DefaultOptions
            .Where(option => !sourceId.Equals($"followup_{option.Id}", StringComparison.Ordinal))
            .Take(5)
            .ToList();

        return options;
    }

    public ActionItem ToActionItem(FollowUpActionOption option)
    {
        return new ActionItem
        {
            Id = $"followup_{option.Id}",
            Name = $"Follow-up: {option.Name}",
            Icon = string.Empty,
            Shortcut = string.Empty,
            Description = $"Follow-up action: {option.Name}",
            Prompt = option.PromptTemplate,
            IsBuiltIn = true
        };
    }
}
