using Spark.Models;

namespace Spark.Services.Actions;

public sealed class SmartLengthActionCatalog
{
    private static readonly IReadOnlyList<ActionItem> LengthActions = new List<ActionItem>
    {
        new()
        {
            Id = "length_50_words",
            Name = "50 Words",
            Icon = "50",
            Shortcut = "",
            Description = "Rewrite to about 50 words while preserving meaning",
            IsBuiltIn = true,
            Prompt = "You are an expert concise writer. Rewrite the following text in approximately 50 words (target range: 45 to 55 words). Keep the original meaning, tone, and key details. Return only the rewritten text:\n\n{text}"
        },
        new()
        {
            Id = "tweet_length",
            Name = "Tweet Length",
            Icon = "🐦",
            Shortcut = "",
            Description = "Rewrite to fit within 280 characters",
            IsBuiltIn = true,
            Prompt = "Rewrite the following text so it fits in 280 characters or less. Keep the key message clear and engaging. Return only the rewritten text:\n\n{text}"
        },
        new()
        {
            Id = "one_paragraph",
            Name = "One Paragraph",
            Icon = "¶",
            Shortcut = "",
            Description = "Convert into one cohesive paragraph",
            IsBuiltIn = true,
            Prompt = "Rewrite the following text as one cohesive paragraph. Preserve the original intent and important details. Return only the rewritten paragraph:\n\n{text}"
        },
        new()
        {
            Id = "bullet_summary",
            Name = "Bullet Summary",
            Icon = "•",
            Shortcut = "",
            Description = "Summarize into concise bullet points",
            IsBuiltIn = true,
            Prompt = "Summarize the following text into 4 to 6 concise bullet points. Include only the most important information. Return only bullet points:\n\n{text}"
        }
    };

    public IReadOnlyList<ActionItem> GetActions() => LengthActions;
}
