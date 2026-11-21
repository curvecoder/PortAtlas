using PortAtlas.Core.Filtering;
using TopologyForge.Filtering;
using TopologyForge.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Filtering and formatting options for consolidating network audits into firewall and security flow matrices.
/// </summary>
public sealed record FirewallRuleExportOptions
{
    /// <summary>
    /// Exclude high ephemeral ports (49152-65535) on the destination side.
    /// </summary>
    public bool ExcludeEphemeralPorts { get; init; } = true;

    /// <summary>
    /// Minimum gross observation count required for a rule to be included in the export (default: 1).
    /// </summary>
    public int MinObservations { get; init; } = 1;

    /// <summary>
    /// Optional freshness filter in days: excludes rules that have not been observed within the specified recent window.
    /// </summary>
    public int? ActiveWithinDays { get; init; }

    /// <summary>
    /// Destination zone filter ("all", "internal" for RFC 1918, "public" for WAN/Internet).
    /// </summary>
    public string DestinationZoneFilter { get; init; } = "all";

    /// <summary>
    /// When true, includes passive listening ports only.
    /// </summary>
    public bool ListenersOnly { get; init; } = false;

    /// <summary>
    /// Flow direction filter ("all", "out", "in").
    /// </summary>
    public string Direction { get; init; } = "all";

    /// <summary>
    /// Destination ports or port ranges to include.
    /// </summary>
    public IReadOnlyList<NetworkPortRange> IncludedPorts { get; init; } = [];

    /// <summary>
    /// Destination ports or port ranges to exclude.
    /// </summary>
    public IReadOnlyList<NetworkPortRange> ExcludedPorts { get; init; } = [];

    /// <summary>
    /// Host name wildcard patterns to include (supports '*').
    /// </summary>
    public IReadOnlyList<string> IncludedHosts { get; init; } = [];

    /// <summary>
    /// Host name wildcard patterns to exclude (supports '*').
    /// </summary>
    public IReadOnlyList<string> ExcludedHosts { get; init; } = [];

    /// <summary>
    /// IP addresses or CIDR subnets to include.
    /// </summary>
    public IReadOnlyList<IpAddressRange> IncludedIps { get; init; } = [];

    /// <summary>
    /// IP addresses or CIDR subnets to exclude.
    /// </summary>
    public IReadOnlyList<IpAddressRange> ExcludedIps { get; init; } = [];

    /// <summary>
    /// Process names or targets to include.
    /// </summary>
    public IReadOnlyList<string> IncludedProcesses { get; init; } = [];

    /// <summary>
    /// Process names or targets to exclude.
    /// </summary>
    public IReadOnlyList<string> ExcludedProcesses { get; init; } = [];

    /// <summary>
    /// Field delimiter character for the CSV output (comma ',' by default). An audit CSV is read with the delimiter
    /// found on its own first line; this one only settles a first line that fits several delimiters equally.
    /// </summary>
    public char Delimiter { get; init; } = ',';

    /// <summary>
    /// Destination of the pair matrix (one row per source and destination address), written beside the detailed
    /// matrix; <see langword="null"/> skips it.
    /// </summary>
    public string? PairsOutputPath { get; init; }

    /// <summary>
    /// Destination of the flow matrix (one row per flow, merged from the logs of both ends); <see langword="null"/>
    /// skips it.
    /// </summary>
    public string? FlowsOutputPath { get; init; }

    /// <summary>
    /// Destination of the host rule matrix (an outbound rule on the source and an inbound rule on the destination of
    /// each flow); <see langword="null"/> skips it.
    /// </summary>
    public string? HostRulesOutputPath { get; init; }

    /// <summary>
    /// Destination of the listening socket matrix, written in the same pass whatever <see cref="ListenersOnly"/> says;
    /// <see langword="null"/> skips it.
    /// </summary>
    public string? ListenersOutputPath { get; init; }
}
