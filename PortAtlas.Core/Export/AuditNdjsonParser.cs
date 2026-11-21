using System.Text;
using System.Text.Json;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Parses NDJSON (newline-delimited JSON) lines and files into strongly-typed network audit records and entries.
/// </summary>
public sealed class AuditNdjsonParser
{
    private static readonly JsonSerializerOptions DeserializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Parses a single NDJSON line into a network audit entry.
    /// </summary>
    /// <param name="ndjsonLine">Raw single-line JSON string.</param>
    /// <returns>Parsed <see cref="NetworkAuditEntry"/>, or null if empty or malformed.</returns>
    public NetworkAuditEntry? ParseLine(string ndjsonLine)
    {
        if (string.IsNullOrWhiteSpace(ndjsonLine))
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<AuditNdjsonRecord>(ndjsonLine, DeserializerOptions);
            return record?.ToEntry();
        }
        catch (JsonException)
        {
            // A line cut short by an abrupt stop must not prevent reading the rest of the log:
            // the caller receives null, and ParseHistoryAsync records the line number as unreadable.
            return null;
        }
    }

    /// <summary>
    /// Asynchronously parses historical audit entries from an NDJSON file.
    /// </summary>
    /// <param name="ndjsonFilePath">Path to the NDJSON file on disk.</param>
    /// <param name="maxRows">Maximum number of recent entries to read (null or negative for all rows, 0 for none).</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Read-only list of parsed network audit entries.</returns>
    public async Task<IReadOnlyList<NetworkAuditEntry>> ParseHistoryAsync(
        string ndjsonFilePath,
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ndjsonFilePath);

        if (maxRows.HasValue && maxRows.Value == 0)
        {
            return [];
        }

        var results = (await ReadLogAsync(ndjsonFilePath, cancellationToken).ConfigureAwait(false)).Entries;
        if (maxRows.HasValue && maxRows.Value > 0 && results.Count > maxRows.Value)
        {
            return results.TakeLast(maxRows.Value).ToList();
        }

        return results;
    }

    /// <summary>
    /// Reads every entry of an NDJSON audit log, and the lines that hold no entry.
    /// </summary>
    /// <param name="ndjsonFilePath">Path to the NDJSON file on disk; a missing file reads as empty.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The entries in file order, and the one-based numbers of the non-blank lines that are not a JSON audit record.</returns>
    public async Task<AuditNdjsonReadResult> ReadLogAsync(string ndjsonFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ndjsonFilePath);

        var entries = new List<NetworkAuditEntry>();
        var unreadableLines = new List<int>();
        if (!File.Exists(ndjsonFilePath))
        {
            return new AuditNdjsonReadResult(entries, unreadableLines);
        }

        await using var stream = new FileStream(ndjsonFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            if (ParseLine(line) is { } entry)
            {
                entries.Add(entry);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                unreadableLines.Add(lineNumber);
            }
        }

        return new AuditNdjsonReadResult(entries, unreadableLines);
    }
}
