namespace PortAtlas.Core.Export;

/// <summary>
/// Merges the rules of the detailed matrix into one row per flow: the outbound rule recorded by the source and the
/// inbound rule recorded by the destination describe the same connection.
/// </summary>
public sealed class FirewallFlowAggregator
{
    private const string OutboundDirection = "Outbound";

    /// <summary>
    /// Groups the rules by source, destination, protocol and destination port.
    /// </summary>
    /// <param name="rules">Rules of the detailed matrix, already filtered.</param>
    /// <param name="servers">Addresses of the audited servers, whose own names are preferred.</param>
    /// <returns>One item per flow, sorted and numbered.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public IReadOnlyList<FirewallFlowItem> Aggregate(IReadOnlyList<FirewallRuleItem> rules, AuditedServerDirectory servers)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(servers);

        var flows = rules
            .GroupBy(rule => (
                Source: rule.SourceIp.ToUpperInvariant(),
                Destination: rule.DestinationIp.ToUpperInvariant(),
                rule.Protocol,
                rule.DestinationPort,
                Server: LocalServerOf(rule).ToUpperInvariant()))
            .Select(group => Merge([.. group], servers))
            .OrderBy(f => f.DestinationZone, StringComparer.Ordinal)
            .ThenBy(f => f.DestinationPort)
            .ThenBy(f => f.DestinationIp, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.SourceIp, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Protocol, StringComparer.Ordinal)
            .ThenBy(f => f.LocalServer, StringComparer.OrdinalIgnoreCase)
            .Select((flow, index) => flow with { FlowId = $"FLOW-{index + 1:D3}" });

        return [.. flows];
    }

    // A loopback or unspecified destination names no machine: the flow stays inside the server whose log saw it, so
    // two servers holding the same local flow keep two rows, as in the detailed matrix.
    private static string LocalServerOf(FirewallRuleItem rule) =>
        NetworkZoneClassifier.NamesOneMachine(rule.DestinationIp) ? string.Empty : rule.AuditedServer;

    private static FirewallFlowItem Merge(IReadOnlyList<FirewallRuleItem> rules, AuditedServerDirectory servers)
    {
        var outbound = rules.Where(r => string.Equals(r.Direction, OutboundDirection, StringComparison.Ordinal)).ToList();
        var inbound = rules.Where(r => !string.Equals(r.Direction, OutboundDirection, StringComparison.Ordinal)).ToList();
        var first = rules[0];

        var seenFrom = (outbound.Count > 0, inbound.Count > 0) switch
        {
            (true, true) => FlowObservationSide.Both,
            (true, false) => FlowObservationSide.Source,
            _ => FlowObservationSide.Destination
        };

        return new FirewallFlowItem
        {
            SourceIp = first.SourceIp,
            // Each log names its own server by its short name and the other end by its DNS name: the log of the end
            // is the one that knows its name.
            SourceHost = HostName(first.SourceIp, outbound.Select(r => r.SourceHost), inbound.Select(r => r.SourceHost), servers),
            DestinationIp = first.DestinationIp,
            DestinationHost = HostName(first.DestinationIp, inbound.Select(r => r.DestinationHost), outbound.Select(r => r.DestinationHost), servers),
            DestinationZone = first.DestinationZone,
            Protocol = first.Protocol,
            DestinationPort = first.DestinationPort,
            ServiceName = ServiceName(inbound.Count > 0 ? inbound : outbound, first),
            SourceProcesses = Processes(outbound),
            DestinationProcesses = Processes(inbound),
            SeenFrom = seenFrom,
            SourceObservationCount = outbound.Sum(r => r.ObservationCount),
            DestinationObservationCount = inbound.Sum(r => r.ObservationCount),
            FirstSeenUtc = rules.Min(r => r.FirstSeenUtc),
            LastSeenUtc = rules.Max(r => r.LastSeenUtc),
            LocalServer = rules.Select(LocalServerOf).Order(StringComparer.Ordinal).First()
        };
    }

    private static string HostName(string ip, IEnumerable<string> ownLogNames, IEnumerable<string> otherLogNames, AuditedServerDirectory servers)
    {
        if (NetworkZoneClassifier.NamesOneMachine(ip) && servers.TryGetServerName(ip, out var serverName))
        {
            return serverName;
        }

        var known = FirstName(ownLogNames, ip) ?? FirstName(otherLogNames, ip);
        return known ?? ip;
    }

    // A rule shows the address when no name was found: any real name is better, and the ordinal order keeps the
    // choice independent of the order of the logs.
    private static string? FirstName(IEnumerable<string> names, string ip) =>
        names
            .Where(name => !string.IsNullOrWhiteSpace(name) && !string.Equals(name, ip, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();

    // The destination runs the service, so its rules name it best; two processes listening on the same port name no
    // single service.
    private static string ServiceName(IReadOnlyList<FirewallRuleItem> preferred, FirewallRuleItem first)
    {
        var names = preferred.Select(r => r.ServiceName).Distinct(StringComparer.Ordinal).ToList();
        return names.Count == 1 ? names[0] : $"{first.DestinationPort}/{first.Protocol}";
    }

    private static IReadOnlyList<string> Processes(IEnumerable<FirewallRuleItem> rules) =>
        [.. rules.Select(r => r.ProcessName).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
