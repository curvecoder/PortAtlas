using TopologyForge.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Topology graph built from audit logs, with the lines of those logs that could not be read as a flow.
/// </summary>
/// <param name="Graph">Topology graph built from the flows read.</param>
/// <param name="RejectedLines">Lines left out, one entry per audit log that had any, in reading order.</param>
public sealed record AuditTopologyReadResult(NetworkTopologyGraph Graph, IReadOnlyList<RejectedAuditLogLines> RejectedLines);
