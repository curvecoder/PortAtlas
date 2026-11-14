using System.Globalization;
using System.Net;
using System.Text;
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

    // A resumed flow missing one of these columns would take default values (TCP, outbound, port 0, current time)
    // and match no observed flow, so it would be logged again on every restart. The local address is left out:
    // it only enters the key of a flow of unknown direction, which no socket scanner reports.
    private static readonly IReadOnlyList<string> ResumeRequiredColumns =
    [
        nameof(AuditCsvColumn.FirstSeenUtc),
        nameof(AuditCsvColumn.Protocol),
        nameof(AuditCsvColumn.Direction),
        nameof(AuditCsvColumn.LocalPort),
        nameof(AuditCsvColumn.RemoteIp),
        nameof(AuditCsvColumn.RemotePort),
        nameof(AuditCsvColumn.ExecutablePath),
        nameof(AuditCsvColumn.TargetExecutable)
    ];

    private const string ResumeWorkaround =
        "Write to a new log with --output, or turn the reload off with --reload-history 0.";

    /// <summary>
    /// Tells whether a line is the header of an audit log, that is the line this parser reads the columns by name from.
    /// </summary>
    /// <param name="line">First line of a CSV file.</param>
    /// <param name="delimiter">Field separator of the file.</param>
    /// <returns>True when the line names one of <see cref="HeaderMarkerColumns"/>.</returns>
    internal static bool IsHeaderLine(string line, char delimiter)
        => HeaderMarkerColumns.Any(BuildColumnIndexMap(SplitCsvLine(line, delimiter)).ContainsKey);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NetworkAuditEntry>> ParseHistoryAsync(
        string csvFilePath,
        int? maxRows = null,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        var table = await ReadTableAsync(csvFilePath, maxRows, delimiter, cancellationToken).ConfigureAwait(false);

        var results = new List<NetworkAuditEntry>();
        foreach (var row in table.EnumerateRows())
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
        var table = await ReadTableAsync(csvFilePath, maxRows, delimiter, cancellationToken).ConfigureAwait(false);

        var missingColumns = ResumeRequiredColumns.Where(column => !table.ColumnMap.ContainsKey(column)).ToList();
        if (missingColumns.Count > 0)
        {
            throw new InvalidDataException(
                $"The audit log '{csvFilePath}' cannot be resumed: it has no {string.Join(", ", missingColumns)} column, " +
                $"so the flows it holds would match none of the observed ones. {ResumeWorkaround}");
        }

        var results = new List<NetworkAuditEntry>();
        foreach (var row in table.EnumerateRows())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var firstSeen = GetField(row.Fields, table.ColumnMap, nameof(AuditCsvColumn.FirstSeenUtc));
            if (!TryParseTimestamp(firstSeen, out _))
            {
                throw new InvalidDataException(
                    $"The audit log '{csvFilePath}' cannot be resumed: line {row.LineNumber} has an unreadable " +
                    $"FirstSeenUtc value '{firstSeen}'. {ResumeWorkaround}");
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

        var lines = await ReadAllLinesAsync(csvFilePath, cancellationToken).ConfigureAwait(false);
        if (lines.Count == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return CsvTable.Empty;
        }

        var columnMap = BuildColumnIndexMap(SplitCsvLine(lines[0], delimiter));
        var isHeaderPresent = HeaderMarkerColumns.Any(columnMap.ContainsKey);
        if (!isHeaderPresent)
        {
            columnMap = StandardPositionalColumnMap;
        }

        var dataStartIndex = isHeaderPresent ? 1 : 0;
        var dataLineCount = lines.Count - dataStartIndex;
        var firstRowIndex = maxRows > 0 && maxRows.Value < dataLineCount
            ? lines.Count - maxRows.Value
            : dataStartIndex;

        return new CsvTable(lines, columnMap, firstRowIndex, delimiter);
    }

    private static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);

    private static async Task<List<string>> ReadAllLinesAsync(string path, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lines.Add(line);
        }

        return lines;
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

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        result.Add(current.ToString());
        return result;
    }

    private sealed record CsvTable(IReadOnlyList<string> Lines, Dictionary<string, int> ColumnMap, int FirstRowIndex, char Delimiter)
    {
        public static CsvTable Empty { get; } = new([], StandardPositionalColumnMap, 0, ',');

        public IEnumerable<CsvRow> EnumerateRows()
        {
            for (var i = FirstRowIndex; i < Lines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(Lines[i]))
                {
                    yield return new CsvRow(i + 1, SplitCsvLine(Lines[i], Delimiter));
                }
            }
        }
    }

    private readonly record struct CsvRow(int LineNumber, List<string> Fields);
}
