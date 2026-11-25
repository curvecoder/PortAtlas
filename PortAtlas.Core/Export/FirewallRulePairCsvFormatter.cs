using System.Globalization;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Formats pair matrix rows into RFC 4180 compliant CSV lines.
/// </summary>
public sealed class FirewallRulePairCsvFormatter : IMatrixColumnLayout<FirewallRulePairItem>
{
    private static readonly string[] Columns =
    [
        "PairId",
        "SourceIp",
        "SourceHost",
        "DestinationIp",
        "DestinationHost",
        "FlowCount",
        "Ports",
        "ObservationCount",
        "FirstSeenUtc",
        "LastSeenUtc",
        "SeenFrom"
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> ColumnNames => Columns;

    /// <inheritdoc />
    public IReadOnlyList<string> FieldsOf(FirewallRulePairItem pair)
    {
        ArgumentNullException.ThrowIfNull(pair);

        return
        [
            pair.PairId,
            pair.SourceIp,
            pair.SourceHost,
            pair.DestinationIp,
            pair.DestinationHost,
            pair.FlowCount.ToString(CultureInfo.InvariantCulture),
            string.Join(", ", pair.Ports),
            pair.ObservationCount.ToString(CultureInfo.InvariantCulture),
            pair.FirstSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            pair.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            string.Join(", ", pair.SeenFrom)
        ];
    }

    /// <summary>
    /// Builds the CSV header line.
    /// </summary>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Delimited header row string.</returns>
    public string BuildHeader(char delimiter = ',') => string.Join(delimiter, Columns);

    /// <summary>
    /// Formats a pair into an escaped, delimited CSV row.
    /// </summary>
    /// <param name="pair">Pair to serialize.</param>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Escaped CSV row string.</returns>
    public string FormatRow(FirewallRulePairItem pair, char delimiter = ',')
    {
        var values = FieldsOf(pair);

        // Opened in a spreadsheet and never read back, with host names from DNS or RDAP: see FirewallRuleCsvFormatter.
        return string.Join(delimiter, values.Select(v => PersistentDelimitedFileWriter.EscapeField(v, delimiter, neutralizeFormulas: true)));
    }
}
