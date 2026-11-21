namespace PortAtlas.Core.Export;

/// <summary>
/// Source of a host rule: the host's own log, or the log of the other end of the flow.
/// </summary>
public enum HostRuleEvidence
{
    /// <summary>
    /// The audit log of the host recorded the flow.
    /// </summary>
    Observed,

    /// <summary>
    /// Only the log of the other end recorded the flow: the host has no log, or its log does not cover the period.
    /// </summary>
    Inferred
}
