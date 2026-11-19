using System.Text;
using FileForge.IO;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Exports network audit entries or converts existing CSV logs into NDJSON files.
/// </summary>
/// <param name="formatter">Formatter of the NDJSON lines.</param>
/// <param name="csvHistoryParser">Reader of the CSV audit logs.</param>
/// <param name="logger">Receives one line for each NDJSON log holding lines that are not a JSON audit record; <see langword="null"/> ignores them.</param>
public sealed class AuditNdjsonExporter(AuditNdjsonFormatter formatter, IAuditCsvHistoryParser csvHistoryParser, Action<string>? logger = null)
{
    private const string UnreadableNdjsonLineReason = "that are not valid JSON";

    private readonly AuditNdjsonFormatter _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
    private readonly IAuditCsvHistoryParser _csvHistoryParser = csvHistoryParser ?? throw new ArgumentNullException(nameof(csvHistoryParser));
    private readonly AuditNdjsonParser _ndjsonParser = new();

    /// <summary>
    /// Initializes an exporter with default formatter and CSV parser components.
    /// </summary>
    /// <param name="logger">Receives one line for each NDJSON log holding lines that are not a JSON audit record; <see langword="null"/> ignores them.</param>
    public AuditNdjsonExporter(Action<string>? logger = null) : this(new AuditNdjsonFormatter(), new AuditCsvHistoryParser(), logger)
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
    /// <exception cref="OutputFileUnwritableException">Thrown when the output file or its folder cannot be created or written.</exception>
    public async Task<int> ExportEntriesAsync(
        IEnumerable<NetworkAuditEntry> entries,
        string outputNdjsonFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputNdjsonFilePath);

        var count = 0;
        await WriteOutputAsync(
            outputNdjsonFilePath,
            async (writer, _, token) => count = await WriteEntriesAsync(writer, entries, token).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
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
    /// <param name="delimiter">Separator kept when the first line of a CSV log does not tell its own (default: ','); each log is read with the separator of its first line.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The total number of entries written across all input files.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inputFilePaths"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="outputNdjsonFilePath"/> is null or white-space.</exception>
    /// <exception cref="OutputFileUnwritableException">Thrown when the output file or its folder cannot be created or written.</exception>
    public async Task<int> ExportAuditLogsToNdjsonAsync(
        IReadOnlyList<string> inputFilePaths,
        string outputNdjsonFilePath,
        char delimiter = ',',
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputFilePaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputNdjsonFilePath);

        var totalCount = 0;
        await WriteOutputAsync(
            outputNdjsonFilePath,
            async (writer, logRead, token) =>
            {
                foreach (var inputFilePath in inputFilePaths)
                {
                    token.ThrowIfCancellationRequested();
                    var entries = await logRead.RunAsync(() => AuditLogRecognizer.IsNdjsonPath(inputFilePath)
                        ? ReadNdjsonLogAsync(inputFilePath, token)
                        : ReadCsvLogAsync(inputFilePath, delimiter, token)).ConfigureAwait(false);
                    totalCount += await WriteEntriesAsync(writer, entries, token).ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);
        return totalCount;
    }

    // A line cut by a crash or edited by hand holds no flow: it is left out, and named so that the operator knows the
    // export holds fewer flows than the log has lines.
    private async Task<IReadOnlyList<NetworkAuditEntry>> ReadNdjsonLogAsync(string filePath, CancellationToken cancellationToken)
    {
        var read = await _ndjsonParser.ReadLogAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (read.UnreadableLineNumbers.Count > 0)
        {
            logger?.Invoke(new RejectedAuditLogLines(filePath, read.UnreadableLineNumbers, UnreadableNdjsonLineReason).Describe());
        }

        return read.Entries;
    }

    // Logs gathered from several servers may have been written with different CsvDelimiter settings. A missing log is
    // left to the parser, which reads it as empty, as before the separator was detected.
    private async Task<IReadOnlyList<NetworkAuditEntry>> ReadCsvLogAsync(string filePath, char configuredDelimiter, CancellationToken cancellationToken)
    {
        var delimiter = File.Exists(filePath)
            ? await AuditCsvDelimiterDetector.DetectAsync(filePath, configuredDelimiter, cancellationToken).ConfigureAwait(false)
            : configuredDelimiter;
        return await _csvHistoryParser.ParseHistoryAsync(filePath, maxRows: null, delimiter: delimiter, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    // The output replaces the previous one only once complete, so that a log that cannot be read, a cancellation or a
    // full disk does not leave it emptied or cut. A file system error names the output, except while a log is read:
    // that error keeps naming the log.
    private static async Task WriteOutputAsync(
        string filePath,
        Func<StreamWriter, LogReadScope, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        var logRead = new LogReadScope();
        try
        {
            // AtomicFileWriter would write the whole output beside the folder before failing to replace it, and leave
            // it there as <path>.pending.
            if (Directory.Exists(filePath))
            {
                throw new IOException("A folder has this name.");
            }

            await AtomicFileWriter.WriteAsync(
                filePath,
                async (stream, token) =>
                {
                    // The stream stays open for AtomicFileWriter, which flushes it to disk.
                    var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true);
                    await using (writer.ConfigureAwait(false))
                    {
                        await write(writer, logRead, token).ConfigureAwait(false);
                    }
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!logRead.IsReading && ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw OutputFileUnwritableException.Naming(filePath, ex);
        }
    }

    // Tells WriteOutputAsync that an error comes from a log being read, not from the output.
    private sealed class LogReadScope
    {
        public bool IsReading { get; private set; }

        public async Task<T> RunAsync<T>(Func<Task<T>> read)
        {
            IsReading = true;
            try
            {
                return await read().ConfigureAwait(false);
            }
            finally
            {
                IsReading = false;
            }
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
