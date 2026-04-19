using System.Text.RegularExpressions;
using Spark.Models;

namespace Spark.Services.Prompts;

public sealed class UserPromptPersonalizationService
{
    private static readonly Regex NamePlaceholderRegex = new(
        "(\\[\\s*your\\s+name\\s*\\]|<\\s*your\\s+name\\s*>|\\{\\s*your\\s+name\\s*\\})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailPlaceholderRegex = new(
        "(\\[\\s*your\\s+email\\s*\\]|<\\s*your\\s+email\\s*>|\\{\\s*your\\s+email\\s*\\})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string BuildPrompt(string promptTemplate, string selectedText, AppSettings settings)
    {
        var basePrompt = (promptTemplate ?? string.Empty).Replace("{text}", selectedText ?? string.Empty);

        var name = settings.UserProfile?.Name?.Trim() ?? string.Empty;
        var email = settings.UserProfile?.Email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
            return basePrompt;

        var senderLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
            senderLines.Add($"- Name: {name}");
        if (!string.IsNullOrWhiteSpace(email))
            senderLines.Add($"- Email: {email}");

        var profileSection = string.Join(Environment.NewLine, senderLines);

        return $"""
Use this sender profile whenever writing emails, messages, or sign-offs:
{profileSection}

If the output contains placeholders like [Your Name] or [Your Email], replace them with the profile values above.

{basePrompt}
""";
    }

    public string ApplyIdentityPlaceholders(string output, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(output))
            return output;

        var result = output;
        var name = settings.UserProfile?.Name?.Trim() ?? string.Empty;
        var email = settings.UserProfile?.Email?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
            result = NamePlaceholderRegex.Replace(result, name);

        if (!string.IsNullOrWhiteSpace(email))
            result = EmailPlaceholderRegex.Replace(result, email);

        return result;
    }
}
