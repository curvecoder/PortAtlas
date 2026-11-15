namespace PortAtlas.Core.Export;

/// <summary>
/// Refuses to start an audit whose startup rewrite would erase the flows of an NDJSON log.
/// </summary>
public static class NdjsonLogLossValidator
{
    /// <summary>
    /// Checks that rewriting the logs from the flows resumed from the CSV log does not discard NDJSON records.
    /// </summary>
    /// <param name="csvLogPath">CSV log the audit resumes from.</param>
    /// <param name="resumedFlowCount">Number of flows read back from the CSV log.</param>
    /// <param name="ndjsonLogPath">NDJSON log written by the audit; null or blank when none is configured.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <exception cref="InvalidDataException">When the NDJSON log holds a record while the CSV log is missing or holds no flow.</exception>
    public static async Task EnsureNoFlowIsLostAsync(
        string csvLogPath,
        int resumedFlowCount,
        string? ndjsonLogPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvLogPath);

        if (resumedFlowCount > 0 || string.IsNullOrWhiteSpace(ndjsonLogPath) || !File.Exists(ndjsonLogPath))
        {
            return;
        }

        if (!await AuditLogRecognizer.IsAuditLogAsync(ndjsonLogPath, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        throw new InvalidDataException(
            $"The NDJSON log '{ndjsonLogPath}' holds flows but the CSV log '{csvLogPath}' is missing or holds none: " +
            "the audit resumes from the CSV log only, so starting would erase them. " +
            "Restore the CSV log or move the NDJSON log.");
    }
}
