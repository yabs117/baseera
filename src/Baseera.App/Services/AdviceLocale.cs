namespace Baseera.App.Services;

internal sealed record AdviceLocale(string Language, bool RightToLeft, string Greeting, string CauseTitle,
    string StepsTitle, string ChecksTitle, string ConfidenceTitle)
{
    internal static string GreetingFor(string language, string generated)
    {
        return InterfaceLocales.ResponseLocale(language) is { } known ? I18n.Translate("assistantGreeting", known) : generated;
    }

    internal static AdviceLocale Interface => new(Settings.Current.Language, Settings.Current.Language == "ar",
        I18n.T("assistantGreeting"), I18n.T("errorLikelyCause"), I18n.T("errorSuggestedSteps"),
        I18n.T("errorFurtherChecks"), I18n.T("errorConfidence"));
}
