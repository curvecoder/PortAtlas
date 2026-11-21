using System.Globalization;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Formats host rule matrix rows into RFC 4180 compliant CSV lines.
/// </summary>
public sealed class FirewallHostRuleCsvFormatter
{
    private static readonly string[] ColumnNames =
    [
        "HostRuleId",
        "Host",
        "HostIp",
        "Audited",
        "Direction",
        "PeerIp",
        "PeerHost",
        "PeerZone",
        "Protocol",
        "Port",
        "ServiceName",
        "LocalProcesses",
        "Evidence",
        "ObservationCount",
        "FirstSeenUtc",
        "LastSeenUtc",
        "FlowId"
    ];

    /// <summary>
    /// Builds the CSV header line.
    /// </summary>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Delimited header row string.</returns>
    public string BuildHeader(char delimiter = ',') => string.Join(delimiter, ColumnNames);

    /// <summary>
    /// Formats a host rule into an escaped, delimited CSV row.
    /// </summary>
    /// <param name="rule">Host rule to serialize.</param>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Escaped CSV row string.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="rule"/> is null.</exception>
    public string FormatRow(FirewallHostRuleItem rule, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(rule);

        var values = new[]
        {
            rule.HostRuleId,
            rule.Host,
            rule.HostIp,
            rule.IsAudited ? "Yes" : "No",
            rule.Direction,
            rule.PeerIp,
            rule.PeerHost,
            rule.PeerZone,
            rule.Protocol,
            rule.Port.ToString(CultureInfo.InvariantCulture),
            rule.ServiceName,
            string.Join(", ", rule.LocalProcesses),
            rule.Evidence.ToString(),
            rule.ObservationCount.ToString(CultureInfo.InvariantCulture),
            rule.FirstSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            rule.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            rule.FlowId
        };

        // Opened in a spreadsheet and never read back, with host and process names chosen by third parties: see
        // FirewallRuleCsvFormatter.
        return string.Join(delimiter, values.Select(v => PersistentDelimitedFileWriter.EscapeField(v, delimiter, neutralizeFormulas: true)));
    }
}
