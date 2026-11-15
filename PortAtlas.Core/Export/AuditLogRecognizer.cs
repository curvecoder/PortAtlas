using System.Text;
using System.Text.Json;

namespace PortAtlas.Core.Export;

/// <summary>
/// Tells an audit log from the other files of a directory by its first line, the way the parsers read it.
/// </summary>
internal static class AuditLogRecognizer
{
    /// <summary>
    /// Number of characters read at most to find the first line, or the first record of an NDJSON file.
    /// </summary>
    internal const int MaxFirstLineLength = 1024 * 1024;

    private const int ReadBufferLength = 4096;

    private static readonly string[] NdjsonExtensions = [".ndjson", ".jsonl"];

    private static readonly char[] LineBreaks = ['\r', '\n'];

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

        var beginning = await ReadUpToFirstRecordAsync(reader, cancellationToken).ConfigureAwait(false);
        if (beginning is null)
        {
            return false;
        }

        var lines = beginning.Split(LineBreaks);
        if (!IsNdjsonPath(path))
        {
            return IsAuditCsvHeader(lines[0]);
        }

        // The NDJSON parser skips blank lines, a CSV is read from its very first line.
        return IsAuditNdjsonRecord(lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)));
    }

    // A directory may hold a large file without line breaks (a dump, a minified export) that ReadLineAsync would load
    // whole: 1.4 GB allocated for a 200 MB file. The text is read up to the first line break that follows a non-blank
    // character, which ends the CSV header and the first NDJSON record alike; null when MaxFirstLineLength characters
    // hold none, since no audit header or record is that long. Blank lines before an NDJSON record count toward the
    // limit: a log the audit writes has none.
    private static async Task<string?> ReadUpToFirstRecordAsync(TextReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[ReadBufferLength];
        var text = new StringBuilder();
        var hasContent = false;
        while (text.Length < MaxFirstLineLength)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return text.ToString();
            }

            var recordEnd = FindRecordEnd(buffer.AsSpan(0, count), ref hasContent);
            text.Append(buffer, 0, recordEnd < 0 ? count : recordEnd);
            if (recordEnd >= 0)
            {
                return text.ToString();
            }
        }

        return null;
    }

    private static int FindRecordEnd(ReadOnlySpan<char> chunk, ref bool hasContent)
    {
        for (var i = 0; i < chunk.Length; i++)
        {
            if (hasContent && chunk[i] is '\r' or '\n')
            {
                return i;
            }

            hasContent |= !char.IsWhiteSpace(chunk[i]);
        }

        return -1;
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
