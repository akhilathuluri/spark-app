using System.Text.RegularExpressions;
using Spark.Models;

namespace Spark.Services.Prompts;

public sealed class UserPromptPersonalizationService
{
    private static readonly Regex EmailOrSignatureIntentRegex = new(
        "\\b(email|mail|reply|respond|response|sign-?off|signature|sender|contact\\s+details)\\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NamePlaceholderRegex = new(
        "(\\[\\s*your\\s+name\\s*\\]|<\\s*your\\s+name\\s*>|\\{\\s*your\\s+name\\s*\\})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EmailPlaceholderRegex = new(
        "(\\[\\s*your\\s+email\\s*\\]|<\\s*your\\s+email\\s*>|\\{\\s*your\\s+email\\s*\\})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string BuildPrompt(string promptTemplate, string selectedText, AppSettings settings)
    {
        promptTemplate ??= string.Empty;
        selectedText ??= string.Empty;

        var basePrompt = promptTemplate.Replace("{text}", selectedText);

        var name = settings.UserProfile?.Name?.Trim() ?? string.Empty;
        var email = settings.UserProfile?.Email?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(email))
            return basePrompt;

        if (!ShouldIncludeProfile(promptTemplate, selectedText))
            return basePrompt;

        var senderLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(name))
            senderLines.Add($"- Name: {name}");
        if (!string.IsNullOrWhiteSpace(email))
            senderLines.Add($"- Email: {email}");

        var profileSection = string.Join(Environment.NewLine, senderLines);

        return $"""
Sender profile for identity/signature fields only:
{profileSection}

If the output contains placeholders like [Your Name] or [Your Email], replace them with the profile values above.
Do not add a sign-off, name, email address, or contact block unless the user explicitly requested one or the output already contains a matching placeholder.

{basePrompt}
""";
    }

    private static bool ShouldIncludeProfile(string promptTemplate, string selectedText)
    {
        if (ContainsIdentityPlaceholder(promptTemplate) || ContainsIdentityPlaceholder(selectedText))
            return true;

        // Keep personalization scoped to explicit sender-identity work. Generic rewrite,
        // grammar, tone, summarize, code, and explain actions should not inherit a signature.
        return EmailOrSignatureIntentRegex.IsMatch(promptTemplate);
    }

    private static bool ContainsIdentityPlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return NamePlaceholderRegex.IsMatch(value) || EmailPlaceholderRegex.IsMatch(value);
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
