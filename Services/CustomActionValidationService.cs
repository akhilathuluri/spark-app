using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Spark.Models;

namespace Spark.Services;

/// <summary>
/// Validates custom action prompts to detect potential injection attacks or problematic patterns.
/// Provides warnings in Settings before user saves, preventing misconfiguration.
/// Non-breaking: Audit-only, existing prompts work fine, user just sees warnings.
/// </summary>
public class CustomActionValidationService
{
    public record ValidationWarning(
        WarningLevel Level,
        string Pattern,
        string Description,
        string? Suggestion);

    public enum WarningLevel
    {
        Info,      // Just informational
        Caution,   // May cause unexpected behavior
        Warning    // Likely a problem
    }

    // Patterns that suggest potential issues
    private static readonly List<ValidationPattern> ValidationPatterns = new()
    {
        new ValidationPattern(
            WarningLevel.Warning,
            @"\{text\}.*\{text\}",
            "Multiple {text} placeholders",
            "Prompt uses {text} more than once — each occurrence will be replaced, potentially creating duplicate content"
        ),
        new ValidationPattern(
            WarningLevel.Caution,
            @"ignore.*instruction|forget.*instruction|disregard",
            "Prompt injection hint",
            "Prompt contains language that might try to override Spark's intentions. The LLM will receive clean {text}, not instructions."
        ),
        new ValidationPattern(
            WarningLevel.Caution,
            @"execute|run|compile|eval",
            "Executable language suggested",
            "Prompt asks the LLM to execute code. Spark does not execute code; output is plain text."
        ),
        new ValidationPattern(
            WarningLevel.Info,
            @"\{text\}.*Return only",
            "Constrains LLM output",
            "Good practice: 'Return only the [result]' focuses the LLM on delivering just what you need"
        ),
        new ValidationPattern(
            WarningLevel.Caution,
            @"modify.*{text}.*as.*system prompt|prompt.*as.*instruction",
            "Treats {text} as instruction",
            "Prompt treats user's selected text as instructions to modify behavior. User text will be passed as-is to avoid injection."
        ),
        new ValidationPattern(
            WarningLevel.Info,
            @"(role|persona|expert).*:",
            "Defines LLM role",
            "Good practice: Defining the LLM's role (e.g. 'You are an expert...') improves output quality"
        ),
        new ValidationPattern(
            WarningLevel.Caution,
            @"(?<!{text})$",  // Missing {text} at end
            "Prompt does not use {text} placeholder",
            "Prompt doesn't reference {text} — no selected text will be processed. Did you forget {text}?"
        ),
    };

    private record ValidationPattern(WarningLevel Level, string Regex, string Pattern, string Description, string? Suggestion = null);

    /// <summary>
    /// Validates a custom action and returns all findings (empty list = valid).
    /// </summary>
    public List<ValidationWarning> ValidateCustomAction(CustomAction action)
    {
        var warnings = new List<ValidationWarning>();

        if (string.IsNullOrWhiteSpace(action.Instructions))
        {
            warnings.Add(new ValidationWarning(
                WarningLevel.Warning,
                "Empty prompt",
                "Custom action has no prompt text",
                "Enter a prompt that uses the {text} placeholder"));
            return warnings;
        }

        string prompt = action.Instructions;

        // Check for missing {text} placeholder
        if (!prompt.Contains("{text}"))
        {
            warnings.Add(new ValidationWarning(
                WarningLevel.Warning,
                "Missing {text}",
                "Prompt does not reference the {text} placeholder",
                "Add {text} where the selected text should be inserted, e.g. 'Translate {text} to Spanish'"));
        }

        // Check for excessive length (suggests accidental paste)
        if (prompt.Length > 5000)
        {
            warnings.Add(new ValidationWarning(
                WarningLevel.Caution,
                "Very long prompt",
                "Prompt is very long (5000+ chars)",
                "Long prompts may be slow and expensive. Consider breaking into multiple actions."));
        }

        // Check for suspicious patterns
        foreach (var pattern in ValidationPatterns)
        {
            try
            {
                if (Regex.IsMatch(prompt, pattern.Regex, RegexOptions.IgnoreCase | RegexOptions.Multiline))
                {
                    warnings.Add(new ValidationWarning(
                        pattern.Level,
                        pattern.Pattern,
                        pattern.Description,
                        pattern.Suggestion ?? "Review the prompt to ensure it matches your intent"));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                DebugLogService.LogMessage($"CustomActionValidationService: Regex timeout on pattern '{pattern.Pattern}'");
            }
        }

        return warnings;
    }

    /// <summary>
    /// Validates all custom actions in settings and returns findings.
    /// Use when loading settings to audit for problems.
    /// </summary>
    public Dictionary<string, List<ValidationWarning>> ValidateAllActions(AppSettings settings)
    {
        var findings = new Dictionary<string, List<ValidationWarning>>();

        foreach (var action in settings.CustomActions)
        {
            var warnings = ValidateCustomAction(action);
            if (warnings.Count > 0)
                findings[action.Id.ToString()] = warnings;
        }

        return findings;
    }

    /// <summary>
    /// Formats validation warnings for display in Settings UI.
    /// Returns grouped warnings with severity indicators.
    /// </summary>
    public string FormatValidationSummary(List<ValidationWarning> warnings)
    {
        if (warnings.Count == 0)
            return "✓ Prompt looks good!";

        var byLevel = warnings.GroupBy(w => w.Level).OrderByDescending(g => g.Key).ToList();
        var summary = new System.Text.StringBuilder();

        foreach (var group in byLevel)
        {
            string icon = group.Key switch
            {
                WarningLevel.Warning => "⚠️",
                WarningLevel.Caution => "⚡",
                WarningLevel.Info => "ℹ️",
                _ => "•"
            };

            summary.AppendLine($"{icon} {group.Key}:");
            foreach (var warning in group)
            {
                summary.AppendLine($"   • {warning.Description}");
                if (!string.IsNullOrEmpty(warning.Suggestion))
                    summary.AppendLine($"     → {warning.Suggestion}");
            }
        }

        return summary.ToString().TrimEnd();
    }

    /// <summary>
    /// Determines if warnings are severe enough to recommend NOT saving.
    /// (User can override, but should be warned.)
    /// </summary>
    public bool HasSevereWarnings(List<ValidationWarning> warnings)
    {
        return warnings.Any(w => w.Level == WarningLevel.Warning);
    }

    /// <summary>
    /// Exports validation report as text for debugging/auditing.
    /// </summary>
    public string ExportReport(AppSettings settings)
    {
        var findings = ValidateAllActions(settings);
        if (findings.Count == 0)
            return "All custom actions pass validation. ✓";

        var report = new System.Text.StringBuilder();
        report.AppendLine("=== Custom Action Validation Report ===\n");

        foreach (var kvp in findings)
        {
            var action = settings.CustomActions.FirstOrDefault(a => a.Id.ToString() == kvp.Key);
            if (action != null)
            {
                report.AppendLine($"Action: {action.Name} ({kvp.Key})");
                report.AppendLine(FormatValidationSummary(kvp.Value));
                report.AppendLine();
            }
        }

        return report.ToString();
    }
}
