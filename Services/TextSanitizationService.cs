using System.Text;
using System.Text.RegularExpressions;

namespace Spark.Services;

/// <summary>
/// Normalizes AI output for safe, readable UI rendering without changing user intent.
/// </summary>
public sealed class TextSanitizationService
{
    private static readonly Regex AnsiEscapeRe =
        new("\\x1B\\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled);

    private static readonly Regex WrappedCodeFenceRe =
        new("^\\s*```(?:[\\w#+.-]+)?\\s*\\n(?<body>[\\s\\S]*?)\\n```\\s*$", RegexOptions.Compiled);

    private static readonly Regex TooManyBlankLinesRe =
        new("\\n{4,}", RegexOptions.Compiled);

    // Common invisible characters that degrade readability in plain text views.
    private static readonly HashSet<char> InvisibleChars =
    [
        '\u200B', // zero-width space
        '\u200C', // zero-width non-joiner
        '\u200D', // zero-width joiner
        '\u2060', // word joiner
        '\uFEFF'  // zero-width no-break space / BOM
    ];

    /// <summary>
    /// Lightweight per-token cleanup for streaming UI updates.
    /// </summary>
    public string SanitizeChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return string.Empty;

        var normalized = NormalizeLineEndings(chunk)
            .Replace('\u00A0', ' ');

        normalized = AnsiEscapeRe.Replace(normalized, string.Empty);
        return RemoveDisallowedCharacters(normalized);
    }

    /// <summary>
    /// Full-pass cleanup after streaming completes.
    /// </summary>
    public string SanitizeFinal(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Normalize(NormalizationForm.FormKC);
        normalized = NormalizeLineEndings(normalized)
            .Replace('\u00A0', ' ')
            .Replace("\t", "    ");

        normalized = AnsiEscapeRe.Replace(normalized, string.Empty);
        normalized = RemoveDisallowedCharacters(normalized);
        normalized = TryUnwrapWholeCodeFence(normalized);
        normalized = TooManyBlankLinesRe.Replace(normalized, "\n\n\n");

        return normalized.Trim();
    }

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string TryUnwrapWholeCodeFence(string text)
    {
        var match = WrappedCodeFenceRe.Match(text);
        return match.Success ? match.Groups["body"].Value : text;
    }

    private static string RemoveDisallowedCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var ch in text)
        {
            if (ch == '\n' || ch == '\t')
            {
                sb.Append(ch);
                continue;
            }

            if (char.IsControl(ch)) continue;
            if (InvisibleChars.Contains(ch)) continue;

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
