using System.Globalization;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Formats consolidated firewall rule items into RFC 4180 compliant CSV lines.
/// </summary>
public sealed class FirewallRuleCsvFormatter
{
    private static readonly string[] ColumnNames =
    [
        "RuleId",
        "Direction",
        "SourceIp",
        "SourceHost",
        "DestinationIp",
        "DestinationHost",
        "DestinationZone",
        "Protocol",
        "DestinationPort",
        "ServiceName",
        "ProcessName",
        "ExecutablePath",
        "UserName",
        "ObservationCount",
        "FirstSeenUtc",
        "LastSeenUtc"
    ];

    /// <summary>
    /// Builds the CSV header line.
    /// </summary>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Delimited header row string.</returns>
    public string BuildHeader(char delimiter = ',')
    {
        return string.Join(delimiter, ColumnNames);
    }

    /// <summary>
    /// Formats a single firewall rule item into an escaped, delimited CSV row.
    /// </summary>
    /// <param name="rule">Rule item to serialize.</param>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Escaped CSV row string.</returns>
    public string FormatRow(FirewallRuleItem rule, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(rule);

        var values = new[]
        {
            rule.RuleId,
            rule.Direction,
            rule.SourceIp,
            rule.SourceHost,
            rule.DestinationIp,
            rule.DestinationHost,
            rule.DestinationZone,
            rule.Protocol,
            rule.DestinationPort.ToString(CultureInfo.InvariantCulture),
            rule.ServiceName,
            rule.ProcessName,
            rule.ExecutablePath,
            rule.UserName,
            rule.ObservationCount.ToString(CultureInfo.InvariantCulture),
            rule.FirstSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            rule.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture)
        };

        // The rule matrix is meant to be opened in a spreadsheet and is never read back by PortAtlas, while a process
        // name, a path or a host name can be chosen by a third party: a value that would start a formula is written
        // with a leading apostrophe. The audit log is not neutralized, since the apostrophe would change what is read.
        var escapedValues = values.Select(v => PersistentDelimitedFileWriter.EscapeField(v, delimiter, neutralizeFormulas: true));
        return string.Join(delimiter, escapedValues);
    }
}
