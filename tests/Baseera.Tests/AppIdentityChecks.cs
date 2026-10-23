using Baseera.Core;

internal static class AppIdentityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var chrome = new InstalledAppEntry("Google Chrome", "130.0", "Google", @"C:\Apps\chrome.exe,0", "");
        var firefox = new InstalledAppEntry("Mozilla Firefox (x64 en-US)", "130.1", "Mozilla", @"C:\Apps\firefox.exe,0", "");
        var helper = new InstalledAppEntry("Mozilla Maintenance Service", "130.1", "Mozilla", @"C:\Apps\maint.exe,0", "");
        check(InstalledAppIdentity.Match("Google.Chrome", "130.0", [chrome]) == chrome, "App identity matches exact full package name");
        check(InstalledAppIdentity.Match("Mozilla.Firefox", "130.1", [helper, firefox]) == firefox, "App name matching removes architecture without selecting maintenance helper");
        var code = new InstalledAppEntry("Microsoft Visual Studio Code (User)", "1.0", "Microsoft", "", "", "Microsoft.VisualStudioCode");
        check(InstalledAppIdentity.Match("Microsoft.VisualStudioCode", "1.0", [code]) == code, "Explicit WinGet identity takes precedence");
        var ambiguous = chrome with { DisplayIcon = @"C:\Other\chrome.exe" };
        check(InstalledAppIdentity.Match("Google.Chrome", "130.0", [chrome, ambiguous]) is null, "Ambiguous app icons are not guessed");
        check(InstalledAppIdentity.Match("Google.Chrome", "130.0", [chrome, chrome]) == chrome, "Duplicate registry views collapse safely");
        check(InstalledAppIdentity.Match("Google.Chrome", "130.0", [chrome with { Version = "129.0" }, chrome]) == chrome, "Installed version disambiguates entries");
        var node = new InstalledAppEntry("Node.js", "22.0", "OpenJS", "", "");
        check(InstalledAppIdentity.Match("OpenJS.NodeJS.LTS", "22.0", [node]) == node, "LTS channel matches the installed Node.js display name");
        check(InstalledAppIdentity.Match("Google.ChromeBeta", "130.0", [chrome]) is null, "Different app editions are not matched by substring");
        if (OperatingSystem.IsWindows())
        {
            var icon = InstalledAppIdentity.ParseIconLocation("\"C:\\Program Files\\Example, Inc\\app.dll\",-42");
            check(icon?.Path == @"C:\Program Files\Example, Inc\app.dll" && icon?.Index == -42, "Icon path preserves spaces, commas and signed resource index");
            check(InstalledAppIdentity.ParseIconLocation(@"\\server\share\app.exe,0") is null && InstalledAppIdentity.ParseIconLocation("https://example.com/icon.png") is null && InstalledAppIdentity.ParseIconLocation("app.exe") is null, "Icon resolver refuses network and relative paths");
            check(InstalledAppIdentity.ParseIconLocation(@"C:\Apps\script.cmd") is null && InstalledAppIdentity.ParseIconLocation("\"C:\\Apps\\app.exe\" --run") is null, "Icon location is never treated as an executable command");
        }
    }
}
