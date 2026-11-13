using System.Text;
using System.Text.Json;

namespace PortAtlas.Core.Export;

/// <summary>
/// Tells an audit log from the other files of a directory by its first line, the way the parsers read it.
/// </summary>
internal static class AuditLogRecognizer
{
    private static readonly string[] NdjsonExtensions = [".ndjson", ".jsonl"];

    /// <summary>
    /// Tells whether the file is read as NDJSON (one JSON record per line) rather than as CSV.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    public static bool IsNdjsonPath(string path) =>
        NdjsonExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the beginning of the file and tells whether it is an audit log.
    /// </summary>
    /// <param name="path">File to inspect; it may still be written by a running audit.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>True for a CSV whose first line is an audit header, or an NDJSON whose first record is an audit record.</returns>
    public static async Task<bool> IsAuditLogAsync(string path, CancellationToken cancellationToken = default)
    {
        // The reader skips the byte order mark that the audit writes at the start of its files.
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (!IsNdjsonPath(path))
        {
            return IsAuditCsvHeader(line);
        }

        // The NDJSON parser skips blank lines, a CSV is read from its very first line.
        while (line is not null && string.IsNullOrWhiteSpace(line))
        {
            line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        }

        return IsAuditNdjsonRecord(line);
    }

    /// <summary>
    /// Tells whether the first line of a CSV is the header of an audit log, whatever its separator.
    /// </summary>
    /// <param name="firstLine">First line of the file, null for an empty file.</param>
    public static bool IsAuditCsvHeader(string? firstLine)
    {
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return false;
        }

        // The separator kept on a tie does not matter here: a tie means that the line names the same columns either way.
        var delimiter = AuditCsvDelimiterDetector.Detect(firstLine, ',');
        return AuditCsvHistoryParser.IsHeaderLine(firstLine, delimiter);
    }

    /// <summary>
    /// Tells whether a line of an NDJSON file is an audit record.
    /// </summary>
    /// <param name="line">First record of the file, null for an empty file.</param>
    public static bool IsAuditNdjsonRecord(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.EnumerateObject().Any(property =>
                       AuditCsvHistoryParser.HeaderMarkerColumns.Contains(property.Name, StringComparer.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            // A first line that is not JSON belongs to another kind of file: the caller leaves the file out and
            // names it, instead of stopping on a file the user did not name.
            return false;
        }
    }
}
