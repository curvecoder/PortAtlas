using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Parser capable of reading and hydrating historical network audit entries from an RFC 4180 CSV file.
/// </summary>
public interface IAuditCsvHistoryParser
{
    /// <summary>
    /// Reads and parses historical audit entries from a CSV file.
    /// </summary>
    /// <param name="csvFilePath">Path to the CSV file on disk.</param>
    /// <param name="maxRows">Maximum number of recent entries to read (null or negative for all rows, 0 for none).</param>
    /// <param name="delimiter">Field delimiter character (default: ',').</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Read-only list of parsed network audit entries.</returns>
    Task<IReadOnlyList<NetworkAuditEntry>> ParseHistoryAsync(
        string csvFilePath,
        int? maxRows = null,
        char delimiter = ',',
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the audit log an audit resumes from, refusing a log whose flows could not be matched with the observed ones.
    /// </summary>
    /// <param name="csvFilePath">Path to the CSV file on disk.</param>
    /// <param name="maxRows">Maximum number of recent entries to read (null or negative for all rows, 0 for none).</param>
    /// <param name="delimiter">Field delimiter character (default: ',').</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Read-only list of parsed network audit entries.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the header has no column for a part of the flow key or for the first-seen date,
    /// or when a read line has an unreadable first-seen date; the message names the columns or the line.
    /// </exception>
    Task<IReadOnlyList<NetworkAuditEntry>> ParseLogForResumeAsync(
        string csvFilePath,
        int? maxRows = null,
        char delimiter = ',',
        CancellationToken cancellationToken = default);
}
