using System.Globalization;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Formats group rule matrix rows into RFC 4180 compliant CSV lines.
/// </summary>
public sealed class FirewallGroupRuleCsvFormatter
{
    private static readonly string[] ColumnNames =
    [
        "GroupRuleId",
        "SourceGroup",
        "DestinationGroup",
        "Protocol",
        "DestinationPort",
        "RuleCount",
        "ObservationCount",
        "FirstSeenUtc",
        "LastSeenUtc",
        "Processes"
    ];

    /// <summary>
    /// Builds the CSV header line.
    /// </summary>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Delimited header row string.</returns>
    public string BuildHeader(char delimiter = ',') => string.Join(delimiter, ColumnNames);

    /// <summary>
    /// Formats a group rule into an escaped, delimited CSV row.
    /// </summary>
    /// <param name="row">Group rule to serialize.</param>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Escaped CSV row string.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="row"/> is null.</exception>
    public string FormatRow(FirewallGroupRuleItem row, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(row);

        var values = new[]
        {
            row.GroupRuleId,
            row.SourceGroup,
            row.DestinationGroup,
            row.Protocol,
            row.DestinationPort.ToString(CultureInfo.InvariantCulture),
            row.RuleCount.ToString(CultureInfo.InvariantCulture),
            row.ObservationCount.ToString(CultureInfo.InvariantCulture),
            row.FirstSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            row.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            string.Join(", ", row.Processes)
        };

        // Opened in a spreadsheet and never read back, with group names typed by the operator: see FirewallRuleCsvFormatter.
        return string.Join(delimiter, values.Select(v => PersistentDelimitedFileWriter.EscapeField(v, delimiter, neutralizeFormulas: true)));
    }
}
