using System.Net;

namespace PortAtlas.Core.Export;

/// <summary>
/// Turns each flow into the rules its two ends carry: an outbound rule on the source and an inbound rule on the
/// destination, as network security groups and security lists are set per resource and per direction.
/// </summary>
public sealed class FirewallHostRuleAggregator
{
    private const string InboundDirection = "Inbound";
    private const string OutboundDirection = "Outbound";

    /// <summary>
    /// Builds the rules of every host with a private address.
    /// </summary>
    /// <param name="flows">Flows of the flow matrix, numbered.</param>
    /// <param name="servers">Addresses of the audited servers.</param>
    /// <returns>The rules, sorted by host and numbered.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public IReadOnlyList<FirewallHostRuleItem> Aggregate(IReadOnlyList<FirewallFlowItem> flows, AuditedServerDirectory servers)
    {
        ArgumentNullException.ThrowIfNull(flows);
        ArgumentNullException.ThrowIfNull(servers);

        var rules = new List<FirewallHostRuleItem>();
        foreach (var flow in flows)
        {
            if (CarriesRules(flow.SourceIp))
            {
                rules.Add(SourceRule(flow, servers));
            }

            if (CarriesRules(flow.DestinationIp))
            {
                rules.Add(DestinationRule(flow, servers));
            }
        }

        var sorted = rules
            .OrderBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.HostIp, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Direction, StringComparer.Ordinal)
            .ThenBy(r => r.Port)
            .ThenBy(r => r.Protocol, StringComparer.Ordinal)
            .ThenBy(r => r.PeerIp, StringComparer.OrdinalIgnoreCase)
            .Select((rule, index) => rule with { HostRuleId = $"HOST-{index + 1:D3}" });

        return [.. sorted];
    }

    // A security group belongs to a machine of the private network: a public address is somebody else's, and a
    // loopback or unspecified address names no machine, its flow never crossing the network.
    private static bool CarriesRules(string ip) =>
        IPAddress.TryParse(ip, out var address) &&
        NetworkZoneClassifier.NamesOneMachine(address) &&
        NetworkZoneClassifier.IsInternal(address);

    private static FirewallHostRuleItem SourceRule(FirewallFlowItem flow, AuditedServerDirectory servers)
    {
        var observed = flow.SeenFrom != FlowObservationSide.Destination;
        return new FirewallHostRuleItem
        {
            Host = flow.SourceHost,
            HostIp = flow.SourceIp,
            IsAudited = servers.TryGetServerName(flow.SourceIp, out _),
            Direction = OutboundDirection,
            PeerIp = flow.DestinationIp,
            PeerHost = flow.DestinationHost,
            PeerZone = flow.DestinationZone,
            Protocol = flow.Protocol,
            Port = flow.DestinationPort,
            ServiceName = flow.ServiceName,
            LocalProcesses = flow.SourceProcesses,
            Evidence = observed ? HostRuleEvidence.Observed : HostRuleEvidence.Inferred,
            ObservationCount = observed ? flow.SourceObservationCount : flow.DestinationObservationCount,
            FirstSeenUtc = flow.FirstSeenUtc,
            LastSeenUtc = flow.LastSeenUtc,
            FlowId = flow.FlowId
        };
    }

    private static FirewallHostRuleItem DestinationRule(FirewallFlowItem flow, AuditedServerDirectory servers)
    {
        var observed = flow.SeenFrom != FlowObservationSide.Source;
        return new FirewallHostRuleItem
        {
            Host = flow.DestinationHost,
            HostIp = flow.DestinationIp,
            IsAudited = servers.TryGetServerName(flow.DestinationIp, out _),
            Direction = InboundDirection,
            PeerIp = flow.SourceIp,
            PeerHost = flow.SourceHost,
            PeerZone = NetworkZoneClassifier.ZoneOf(flow.SourceIp),
            Protocol = flow.Protocol,
            Port = flow.DestinationPort,
            ServiceName = flow.ServiceName,
            LocalProcesses = flow.DestinationProcesses,
            Evidence = observed ? HostRuleEvidence.Observed : HostRuleEvidence.Inferred,
            ObservationCount = observed ? flow.DestinationObservationCount : flow.SourceObservationCount,
            FirstSeenUtc = flow.FirstSeenUtc,
            LastSeenUtc = flow.LastSeenUtc,
            FlowId = flow.FlowId
        };
    }
}
