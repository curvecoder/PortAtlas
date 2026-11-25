using System.Globalization;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Formats group rule matrix rows into RFC 4180 compliant CSV lines.
/// </summary>
public sealed class FirewallGroupRuleCsvFormatter : IMatrixColumnLayout<FirewallGroupRuleItem>
{
    private static readonly string[] Columns =
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

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames => Columns;

    /// <inheritdoc />
    public IReadOnlyList<string> FieldsOf(FirewallGroupRuleItem row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return
        [
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
        ];
    }

    /// <summary>
    /// Builds the CSV header line.
    /// </summary>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Delimited header row string.</returns>
    public string BuildHeader(char delimiter = ',') => string.Join(delimiter, Columns);

    /// <summary>
    /// Formats a group rule into an escaped, delimited CSV row.
    /// </summary>
    /// <param name="row">Group rule to serialize.</param>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Escaped CSV row string.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="row"/> is null.</exception>
    public string FormatRow(FirewallGroupRuleItem row, char delimiter = ',')
    {
        var values = FieldsOf(row);

        // Opened in a spreadsheet and never read back, with group names typed by the operator: see FirewallRuleCsvFormatter.
        return string.Join(delimiter, values.Select(v => PersistentDelimitedFileWriter.EscapeField(v, delimiter, neutralizeFormulas: true)));
    }
}
