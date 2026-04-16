using System.Text.RegularExpressions;

namespace Spark.Services.Recommendations;

public enum SourceAppCategory
{
    Unknown,
    CodeEditor,
    Terminal,
    Browser,
    Chat,
    Office
}

public sealed class ActionRecommendationContext
{
    private static readonly Regex UrlRe = new(@"(https?://|www\.)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ListLineRe = new(@"^\s*([-*]|\d+\.)\s+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly HashSet<string> PromptIntentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "prompt", "write", "create", "generate", "draft", "improve", "rewrite", "summarize", "explain"
    };

    private static readonly string[] CodeIndicators =
    {
        "{", "}", "=>", "->", "def ", "func ", "class ", "import ", "using ", "var ", "const ", "let ",
        "function ", "#include", "public ", "private ", "protected ", "return ", "if (", "for (", "while (",
        "try {", "catch (", "namespace ", "Console.", "System.", "</", " />"
    };

    private static readonly string[] EmailIndicators =
    {
        "dear ", "hi ", "hello ", "best regards", "kind regards", "sincerely", "subject:", "thanks,"
    };

    public string Text { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public SourceAppCategory AppCategory { get; init; } = SourceAppCategory.Unknown;
    public int CharCount { get; init; }
    public int WordCount { get; init; }
    public int LineCount { get; init; }
    public int SentenceCount { get; init; }
    public bool IsCodeLike { get; init; }
    public bool IsEmailLike { get; init; }
    public bool IsQuestionLike { get; init; }
    public bool HasUrl { get; init; }
    public bool IsListLike { get; init; }
    public bool ContainsPromptIntent { get; init; }

    public static ActionRecommendationContext Create(string text, string? processName)
    {
        var safeText = text ?? string.Empty;
        var trimmed = safeText.Trim();
        var process = (processName ?? string.Empty).Trim();

        return new ActionRecommendationContext
        {
            Text = safeText,
            ProcessName = process,
            AppCategory = ResolveAppCategory(process),
            CharCount = safeText.Length,
            WordCount = CountWords(safeText),
            LineCount = CountLines(safeText),
            SentenceCount = CountSentences(safeText),
            IsCodeLike = IsCode(safeText),
            IsEmailLike = IsEmail(safeText),
            IsQuestionLike = IsQuestion(trimmed),
            HasUrl = UrlRe.IsMatch(safeText),
            IsListLike = ListLineRe.IsMatch(safeText),
            ContainsPromptIntent = ContainsPromptKeywords(trimmed)
        };
    }

    private static SourceAppCategory ResolveAppCategory(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return SourceAppCategory.Unknown;

        var p = processName.Replace(".exe", string.Empty, StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

        if (p.Contains("code") || p.Contains("devenv") || p.Contains("rider") || p.Contains("notepad++") || p.Contains("sublime"))
            return SourceAppCategory.CodeEditor;

        if (p.Contains("windowsterminal") || p == "cmd" || p.Contains("powershell") || p == "wt")
            return SourceAppCategory.Terminal;

        if (p.Contains("chrome") || p.Contains("msedge") || p.Contains("firefox") || p.Contains("brave"))
            return SourceAppCategory.Browser;

        if (p.Contains("teams") || p.Contains("slack") || p.Contains("discord") || p.Contains("telegram") || p.Contains("whatsapp"))
            return SourceAppCategory.Chat;

        if (p.Contains("outlook") || p.Contains("winword") || p.Contains("onenote") || p.Contains("thunderbird"))
            return SourceAppCategory.Office;

        return SourceAppCategory.Unknown;
    }

    private static bool IsCode(string text) =>
        CodeIndicators.Any(indicator => text.Contains(indicator, StringComparison.Ordinal));

    private static bool IsEmail(string text) =>
        EmailIndicators.Any(indicator => text.Contains(indicator, StringComparison.OrdinalIgnoreCase));

    private static bool IsQuestion(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.Contains('?'))
            return true;

        var lower = text.ToLowerInvariant();
        return lower.StartsWith("how ") || lower.StartsWith("why ") || lower.StartsWith("what ") ||
               lower.StartsWith("when ") || lower.StartsWith("where ") || lower.StartsWith("can ") ||
               lower.StartsWith("could ") || lower.StartsWith("should ");
    }

    private static bool ContainsPromptKeywords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = text.Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ':', ';', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => PromptIntentKeywords.Contains(token));
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return normalized.Split('\n').Length;
    }

    private static int CountSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Count(c => c == '.' || c == '!' || c == '?');
    }
}
