using System.Text;

namespace PortAtlas.Core.Export;

/// <summary>
/// Finds the field separator of an audit CSV from its header line, so that a log written with another
/// <c>CsvDelimiter</c> than the current one is still read.
/// </summary>
internal static class AuditCsvDelimiterDetector
{
    private static readonly char[] UsualSeparators = [',', ';', '\t', '|'];

    private static readonly HashSet<string> AuditColumnNames = new(Enum.GetNames<AuditCsvColumn>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the first line of the file and returns its separator (see <see cref="Detect"/>).
    /// </summary>
    /// <param name="csvFilePath">Audit CSV to inspect; it may still be written by a running audit.</param>
    /// <param name="configuredDelimiter">Separator kept on a tie, and for an empty file.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The separator of the file.</returns>
    public static async Task<char> DetectAsync(string csvFilePath, char configuredDelimiter, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(csvFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var firstLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        return Detect(firstLine, configuredDelimiter);
    }

    /// <summary>
    /// Returns the separator that splits the line into the most audit column names or, when the line names none
    /// (log without header), the separator that occurs most often outside its quoted fields.
    /// </summary>
    /// <param name="firstLine">First line of an audit CSV.</param>
    /// <param name="configuredDelimiter">Separator kept on a tie, and for an empty line.</param>
    /// <returns>The separator of the line.</returns>
    public static char Detect(string? firstLine, char configuredDelimiter)
    {
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return configuredDelimiter;
        }

        var (headerSeparator, auditColumnCount) = FindHighestCount(configuredDelimiter, separator => CountAuditColumns(firstLine, separator));
        if (auditColumnCount > 0)
        {
            return headerSeparator;
        }

        // A log written without header starts with a data row: its separator is the one that occurs most often
        // outside the quoted fields, where a command line may hold any of the others.
        return FindHighestCount(configuredDelimiter, separator => CountUnquotedOccurrences(firstLine, separator)).Separator;
    }

    // The configured separator is evaluated first and replaced only by a strictly higher count, so it wins a tie.
    private static (char Separator, int Count) FindHighestCount(char configuredDelimiter, Func<char, int> count)
    {
        var best = (Separator: configuredDelimiter, Count: count(configuredDelimiter));

        foreach (var separator in UsualSeparators)
        {
            var separatorCount = count(separator);
            if (separatorCount > best.Count)
            {
                best = (separator, separatorCount);
            }
        }

        return best;
    }

    // A column name holds no separator and no quote, so a plain split is enough; the quotes are trimmed because a
    // spreadsheet that saved the log may have quoted every header.
    private static int CountAuditColumns(string line, char separator)
        => line.Split(separator).Count(field => AuditColumnNames.Contains(field.Trim().Trim('"')));

    private static int CountUnquotedOccurrences(string line, char separator)
    {
        var occurrences = 0;
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                // An escaped quote ("") toggles twice and leaves the state unchanged.
                inQuotes = !inQuotes;
            }
            else if (character == separator && !inQuotes)
            {
                occurrences++;
            }
        }

        return occurrences;
    }
}
