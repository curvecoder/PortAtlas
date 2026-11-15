using System.Text;
using FileForge.IO;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Writes the whole audit log, one line per flow, from the flows held in memory.
/// </summary>
/// <remarks>
/// Each log is written beside the previous one and replaces it only once complete (<see cref="AtomicFileWriter"/>):
/// an interruption leaves the previous log, or its complete new version in <c>&lt;log&gt;.pending</c>.
/// </remarks>
/// <param name="csvPath">Path of the CSV log.</param>
/// <param name="csvFormatter">Formats the header and the lines of the CSV log.</param>
/// <param name="ndjsonPath">Path of the NDJSON log, or null when the audit writes none.</param>
/// <param name="ndjsonFormatter">Formats the lines of the NDJSON log; the NDJSON log is written only when it and <paramref name="ndjsonPath"/> are given.</param>
public sealed class AuditLogRewriter(
    string csvPath,
    AuditCsvFormatter csvFormatter,
    string? ndjsonPath = null,
    AuditNdjsonFormatter? ndjsonFormatter = null)
{
    private static readonly UTF8Encoding NdjsonEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Replaces the logs with the given flows.
    /// </summary>
    /// <param name="entries">Every flow of the audit.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>A task that ends when the logs hold the flows.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="entries"/> is null.</exception>
    /// <exception cref="IOException">
    /// When a log was not replaced; the message names the log, and a complete new version left in
    /// <c>&lt;log&gt;.pending</c> (on Windows, while another program holds the log open).
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">When the folder of a log cannot be written.</exception>
    public async Task RewriteAsync(IReadOnlyList<NetworkAuditEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        // Discovery order, as in the logs appended by earlier versions; the line itself breaks ties so that two
        // rewrites of the same flows give the same file.
        var ordered = entries
            .Select(entry => (Entry: entry, CsvLine: csvFormatter.FormatEntry(entry)))
            .OrderBy(flow => flow.Entry.FirstSeenUtc)
            .ThenBy(flow => flow.CsvLine, StringComparer.Ordinal)
            .ToList();

        // UTF-8 with a byte order mark for the CSV log, as earlier versions wrote it: spreadsheet programs need it to
        // show accented names. None for the NDJSON log, which JSON Lines readers may refuse with one.
        await WriteLinesAsync(csvPath, Encoding.UTF8, ordered.Select(flow => flow.CsvLine).Prepend(csvFormatter.BuildHeaderLine()), cancellationToken).ConfigureAwait(false);

        if (ndjsonPath is not null && ndjsonFormatter is not null)
        {
            await WriteLinesAsync(ndjsonPath, NdjsonEncoding, ordered.Select(flow => ndjsonFormatter.FormatEntry(flow.Entry)), cancellationToken).ConfigureAwait(false);
        }
    }

    private static Task WriteLinesAsync(string path, Encoding encoding, IEnumerable<string> lines, CancellationToken cancellationToken) =>
        AtomicFileWriter.WriteAsync(
            path,
            async (stream, token) =>
            {
                // The stream stays open for AtomicFileWriter, which flushes it to disk.
                var writer = new StreamWriter(stream, encoding, leaveOpen: true);
                await using (writer.ConfigureAwait(false))
                {
                    foreach (var line in lines)
                    {
                        await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
                    }
                }
            },
            cancellationToken);
}
