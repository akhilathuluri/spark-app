using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Spark.Models;

public class AppSettings
{
    public int SettingsVersion { get; set; } = 3;
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyKey { get; set; } = "Space";
    public string ActiveProvider { get; set; } = "OpenRouter";
    public OpenRouterSettings OpenRouter { get; set; } = new();
    public GroqSettings Groq { get; set; } = new();
    public List<CustomAction> CustomActions { get; set; } = new();
    public bool StartWithWindows { get; set; } = false;
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "en";
    public bool SafeReplacePreviewMode { get; set; } = false;
    public bool EnableDiagnosticsBundle { get; set; } = true;
}

public class OpenRouterSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class GroqSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
}

public class CustomAction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
    public string Icon { get; set; } = "✨";

    [JsonIgnore]
    public bool IsBuiltIn => false;
}

public class ActionItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;
    public string? Prompt { get; set; }
    public bool IsBuiltIn { get; set; } = true;
    public bool IsCustom => !IsBuiltIn;
}
