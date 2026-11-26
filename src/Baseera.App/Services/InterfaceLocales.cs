using System.Text.Json;

namespace Baseera.App.Services;

internal sealed record InterfaceLocale(string Code, string Name, string Target);

internal static class InterfaceLocales
{
    internal const string DefaultCode = "en";
    internal const string DefaultTarget = "English";
    internal static readonly InterfaceLocale[] All =
    [
        new("ar", "العربية", "Arabic"),
        new(DefaultCode, "English", DefaultTarget),
        new("fr", "Français", "French"),
        new("es", "Español", "Spanish"),
        new("zh-CN", "简体中文", "Simplified Chinese (mainland China)"),
        new("zh-TW", "繁體中文", "Traditional Chinese (Taiwan)"),
        new("vi", "Tiếng Việt", "Vietnamese")
    ];

    internal static readonly Lazy<Dictionary<string, string[]>> Additional = new(() =>
    {
        using var stream = typeof(InterfaceLocales).Assembly.GetManifestResourceStream("Baseera.AdditionalLocales.json")
            ?? throw new InvalidOperationException("Missing interface translations.");
        return JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream) ?? [];
    });

    internal static int Index(string language) => Array.FindIndex(All, locale => locale.Code.Equals(language, StringComparison.OrdinalIgnoreCase));
    internal static string TargetFor(string language) => All.FirstOrDefault(locale => locale.Code == language)?.Target ?? DefaultTarget;
    internal static InterfaceLocale FromTarget(string target) => All.FirstOrDefault(locale => locale.Target == target) ?? All[Index(DefaultCode)];

    internal static string? ResponseLocale(string tag)
    {
        string[] parts = tag.ToLowerInvariant().Split('-');
        if (parts[0] != "zh") return Index(parts[0]) >= 0 ? parts[0] : null;
        if (parts.Contains("hans")) return "zh-CN";
        if (parts.Contains("hant")) return "zh-TW";
        if (parts.Any(part => part is "hk" or "mo" or "tw")) return "zh-TW";
        if (parts.Any(part => part is "cn" or "sg")) return "zh-CN";
        return Settings.Current.Language is "zh-CN" or "zh-TW" ? Settings.Current.Language : null;
    }
}
