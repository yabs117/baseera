using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Baseera.Core;
using Microsoft.Win32;
using UglyToad.PdfPig;

namespace Baseera.App.Services;

internal static class LocalProbes
{
    internal static async Task<int> Dispatch(string kind)
    {
        try
        {
            var input = Console.In; var request = new StringBuilder();
            while (request.Length <= 16384) { int c = input.Read(); if (c is -1 or 10) break; if (c != 13) request.Append((char)c); }
            if (request.Length > 16384) return 3;
            var data = JsonSerializer.Deserialize<ProbeInput>(request.ToString()) ?? throw new InvalidDataException();
            if (!Guid.TryParseExact(data.Nonce, "N", out _) || data.Days is < 1 or > 30) return 3;
            object result = kind switch
            {
                "security" => Security(), "drivers" => Drivers(), "usb" => Usb(), "timeline" => Timeline(data.Days),
                "extract" => Extract(data.Path, data.Root), "signature" => await FileTrust.Inspect(data.Path),
                "fixture" => new InvestigationReport("doctor", DateTimeOffset.UtcNow, [new("hubFixture", "fixture-only")], []),
                _ => throw new InvalidDataException("Unsupported probe.")
            };
            string json = JsonSerializer.Serialize(result); if (json.Length > 1048576) return 4;
            await using var output = Console.OpenStandardOutput(); byte[] bytes = Encoding.UTF8.GetBytes(json); await output.WriteAsync(bytes); return 0;
        }
        catch { return 3; } // Raw paths, provider errors and private diagnostic values never go to stderr.
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value); }
    private static List<Dictionary<string, string>> Wmi(string space, string query, string[] fields, int maximum = 100, List<string>? limits = null)
    {
        object? locator = null, service = null, set = null; var result = new List<Dictionary<string, string>>(); var clock = Stopwatch.StartNew();
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            service = ((dynamic)locator!).ConnectServer(".", space); set = ((dynamic)service).ExecQuery(query, "WQL", 48);
            foreach (object item in (IEnumerable)set)
            {
                try
                {
                    var row = new Dictionary<string, string>();
                    foreach (string field in fields)
                    {
                        object? properties = null, property = null;
                        try { properties = ((dynamic)item).Properties_; property = ((dynamic)properties).Item(field); row[field] = DeviceHealth.Label(Convert.ToString(((dynamic)property).Value, CultureInfo.InvariantCulture) ?? ""); }
                        catch { row[field] = ""; } finally { Release(property); Release(properties); }
                    }
                    result.Add(row);
                    if (result.Count >= maximum || clock.Elapsed.TotalSeconds > 6) { limits?.Add("hubPartial"); break; }
                }
                finally { Release(item); }
            }
        }
        finally { Release(set); Release(service); Release(locator); }
        return result;
    }
    private static InvestigationReport Drivers()
    {
        var facts = new List<InvestigationFact>(); var limits = new List<string> { "hubDriverLimits" };
        try
        {
            var rows = Wmi(@"root\cimv2", "SELECT Name,ConfigManagerErrorCode,Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode<>0", ["Name", "ConfigManagerErrorCode", "Status"], 64, limits);
            foreach (var r in rows) facts.Add(new("hubDriverProblem", r["Name"], "PnP " + r["ConfigManagerErrorCode"] + " · " + r["Status"]));
            if (rows.Count == 0) facts.Add(new("hubDriverProblem", "state:hubNoReportedErrors"));
            if (rows.Count == 64) limits.Add("hubPartial");
            foreach (var r in Wmi(@"root\cimv2", "SELECT DeviceName,DriverVersion,IsSigned FROM Win32_PnPSignedDriver WHERE DeviceClass='DISPLAY' OR DeviceClass='NET'", ["DeviceName", "DriverVersion", "IsSigned"], 30, limits))
                facts.Add(new("hubDriverVersion", r["DeviceName"], r["DriverVersion"] + " · signed inventory=" + r["IsSigned"]));
        }
        catch { limits.Add("hubUnavailable"); }
        return new("doctor", DateTimeOffset.UtcNow, facts, limits.Distinct().ToArray());
    }
    private static InvestigationReport Security()
    {
        var facts = new List<InvestigationFact>(); var limits = new List<string> { "hubSecurityLimits" };
        void Query(string space, string query, string[] fields, Action<Dictionary<string, string>> receive)
        { try { var rows = Wmi(space, query, fields, 32, limits); if (rows.Count == 0) limits.Add("hubUnavailable"); foreach (var row in rows) receive(row); } catch { limits.Add("hubUnavailable"); } }
        string State(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase) ? "state:hubOn" : value.Equals("false", StringComparison.OrdinalIgnoreCase) ? "state:hubOff" : "state:hubUnavailable";
        Query(@"root\Microsoft\Windows\Defender", "SELECT AntivirusEnabled,RealTimeProtectionEnabled,AntivirusSignatureLastUpdated FROM MSFT_MpComputerStatus", ["AntivirusEnabled", "RealTimeProtectionEnabled", "AntivirusSignatureLastUpdated"], r =>
        { facts.Add(new("hubDefender", State(r["AntivirusEnabled"]))); facts.Add(new("hubRealtime", State(r["RealTimeProtectionEnabled"]))); facts.Add(new("hubSignatures", r["AntivirusSignatureLastUpdated"])); });
        Query(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct", ["displayName"], r => facts.Add(new("hubRegisteredAntivirus", r["displayName"])));
        object? firewall = null;
        try
        {
            firewall = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!); int active = ((dynamic)firewall!).CurrentProfileTypes;
            foreach (var profile in new[] { (1, "hubDomainFirewall"), (2, "hubPrivateFirewall"), (4, "hubPublicFirewall") })
                facts.Add(new(profile.Item2, ((dynamic)firewall!).FirewallEnabled[profile.Item1] ? "state:hubOn" : "state:hubOff", (active & profile.Item1) != 0 ? "hubActiveProfile" : "hubInactiveProfile"));
        }
        catch { facts.Add(new("hubFirewall", "state:hubUnavailable")); } finally { Release(firewall); }
        Query(@"root\cimv2\Security\MicrosoftVolumeEncryption", "SELECT DriveLetter,ProtectionStatus FROM Win32_EncryptableVolume", ["DriveLetter", "ProtectionStatus"], r => facts.Add(new("hubEncryption", r["DriveLetter"], r["ProtectionStatus"] == "1" ? "hubOn" : r["ProtectionStatus"] == "0" ? "hubOff" : "hubUnavailable")));
        object? updates = null, results = null;
        try { updates = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.AutoUpdate")!); results = ((dynamic)updates!).Results; facts.Add(new("hubLastUpdateSearch", Convert.ToString(((dynamic)results).LastSearchSuccessDate, CultureInfo.InvariantCulture) ?? "")); facts.Add(new("hubLastUpdateInstall", Convert.ToString(((dynamic)results).LastInstallationSuccessDate, CultureInfo.InvariantCulture) ?? "")); }
        catch { facts.Add(new("hubUpdates", "state:hubUnavailable")); } finally { Release(results); Release(updates); }
        return new("security", DateTimeOffset.UtcNow, facts, limits.Distinct().ToArray());
    }
    private static InvestigationReport Usb()
    {
        var facts = new List<InvestigationFact>(); var limits = new List<string> { "hubUsbLimits" };
        try
        {
            foreach (var r in Wmi(@"root\cimv2", "SELECT Name,PNPClass,ConfigManagerErrorCode,Status FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'", ["Name", "PNPClass", "ConfigManagerErrorCode", "Status"], 100, limits))
                facts.Add(new("hubUsbDevice", r["Name"], r["PNPClass"] + " · PnP " + r["ConfigManagerErrorCode"] + " · " + r["Status"]));
            foreach (var r in Wmi(@"root\cimv2", "SELECT Model,Size,Status FROM Win32_DiskDrive WHERE InterfaceType='USB'", ["Model", "Size", "Status"], 32, limits)) facts.Add(new("hubUsbDisk", r["Model"], r["Size"] + " bytes · " + r["Status"]));
        }
        catch { limits.Add("hubUnavailable"); }
        var removable = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable).ToArray();
        if (removable.Length > 32) limits.Add("hubPartial");
        foreach (var drive in removable.Take(32))
            try { facts.Add(new("hubRemovableVolume", drive.Name, drive.IsReady ? drive.AvailableFreeSpace + " / " + drive.TotalSize + " bytes" : "hubUnavailable")); } catch { limits.Add("hubUnreadable"); }
        if (facts.Count == 0) facts.Add(new("hubUsbDevice", "state:hubNoDevices"));
        return new("usb", DateTimeOffset.UtcNow, facts, limits.Distinct().ToArray());
    }
    private static TimelineSnapshot Timeline(int days)
    {
        var rows = new List<TimelineEntry>(); var notices = new List<string> { "hubTimelineLimits" }; var since = DateTimeOffset.UtcNow.AddDays(-days); var clock = Stopwatch.StartNew();
        foreach (var log in new[] { ("Application", "hubCrashEvent"), ("System", "hubSystemEvent"), ("Setup", "hubUpdateEvent"), ("Microsoft-Windows-WindowsUpdateClient/Operational", "hubUpdateEvent"), ("Microsoft-Windows-Kernel-PnP/Configuration", "hubDriverEvent") })
        {
            if (clock.Elapsed.TotalSeconds > 9) { notices.Add("hubPartial"); break; }
            try
            {
                string levels = log.Item1 is "Application" or "System" ? "(Level=1 or Level=2) and " : "";
                using var reader = new EventLogReader(new EventLogQuery(log.Item1, PathType.LogName, $"*[System[{levels}TimeCreated[timediff(@SystemTime) <= {days * 86400000L}]]]") { ReverseDirection = true });
                int count = 0; while (count++ < 80 && clock.Elapsed.TotalSeconds < 10)
                {
                    using var item = reader.ReadEvent(TimeSpan.FromMilliseconds(250)); if (item is null) break; string message;
                    try { message = item.FormatDescription() ?? ""; } catch { message = ""; }
                    message = DiagnosticPrivacy.Sanitize(message, Environment.UserName, Environment.MachineName); if (message.Length > 500) message = message[..500];
                    rows.Add(new(item.TimeCreated is { } time ? new DateTimeOffset(time.ToUniversalTime()) : null, log.Item2, DeviceHealth.Label(item.ProviderName ?? log.Item1) + " · ID " + item.Id, message));
                }
                if (count >= 80) notices.Add("hubPartial");
            }
            catch { notices.Add("hubUnreadable"); }
        }
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view); using var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", false);
                if (key is null) continue;
                foreach (string name in key.GetSubKeyNames().Take(200))
                {
                    using var app = key.OpenSubKey(name, false); if (app is null) continue;
                    string? title = app.GetValue("DisplayName") as string, date = app.GetValue("InstallDate") as string;
                    if (title is null || !DateTime.TryParseExact(date, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var installed) || installed.ToUniversalTime() < since.UtcDateTime) continue;
                    rows.Add(new(new DateTimeOffset(installed).ToUniversalTime(), "hubProgramDate", DeviceHealth.Label(title), "hubRegistryDateLimits"));
                }
            }
            catch { notices.Add("hubUnreadable"); }
        return new(DateTimeOffset.UtcNow, rows.Distinct().OrderByDescending(e => e.Time).Take(400).ToArray(), notices.Distinct().ToArray());
    }
    private static ExtractedText Extract(string path, string root)
    {
        LocalFileScope.RequireFile(root, path); var file = new FileInfo(path); if (file.Length > 20971520) return new("", "hubFileTooLarge"); string ext = file.Extension.ToLowerInvariant();
        if (ext is ".docx" or ".xlsx" or ".pptx") return new(LocalSearch.OfficeText(path), "hubTextLimited");
        if (ext == ".pdf")
        {
            using var document = PdfDocument.Open(path); var text = new StringBuilder();
            foreach (var page in document.GetPages().Take(20)) { text.Append(page.Text); text.Append(' '); if (text.Length >= 8000) break; }
            return new(text.ToString(0, Math.Min(8000, text.Length)), text.Length == 0 ? "hubNoOcr" : "hubTextLimited");
        }
        if (new[] { ".txt", ".md", ".csv", ".json", ".log", ".xml" }.Contains(ext))
        { using var reader = new StreamReader(path, Encoding.UTF8, true); var chars = new char[8000]; int length = reader.ReadBlock(chars, 0, chars.Length); return new(new string(chars, 0, length), "hubTextLimited"); }
        return new("", "hubNameOnly");
    }
}
