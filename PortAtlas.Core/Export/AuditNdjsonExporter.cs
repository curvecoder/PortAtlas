using System.Text;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Exports network audit entries or converts existing CSV logs into NDJSON files.
/// </summary>
public sealed class AuditNdjsonExporter(AuditNdjsonFormatter formatter, IAuditCsvHistoryParser csvHistoryParser)
{
    private readonly AuditNdjsonFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly IAuditCsvHistoryParser _csvHistoryParser = csvHistoryParser ?? throw new ArgumentNullException(nameof(csvHistoryParser));
    private readonly AuditNdjsonParser _ndjsonParser = new();

    /// <summary>
    /// Initializes an exporter with default formatter and CSV parser components.
    /// </summary>
    public AuditNdjsonExporter() : this(new AuditNdjsonFormatter(), new AuditCsvHistoryParser())
    {
    }

    /// <summary>
    /// Exports a collection of network audit entries to an NDJSON file.
    /// </summary>
    /// <param name="entries">Network audit entries to export.</param>
    /// <param name="outputNdjsonFilePath">Target NDJSON file path.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The total number of entries written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="outputNdjsonFilePath"/> is null or white-space.</exception>
    public async Task<int> ExportEntriesAsync(
        IEnumerable<NetworkAuditEntry> entries,
        string outputNdjsonFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputNdjsonFilePath);

        EnsureDirectoryExists(outputNdjsonFilePath);

        await using var fileStream = new FileStream(outputNdjsonFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using var writer = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var count = await WriteEntriesAsync(writer, entries, cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    /// <summary>
    /// Reads an existing CSV audit file and converts all valid entries to an NDJSON file.
    /// </summary>
    /// <param name="inputCsvFilePath">Source CSV file path.</param>
    /// <param name="outputNdjsonFilePath">Target NDJSON file path.</param>
    /// <param name="delimiter">CSV delimiter character (default: ',').</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The total number of entries converted and written.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="inputCsvFilePath"/> or <paramref name="outputNdjsonFilePath"/> is null or white-space.</exception>
    public Task<int> ExportCsvToNdjsonAsync(
        string inputCsvFilePath,
        string outputNdjsonFilePath,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputCsvFilePath);
        return ExportAuditLogsToNdjsonAsync([inputCsvFilePath], outputNdjsonFilePath, delimiter, cancellationToken);
    }

    /// <summary>
    /// Reads audit logs one after the other and writes every entry they hold into a single NDJSON file.
    /// </summary>
    /// <remarks>
    /// A log whose path ends in <c>.ndjson</c> or <c>.jsonl</c> is read as NDJSON, any other as CSV. Each entry read
    /// gives one line: a flow held by two logs is written twice.
    /// </remarks>
    /// <param name="inputFilePaths">Audit log paths, read in this order.</param>
    /// <param name="outputNdjsonFilePath">Target NDJSON file path.</param>
    /// <param name="delimiter">Field separator of the CSV logs (default: ',').</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The total number of entries written across all input files.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputFilePaths"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="outputNdjsonFilePath"/> is null or white-space.</exception>
    public async Task<int> ExportAuditLogsToNdjsonAsync(
        IReadOnlyList<string> inputFilePaths,
        string outputNdjsonFilePath,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputFilePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputNdjsonFilePath);

        EnsureDirectoryExists(outputNdjsonFilePath);

        await using var fileStream = new FileStream(outputNdjsonFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true);
        await using var writer = new StreamWriter(fileStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var totalCount = 0;
        foreach (var inputFilePath in inputFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = AuditLogRecognizer.IsNdjsonPath(inputFilePath)
                ? await _ndjsonParser.ParseHistoryAsync(inputFilePath, cancellationToken: cancellationToken).ConfigureAwait(false)
                : await _csvHistoryParser.ParseHistoryAsync(inputFilePath, maxRows: null, delimiter: delimiter, cancellationToken: cancellationToken).ConfigureAwait(false);

            totalCount += await WriteEntriesAsync(writer, entries, cancellationToken).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        return totalCount;
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private async Task<int> WriteEntriesAsync(
        StreamWriter writer,
        IEnumerable<NetworkAuditEntry> entries,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = _formatter.FormatEntry(entry);
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
            count++;
        }

        return count;
    }
}
