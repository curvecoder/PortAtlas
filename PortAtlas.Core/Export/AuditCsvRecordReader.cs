using System.Runtime.CompilerServices;
using FileForge.IO;

namespace PortAtlas.Core.Export;

/// <summary>
/// Reads the records of an audit CSV, whose quoted fields (command lines) may span several lines.
/// </summary>
internal static class AuditCsvRecordReader
{
    /// <summary>
    /// Splits a header line into column names.
    /// </summary>
    /// <param name="line">Header line.</param>
    /// <param name="delimiter">Field separator of the file.</param>
    /// <returns>The column names, without surrounding spaces or quotes.</returns>
    public static IReadOnlyList<string> SplitHeader(string line, char delimiter)
    {
        // Column names hold neither separators nor line breaks, so the header splits on the separator alone.
        return line.Split(delimiter).Select(name => name.Trim().Trim('"')).ToList();
    }

    /// <summary>
    /// Reads every record of an audit CSV from its first line, header included.
    /// </summary>
    /// <param name="reader">Text of the file, positioned at its start so that line numbers match the file.</param>
    /// <param name="path">Path of the file, named in the error.</param>
    /// <param name="delimiter">Field separator of the file.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The records; a line of spaces is skipped, as when the log was read line by line.</returns>
    /// <exception cref="AuditLogFormatException">When a quoted field is not closed before the end of the file; the message names the file and the line.</exception>
    public static async IAsyncEnumerable<DelimitedRecord> ReadRecordsAsync(
        TextReader reader,
        string path,
        char delimiter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var records = DelimitedRecordReader.ReadRecordsAsync(reader, delimiter, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using (records.ConfigureAwait(false))
        {
            while (await MoveNextAsync(records, path).ConfigureAwait(false))
            {
                if (records.Current.Fields is not [var onlyField] || !string.IsNullOrWhiteSpace(onlyField))
                {
                    yield return records.Current;
                }
            }
        }
    }

    private static async ValueTask<bool> MoveNextAsync(IAsyncEnumerator<DelimitedRecord> records, string path)
    {
        try
        {
            return await records.MoveNextAsync().ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new AuditLogFormatException($"The audit log '{path}' cannot be read: {ex.Message}", ex);
        }
    }
}
