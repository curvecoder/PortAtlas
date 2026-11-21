namespace PortAtlas.Core.Export;

/// <summary>
/// One row of the flow matrix: a connection from a source to a destination port, merged from the logs of both ends.
/// </summary>
public sealed record FirewallFlowItem
{
    /// <summary>
    /// Sequential identifier (<c>FLOW-001</c>) assigned after sorting.
    /// </summary>
    public string FlowId { get; init; } = string.Empty;

    /// <summary>
    /// Address that opens the connection.
    /// </summary>
    public required string SourceIp { get; init; }

    /// <summary>
    /// Name of the source: the audited server owning the address, otherwise the name found in the logs, otherwise the address.
    /// </summary>
    public required string SourceHost { get; init; }

    /// <summary>
    /// Address that accepts the connection.
    /// </summary>
    public required string DestinationIp { get; init; }

    /// <summary>
    /// Name of the destination, chosen as for <see cref="SourceHost"/>.
    /// </summary>
    public required string DestinationHost { get; init; }

    /// <summary>
    /// Zone of the destination ("Internal" or "Public"), as in the detailed matrix.
    /// </summary>
    public required string DestinationZone { get; init; }

    /// <summary>
    /// Transport protocol ("TCP" or "UDP").
    /// </summary>
    public required string Protocol { get; init; }

    /// <summary>
    /// Destination port.
    /// </summary>
    public required int DestinationPort { get; init; }

    /// <summary>
    /// Service name of the port, or <c>port/protocol</c> when the rules of the flow name different services.
    /// </summary>
    public required string ServiceName { get; init; }

    /// <summary>
    /// Processes that opened the flow on the source, sorted; empty when the source log did not record it.
    /// </summary>
    public required IReadOnlyList<string> SourceProcesses { get; init; }

    /// <summary>
    /// Processes that accepted the flow on the destination, sorted; empty when the destination log did not record it.
    /// </summary>
    public required IReadOnlyList<string> DestinationProcesses { get; init; }

    /// <summary>
    /// End whose log recorded the flow.
    /// </summary>
    public required FlowObservationSide SeenFrom { get; init; }

    /// <summary>
    /// Observations recorded by the source log.
    /// </summary>
    public required long SourceObservationCount { get; init; }

    /// <summary>
    /// Observations recorded by the destination log.
    /// </summary>
    public required long DestinationObservationCount { get; init; }

    /// <summary>
    /// Earliest observation on either end.
    /// </summary>
    public required DateTimeOffset FirstSeenUtc { get; init; }

    /// <summary>
    /// Latest observation on either end.
    /// </summary>
    public required DateTimeOffset LastSeenUtc { get; init; }

    /// <summary>
    /// Audited server holding the flow when its destination is a loopback or unspecified address, which names no
    /// machine; empty otherwise. Not written to the matrix: the host names already show it.
    /// </summary>
    public string LocalServer { get; init; } = string.Empty;

    /// <summary>
    /// Observation count of the flow, counted once: the larger of the two ends, since both logs saw the same connection.
    /// </summary>
    public long ObservationCount => Math.Max(SourceObservationCount, DestinationObservationCount);
}
