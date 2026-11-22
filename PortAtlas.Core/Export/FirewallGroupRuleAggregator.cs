using System.Net;
using PortAtlas.Core.Filtering;

namespace PortAtlas.Core.Export;

/// <summary>
/// Merges the rules of the detailed matrix into one row per source group, destination group, protocol and port.
/// </summary>
public sealed class FirewallGroupRuleAggregator
{
    internal const string AnySource = "Any";

    /// <summary>
    /// Groups the rules by the address groups of their ends.
    /// </summary>
    /// <param name="rules">Rules of the detailed matrix, already filtered.</param>
    /// <param name="groups">Address groups; an address in no group stands for itself.</param>
    /// <returns>One item per source group, destination group, port and protocol, sorted in that order.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    /// <remarks>
    /// A flow recorded at both of its ends is two rules of the detailed matrix (outbound and inbound): the row counts
    /// both and adds their observations, as the detailed matrix lists them.
    /// </remarks>
    public IReadOnlyList<FirewallGroupRuleItem> Aggregate(IReadOnlyList<FirewallRuleItem> rules, AddressGroupDirectory groups)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(groups);

        var rows = rules
            .Select(rule => (Rule: rule, Source: SourceOf(rule, groups), Destination: EndpointOf(rule.DestinationIp, groups)))
            .GroupBy(item => (
                Source: item.Source.ToUpperInvariant(),
                Destination: item.Destination.ToUpperInvariant(),
                item.Rule.Protocol,
                item.Rule.DestinationPort))
            .OrderBy(group => group.Key.Source, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Destination, StringComparer.Ordinal)
            .ThenBy(group => group.Key.DestinationPort)
            .ThenBy(group => group.Key.Protocol, StringComparer.Ordinal)
            .Select((group, index) =>
            {
                var first = group.First();
                return new FirewallGroupRuleItem(
                    $"GROUP-{index + 1:D3}",
                    first.Source,
                    first.Destination,
                    first.Rule.Protocol,
                    first.Rule.DestinationPort,
                    group.Count(),
                    group.Sum(item => item.Rule.ObservationCount),
                    group.Min(item => item.Rule.FirstSeenUtc),
                    group.Max(item => item.Rule.LastSeenUtc),
                    [.. group.Select(item => item.Rule.ProcessName).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
            });

        return [.. rows];
    }

    // A listening socket has no source host: its rule is written 0.0.0.0 or :: and stands for any peer.
    private static string SourceOf(FirewallRuleItem rule, AddressGroupDirectory groups) =>
        AddressGroupDirectory.IsUnspecified(rule.SourceIp) ? AnySource : EndpointOf(rule.SourceIp, groups);

    // A host in no group is written in one form, so that ::ffff:10.0.0.5 and 10.0.0.5, or fe80::1%3 and fe80::1, give
    // one row as they give one group.
    private static string EndpointOf(string ip, AddressGroupDirectory groups) =>
        groups.FindGroup(ip) ?? (IPAddress.TryParse(ip, out var address) ? IpAddressCanonicalForm.Of(address).ToString() : ip);
}
