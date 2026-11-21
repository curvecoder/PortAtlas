namespace PortAtlas.Core.Export;

/// <summary>
/// One row of the host rule matrix: a rule to set on one host, as a network security group or security list holds it.
/// </summary>
public sealed record FirewallHostRuleItem
{
    /// <summary>
    /// Sequential identifier (<c>HOST-001</c>) assigned after sorting.
    /// </summary>
    public string HostRuleId { get; init; } = string.Empty;

    /// <summary>
    /// Name of the host that carries the rule.
    /// </summary>
    public required string Host { get; init; }

    /// <summary>
    /// Address of the host that carries the rule.
    /// </summary>
    public required string HostIp { get; init; }

    /// <summary>
    /// Whether an audit log of this address was read.
    /// </summary>
    public required bool IsAudited { get; init; }

    /// <summary>
    /// "Inbound" when the host accepts the connection, "Outbound" when it opens it.
    /// </summary>
    public required string Direction { get; init; }

    /// <summary>
    /// Address of the other end of the flow.
    /// </summary>
    public required string PeerIp { get; init; }

    /// <summary>
    /// Name of the other end of the flow.
    /// </summary>
    public required string PeerHost { get; init; }

    /// <summary>
    /// Zone of the other end ("Internal" or "Public").
    /// </summary>
    public required string PeerZone { get; init; }

    /// <summary>
    /// Transport protocol ("TCP" or "UDP").
    /// </summary>
    public required string Protocol { get; init; }

    /// <summary>
    /// Destination port of the flow, on the host for an inbound rule, on the peer for an outbound one.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Service name of the flow.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Processes of the host that use the flow; empty when the host's log did not record it.
    /// </summary>
    public required IReadOnlyList<string> LocalProcesses { get; init; }

    /// <summary>
    /// Whether the host's own log recorded the flow.
    /// </summary>
    public required HostRuleEvidence Evidence { get; init; }

    /// <summary>
    /// Observations recorded by the host's log, or by the peer's log for an inferred rule.
    /// </summary>
    public required long ObservationCount { get; init; }

    /// <summary>
    /// Earliest observation of the flow.
    /// </summary>
    public required DateTimeOffset FirstSeenUtc { get; init; }

    /// <summary>
    /// Latest observation of the flow.
    /// </summary>
    public required DateTimeOffset LastSeenUtc { get; init; }

    /// <summary>
    /// Identifier of the flow in the flow matrix.
    /// </summary>
    public required string FlowId { get; init; }
}
