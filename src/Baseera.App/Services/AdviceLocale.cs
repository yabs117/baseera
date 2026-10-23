namespace Baseera.App.Services;

internal sealed record AdviceLocale(string Language, bool RightToLeft, string Greeting, string CauseTitle,
    string StepsTitle, string ChecksTitle, string ConfidenceTitle)
{
    internal static string GreetingFor(string language, string generated)
    {
        int known = language.Split('-')[0].ToLowerInvariant() switch { "ar" => 0, "en" => 1, "fr" => 2, "es" => 3, _ => -1 };
        return known < 0 ? generated : I18n.Words["assistantGreeting"][known];
    }

    internal static AdviceLocale Interface => new(Settings.Current.Language, Settings.Current.Language == "ar",
        I18n.T("assistantGreeting"), I18n.T("errorLikelyCause"), I18n.T("errorSuggestedSteps"),
        I18n.T("errorFurtherChecks"), I18n.T("errorConfidence"));
}
