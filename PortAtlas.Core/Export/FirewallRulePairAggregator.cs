namespace PortAtlas.Core.Export;

/// <summary>
/// Merges the flows of the flow matrix into one row per source and destination address.
/// </summary>
public sealed class FirewallRulePairAggregator
{
    /// <summary>
    /// Groups the flows by oriented address pair.
    /// </summary>
    /// <param name="flows">Flows of the flow matrix.</param>
    /// <returns>One item per oriented pair, sorted by source then destination address.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="flows"/> is null.</exception>
    public IReadOnlyList<FirewallRulePairItem> Aggregate(IReadOnlyList<FirewallFlowItem> flows)
    {
        ArgumentNullException.ThrowIfNull(flows);

        // A firewall rule is oriented: the pair keeps the source → destination direction, so the two directions between
        // two servers stay on two rows. A flow seen by both of its ends is already one row of the flow matrix, so the
        // pair counts it and its observations once.
        var pairs = flows
            .GroupBy(f => (Source: f.SourceIp.ToUpperInvariant(), Destination: f.DestinationIp.ToUpperInvariant(), Server: f.LocalServer.ToUpperInvariant()))
            .OrderBy(g => g.Key.Source, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Destination, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Server, StringComparer.Ordinal)
            .Select((group, index) =>
            {
                var first = group.First();
                return new FirewallRulePairItem(
                    $"PAIR-{index + 1:D3}",
                    first.SourceIp,
                    first.SourceHost,
                    first.DestinationIp,
                    first.DestinationHost,
                    group.Count(),
                    [.. group
                        .Select(f => (f.DestinationPort, f.Protocol))
                        .Distinct()
                        .OrderBy(p => p.DestinationPort)
                        .ThenBy(p => p.Protocol, StringComparer.Ordinal)
                        .Select(p => $"{p.DestinationPort}/{p.Protocol}")],
                    group.Sum(f => f.ObservationCount),
                    group.Min(f => f.FirstSeenUtc),
                    group.Max(f => f.LastSeenUtc),
                    [.. group.Select(f => f.SeenFrom).Distinct().Order()]);
            });

        return [.. pairs];
    }
}
