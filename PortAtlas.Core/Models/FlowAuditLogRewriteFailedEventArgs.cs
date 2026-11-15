namespace PortAtlas.Core.Models;

/// <summary>
/// Event arguments raised when a periodic rewrite of the audit logs fails and the audit keeps running.
/// </summary>
public sealed class FlowAuditLogRewriteFailedEventArgs(Exception error) : EventArgs
{
    /// <summary>
    /// Error that stopped the rewrite; its message names the log that was not replaced.
    /// </summary>
    public Exception Error { get; } = error ?? throw new ArgumentNullException(nameof(error));
}
