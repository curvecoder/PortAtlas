namespace PortAtlas.Core.Models;

/// <summary>
/// Event arguments raised when a scan cycle fails and the audit keeps running.
/// </summary>
public sealed class FlowAuditCycleFailedEventArgs(Exception error, int consecutiveFailures, int consecutiveFailureLimit) : EventArgs
{
    /// <summary>
    /// Error that ended the scan cycle.
    /// </summary>
    public Exception Error { get; } = error ?? throw new ArgumentNullException(nameof(error));

    /// <summary>
    /// Number of scan cycles that failed in a row, this one included.
    /// </summary>
    public int ConsecutiveFailures { get; } = consecutiveFailures;

    /// <summary>
    /// Number of scan cycles failing in a row that stops the audit.
    /// </summary>
    public int ConsecutiveFailureLimit { get; } = consecutiveFailureLimit;
}
