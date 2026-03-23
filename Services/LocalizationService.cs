using System.IO;
using System.Text.Json;

namespace Spark.Services;

public class LocalizationService
{
    private readonly Dictionary<string, string> _fallback;
    private readonly Dictionary<string, string> _current;

    public string Language { get; }

    public LocalizationService(string language)
    {
        Language = string.IsNullOrWhiteSpace(language) ? "en" : language.ToLowerInvariant();
        _fallback = LoadLocale("en");
        _current = Language == "en" ? _fallback : LoadLocale(Language);
    }

    public string T(string key, string? defaultValue = null)
    {
        if (_current.TryGetValue(key, out var value))
            return value;

        if (_fallback.TryGetValue(key, out value))
            return value;

        return defaultValue ?? key;
    }

    private static Dictionary<string, string> LoadLocale(string language)
    {
        try
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Locales", $"{language}.json");
            if (!File.Exists(path))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
