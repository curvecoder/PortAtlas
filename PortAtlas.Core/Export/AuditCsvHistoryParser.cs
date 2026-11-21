using System.Globalization;
using System.Net;
using System.Text;
using FileForge.IO;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Default implementation of <see cref="IAuditCsvHistoryParser"/> that parses historical RFC 4180 audit CSV records.
/// </summary>
public sealed class AuditCsvHistoryParser : IAuditCsvHistoryParser
{
    private static readonly Dictionary<string, int> StandardPositionalColumnMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FirstSeenUtc"] = 0,
        ["LastSeenUtc"] = 1,
        ["ObservationCount"] = 2,
        ["ServerName"] = 3,
        ["ServerOsVersion"] = 4,
        ["ServerPrimaryIp"] = 5,
        ["Protocol"] = 6,
        ["Direction"] = 7,
        ["TcpState"] = 8,
        ["LocalIp"] = 9,
        ["LocalPort"] = 10,
        ["RemoteIp"] = 11,
        ["RemotePort"] = 12,
        ["RemoteHostName"] = 13,
        ["ProcessId"] = 14,
        ["ProcessName"] = 15,
        ["ExecutablePath"] = 16,
        ["CommandLine"] = 17,
        ["TargetExecutable"] = 18,
        ["UserName"] = 19
    };

    /// <summary>
    /// Columns whose name tells the header of an audit log from a data row, and from the header of another file.
    /// </summary>
    internal static readonly IReadOnlyList<string> HeaderMarkerColumns = [nameof(AuditCsvColumn.ServerName), nameof(AuditCsvColumn.LocalPort)];

    private static readonly HashSet<string> AuditColumnNames = new(Enum.GetNames<AuditCsvColumn>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Columns an audit log must hold to be resumed: the parts of the flow key and the first-seen date.
    /// </summary>
    /// <remarks>
    /// A resumed flow missing one of these columns would take default values (TCP, outbound, port 0, current time)
    /// and match no observed flow. The local address is left out: it only enters the key of a flow of unknown
    /// direction, which no socket scanner reports. The process name identifies the owner of a flow whose executable
    /// path is empty (SocketForge 2.0).
    /// </remarks>
    public static IReadOnlyList<string> ResumeRequiredColumns { get; } =
    [
        nameof(AuditCsvColumn.FirstSeenUtc),
        nameof(AuditCsvColumn.Protocol),
        nameof(AuditCsvColumn.Direction),
        nameof(AuditCsvColumn.LocalPort),
        nameof(AuditCsvColumn.RemoteIp),
        nameof(AuditCsvColumn.RemotePort),
        nameof(AuditCsvColumn.ProcessName),
        nameof(AuditCsvColumn.ExecutablePath),
        nameof(AuditCsvColumn.TargetExecutable)
    ];

    private const string ResumeWorkaround = "Write to a new log with --output.";

    private const string UnreadableLineWorkaround = "Correct or remove the line, or write to a new log with --output.";

    // A value read into a default (TCP, port 0, the current time) would be written back by the next rewrite of the
    // log, which would lose what the line held: such a line stops the resume instead. A blank optional value is
    // the one an earlier version wrote or a reduced column set left out, and keeps its default.
    private static readonly IReadOnlyList<ResumeValueRule> ResumeValueRules =
    [
        new(nameof(AuditCsvColumn.FirstSeenUtc), IsRequired: true, value => TryParseTimestamp(value, out _)),
        new(nameof(AuditCsvColumn.LastSeenUtc), IsRequired: false, value => TryParseTimestamp(value, out _)),
        new(nameof(AuditCsvColumn.ObservationCount), IsRequired: false, IsObservationCount),
        new(nameof(AuditCsvColumn.Protocol), IsRequired: true, IsDefinedName<SocketTransportProtocol>),
        new(nameof(AuditCsvColumn.Direction), IsRequired: true, IsDefinedName<SocketFlowDirection>),
        new(nameof(AuditCsvColumn.LocalIp), IsRequired: false, value => IPAddress.TryParse(value, out _)),
        new(nameof(AuditCsvColumn.LocalPort), IsRequired: true, IsPort),
        new(nameof(AuditCsvColumn.RemoteIp), IsRequired: true, value => IPAddress.TryParse(value, out _)),
        new(nameof(AuditCsvColumn.RemotePort), IsRequired: true, IsPort)
    ];

    /// <summary>
    /// Tells whether a line is the header of an audit log, that is the line this parser reads the columns by name from.
    /// </summary>
    /// <param name="line">First line of a CSV file.</param>
    /// <param name="delimiter">Field separator of the file.</param>
    /// <returns>True when the line names one of <see cref="HeaderMarkerColumns"/>.</returns>
    internal static bool IsHeaderLine(string line, char delimiter)
        => HeaderMarkerColumns.Any(BuildColumnIndexMap(AuditCsvRecordReader.SplitHeader(line, delimiter)).ContainsKey);

    /// <summary>
    /// Tells whether the first record of an audit log is its header rather than a flow written without header.
    /// </summary>
    /// <remarks>
    /// A record that names any audit column is a header, even the header of a reduced log that names neither flow key
    /// column: read by position, it would become a flow of its own, and its rows would be read through the wrong columns.
    /// </remarks>
    /// <param name="fields">Fields of the first record of a CSV file.</param>
    /// <returns>True when one field, trimmed, is the name of an <see cref="AuditCsvColumn"/>.</returns>
    internal static bool IsHeaderRecord(IReadOnlyList<string> fields)
        => fields.Any(field => AuditColumnNames.Contains(field.Trim()));

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkAuditEntry>> ParseHistoryAsync(
        string csvFilePath,
        int? maxRows = null,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        var table = await ReadTableAsync(csvFilePath, maxRows, delimiter, cancellationToken).ConfigureAwait(false);

        var results = new List<NetworkAuditEntry>();
        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ConvertFieldsToAuditEntry(row.Fields, table.ColumnMap) is { } entry)
            {
                results.Add(entry);
            }
        }

        return results;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkAuditEntry>> ParseLogForResumeAsync(
        string csvFilePath,
        int? maxRows = null,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        // The audit rewrites its log with the configured separator at startup: a log written before CsvDelimiter changed
        // is read with its own separator, so that the change does not stop the audit from resuming it.
        var logDelimiter = File.Exists(csvFilePath)
            ? await AuditCsvDelimiterDetector.DetectAsync(csvFilePath, delimiter, cancellationToken).ConfigureAwait(false)
            : delimiter;
        var table = await ReadTableAsync(csvFilePath, maxRows, logDelimiter, cancellationToken).ConfigureAwait(false);

        var missingColumns = ResumeRequiredColumns.Where(column => !table.ColumnMap.ContainsKey(column)).ToList();
        if (missingColumns.Count > 0)
        {
            throw new InvalidDataException(
                $"The audit log '{csvFilePath}' cannot be resumed: it has no {string.Join(", ", missingColumns)} column, " +
                $"so the flows it holds would match none of the observed ones. {ResumeWorkaround}");
        }

        var results = new List<NetworkAuditEntry>();
        foreach (var row in table.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var rule in ResumeValueRules)
            {
                var value = GetField(row.Fields, table.ColumnMap, rule.Column);
                var isReadable = string.IsNullOrWhiteSpace(value) ? !rule.IsRequired : rule.IsReadable(value);
                if (!isReadable)
                {
                    throw new InvalidDataException(
                        $"The audit log '{csvFilePath}' cannot be resumed: line {row.LineNumber} has an unreadable " +
                        $"{rule.Column} value '{value}'. {UnreadableLineWorkaround}");
                }
            }

            if (ConvertFieldsToAuditEntry(row.Fields, table.ColumnMap) is { } entry)
            {
                results.Add(entry);
            }
        }

        return results;
    }

    private static async Task<CsvTable> ReadTableAsync(
        string csvFilePath,
        int? maxRows,
        char delimiter,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvFilePath);

        if (maxRows == 0 || !File.Exists(csvFilePath))
        {
            return CsvTable.Empty;
        }

        var records = await ReadAllRecordsAsync(csvFilePath, delimiter, cancellationToken).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return CsvTable.Empty;
        }

        var columnMap = BuildColumnIndexMap(records[0].Fields);
        var isHeaderPresent = IsHeaderRecord(records[0].Fields);
        if (!isHeaderPresent)
        {
            columnMap = StandardPositionalColumnMap;
        }

        var dataStartIndex = isHeaderPresent ? 1 : 0;
        var dataRecordCount = records.Count - dataStartIndex;
        var firstRowIndex = maxRows > 0 && maxRows.Value < dataRecordCount
            ? records.Count - maxRows.Value
            : dataStartIndex;

        return new CsvTable(records.Skip(firstRowIndex).ToList(), columnMap);
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);

    private static bool IsObservationCount(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0;

    private static bool IsPort(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port <= IPEndPoint.MaxPort;

    // Enum.TryParse also reads numbers and comma-separated combinations, which no log writes for these columns.
    private static bool IsDefinedName<TEnum>(string value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !char.IsAsciiDigit(value.Trim()[0]);

    private static async Task<List<DelimitedRecord>> ReadAllRecordsAsync(string path, char delimiter, CancellationToken cancellationToken)
    {
        var records = new List<DelimitedRecord>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        await foreach (var record in AuditCsvRecordReader.ReadRecordsAsync(reader, path, delimiter, cancellationToken).ConfigureAwait(false))
        {
            records.Add(record);
        }

        return records;
    }

    private static Dictionary<string, int> BuildColumnIndexMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var clean = headers[i].Trim();
            if (!string.IsNullOrEmpty(clean) && !map.ContainsKey(clean))
            {
                map[clean] = i;
            }
        }

        return map;
    }

    private static NetworkAuditEntry? ConvertFieldsToAuditEntry(
        IReadOnlyList<string> fields,
        Dictionary<string, int> columnMap)
    {
        var firstSeenStr = GetField(fields, columnMap, "FirstSeenUtc");
        var lastSeenStr = GetField(fields, columnMap, "LastSeenUtc");
        var obsCountStr = GetField(fields, columnMap, "ObservationCount");
        var serverName = GetField(fields, columnMap, "ServerName");
        var serverOs = GetField(fields, columnMap, "ServerOsVersion");
        var serverPrimaryIp = GetField(fields, columnMap, "ServerPrimaryIp");
        var protocolStr = GetField(fields, columnMap, "Protocol");
        var directionStr = GetField(fields, columnMap, "Direction");
        var tcpState = GetField(fields, columnMap, "TcpState");
        var localIpStr = GetField(fields, columnMap, "LocalIp");
        var localPortStr = GetField(fields, columnMap, "LocalPort");
        var remoteIpStr = GetField(fields, columnMap, "RemoteIp");
        var remotePortStr = GetField(fields, columnMap, "RemotePort");
        var remoteHostName = GetField(fields, columnMap, "RemoteHostName");
        var pidStr = GetField(fields, columnMap, "ProcessId");
        var processName = GetField(fields, columnMap, "ProcessName");
        var execPath = GetField(fields, columnMap, "ExecutablePath");
        var commandLine = GetField(fields, columnMap, "CommandLine");
        var targetExec = GetField(fields, columnMap, "TargetExecutable");
        var userName = GetField(fields, columnMap, "UserName");

        if (string.IsNullOrWhiteSpace(serverName) && string.IsNullOrWhiteSpace(localPortStr))
        {
            return null;
        }

        var firstSeen = TryParseTimestamp(firstSeenStr, out var parsedFirst)
            ? parsedFirst
            : DateTimeOffset.UtcNow;

        var lastSeen = TryParseTimestamp(lastSeenStr, out var parsedLast)
            ? parsedLast
            : firstSeen;

        _ = long.TryParse(obsCountStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var obsCount);
        if (obsCount <= 0)
        {
            obsCount = 1;
        }

        var protocol = Enum.TryParse<SocketTransportProtocol>(protocolStr, ignoreCase: true, out var parsedProtocol)
            ? parsedProtocol
            : SocketTransportProtocol.Tcp;

        var direction = Enum.TryParse<SocketFlowDirection>(directionStr, ignoreCase: true, out var parsedDirection)
            ? parsedDirection
            : SocketFlowDirection.Outbound;

        var localAddress = IPAddress.TryParse(localIpStr, out var parsedLocalIp)
            ? parsedLocalIp
            : IPAddress.Loopback;

        _ = int.TryParse(localPortStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var localPort);

        var remoteAddress = IPAddress.TryParse(remoteIpStr, out var parsedRemoteIp)
            ? parsedRemoteIp
            : IPAddress.Any;

        _ = int.TryParse(remotePortStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var remotePort);
        _ = int.TryParse(pidStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid);

        // A port outside 0-65535 cannot become an endpoint: the line is left out like any other unreadable line, not read into a default.
        if (localPort is < 0 or > IPEndPoint.MaxPort || remotePort is < 0 or > IPEndPoint.MaxPort)
        {
            return null;
        }

        var serverMetadata = new ServerHostMetadata(
            HostName: string.IsNullOrWhiteSpace(serverName) ? Environment.MachineName : serverName,
            OperatingSystemDescription: serverOs,
            PrimaryIpAddress: serverPrimaryIp);

        var processMetadata = new ProcessMetadata(
            ProcessId: pid,
            ProcessName: processName,
            ExecutablePath: execPath,
            CommandLine: commandLine,
            TargetExecutable: targetExec,
            UserName: userName);

        var socket = new ActiveSocketConnection(
            Protocol: protocol,
            Direction: direction,
            LocalEndpoint: new SocketEndpoint(localAddress, localPort),
            RemoteEndpoint: new SocketEndpoint(remoteAddress, remotePort),
            State: string.IsNullOrWhiteSpace(tcpState) ? "Established" : tcpState,
            Process: processMetadata);

        return new NetworkAuditEntry(
            FirstSeenUtc: firstSeen,
            LastSeenUtc: lastSeen,
            ObservationCount: obsCount,
            Server: serverMetadata,
            Socket: socket,
            RemoteHostName: remoteHostName);
    }

    private static string GetField(IReadOnlyList<string> fields, Dictionary<string, int> columnMap, string columnName)
    {
        if (columnMap.TryGetValue(columnName, out var index) && index >= 0 && index < fields.Count)
        {
            return fields[index];
        }

        return string.Empty;
    }

    private sealed record ResumeValueRule(string Column, bool IsRequired, Func<string, bool> IsReadable);

    private sealed record CsvTable(IReadOnlyList<DelimitedRecord> Rows, Dictionary<string, int> ColumnMap)
    {
        public static CsvTable Empty { get; } = new([], StandardPositionalColumnMap);
    }
}
