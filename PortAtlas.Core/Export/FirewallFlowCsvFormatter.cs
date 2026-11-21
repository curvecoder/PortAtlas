using System.Globalization;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Formats flow matrix rows into RFC 4180 compliant CSV lines.
/// </summary>
public sealed class FirewallFlowCsvFormatter
{
    private static readonly string[] ColumnNames =
    [
        "FlowId",
        "SourceIp",
        "SourceHost",
        "DestinationIp",
        "DestinationHost",
        "DestinationZone",
        "Protocol",
        "DestinationPort",
        "ServiceName",
        "SourceProcesses",
        "DestinationProcesses",
        "SeenFrom",
        "SourceObservationCount",
        "DestinationObservationCount",
        "FirstSeenUtc",
        "LastSeenUtc"
    ];

    /// <summary>
    /// Builds the CSV header line.
    /// </summary>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Delimited header row string.</returns>
    public string BuildHeader(char delimiter = ',') => string.Join(delimiter, ColumnNames);

    /// <summary>
    /// Formats a flow into an escaped, delimited CSV row.
    /// </summary>
    /// <param name="flow">Flow to serialize.</param>
    /// <param name="delimiter">Field delimiter character.</param>
    /// <returns>Escaped CSV row string.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="flow"/> is null.</exception>
    public string FormatRow(FirewallFlowItem flow, char delimiter = ',')
    {
        ArgumentNullException.ThrowIfNull(flow);

        var values = new[]
        {
            flow.FlowId,
            flow.SourceIp,
            flow.SourceHost,
            flow.DestinationIp,
            flow.DestinationHost,
            flow.DestinationZone,
            flow.Protocol,
            flow.DestinationPort.ToString(CultureInfo.InvariantCulture),
            flow.ServiceName,
            string.Join(", ", flow.SourceProcesses),
            string.Join(", ", flow.DestinationProcesses),
            flow.SeenFrom.ToString(),
            flow.SourceObservationCount.ToString(CultureInfo.InvariantCulture),
            flow.DestinationObservationCount.ToString(CultureInfo.InvariantCulture),
            flow.FirstSeenUtc.ToString("o", CultureInfo.InvariantCulture),
            flow.LastSeenUtc.ToString("o", CultureInfo.InvariantCulture)
        };

        // Opened in a spreadsheet and never read back, with host and process names chosen by third parties: see
        // FirewallRuleCsvFormatter.
        return string.Join(delimiter, values.Select(v => PersistentDelimitedFileWriter.EscapeField(v, delimiter, neutralizeFormulas: true)));
    }
}
