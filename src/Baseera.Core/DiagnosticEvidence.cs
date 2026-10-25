using System.Globalization;
using System.Text.RegularExpressions;

namespace Baseera.Core;

public sealed record DiagnosticEventGroup(string Channel, string Provider, int EventId, int Count, DateTimeOffset Latest);
public sealed record DiagnosticEvidence(int EventCount, IReadOnlyList<DiagnosticEventGroup> Groups,
    IReadOnlyList<string> ErrorCodes, int CoverageNotices)
{
    // A local index of the editable preview, not an authenticity check or diagnosis.
    public static DiagnosticEvidence Read(string preview)
    {
        preview = preview[..Math.Min(preview.Length, DiagnosticPrivacy.MaxCharacters)];
        var events = new List<(string Channel, string Provider, int Id, DateTimeOffset Time)>();
        foreach (string line in preview.Split('\n'))
        {
            var fields = line.Trim().Split(" | ");
            if (fields.Length != 5 || fields[1] is not ("Application" or "System") || fields[2].Length is 0 or > 200 ||
                !fields[3].StartsWith("ID ", StringComparison.Ordinal) || fields[4] is not ("Level 1" or "Level 2")) continue;
            if (int.TryParse(fields[3].AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id is >= 0 and <= 65535 &&
                DateTimeOffset.TryParse(fields[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time))
                events.Add((fields[1], fields[2], id, time.ToUniversalTime()));
        }
        var groups = events.GroupBy(e => (e.Channel, e.Provider, e.Id))
            .Select(g => new DiagnosticEventGroup(g.Key.Channel, g.Key.Provider, g.Key.Id, g.Count(), g.Max(e => e.Time)))
            .OrderByDescending(g => g.Count).ThenByDescending(g => g.Latest).Take(12).ToArray();
        var codes = Regex.Matches(preview, @"\b0x[0-9a-f]{8}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100)).Select(m => "0x" + m.Value[2..].ToUpperInvariant()).Distinct(StringComparer.Ordinal).Take(16).ToArray();
        int notices = preview.Split('\n').Count(line => line.Contains("[Source unavailable:", StringComparison.Ordinal) ||
            line.Contains("[Collection deadline", StringComparison.Ordinal) || line.Contains("[Data limit", StringComparison.Ordinal) ||
            line.Contains("[Sample limit", StringComparison.Ordinal) || line.Contains("[truncated]", StringComparison.Ordinal));
        return new(events.Count, Array.AsReadOnly(groups), Array.AsReadOnly(codes), notices);
    }
}
