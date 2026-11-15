using System.Net;
using TopologyForge.Filtering;

namespace PortAtlas.Core.Filtering;

/// <summary>
/// Evaluates whether an IP address matches a configured set of address rules (exact IP or CIDR subnets).
/// </summary>
public sealed class IpAddressRangeMatcher
{
    private readonly IpAddressRangeSet _ranges;

    /// <summary>
    /// Initializes a new matcher instance with the given filtering rules (CIDR subnets or exact IP addresses).
    /// A rule that cannot be read is left out.
    /// </summary>
    /// <param name="rules">Exclusion rules (e.g. "192.168.1.50", "10.0.0.0/8").</param>
    public IpAddressRangeMatcher(IEnumerable<string>? rules)
        : this(ReadableRanges(rules))
    {
    }

    private IpAddressRangeMatcher(IEnumerable<IpAddressRange> ranges) => _ranges = new IpAddressRangeSet(ranges);

    /// <summary>
    /// Creates a matcher from ranges already validated.
    /// </summary>
    /// <param name="ranges">Addresses and subnets to match.</param>
    /// <returns>The matcher.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is <see langword="null"/>.</exception>
    public static IpAddressRangeMatcher FromRanges(IEnumerable<IpAddressRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        return new IpAddressRangeMatcher(ranges);
    }

    /// <summary>
    /// Indicates whether at least one matching rule is configured.
    /// </summary>
    public bool HasRules => !_ranges.IsEmpty;

    /// <summary>
    /// Determines whether the specified IP address matches any configured address or CIDR range.
    /// An IPv4 peer reported in its IPv4-mapped form is matched as IPv4, and an IPv6 rule never matches an IPv4 peer.
    /// </summary>
    /// <param name="address">IP address to evaluate.</param>
    /// <returns>True if the address matches any rule; otherwise false.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public bool IsMatch(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return _ranges.Contains(address);
    }

    // The rules typed in a configuration file or added from the dashboard have always been read leniently: an
    // unreadable rule is skipped rather than stopping the audit, unlike IpAddressRangeSet.Parse.
    private static IEnumerable<IpAddressRange> ReadableRanges(IEnumerable<string>? rules)
    {
        if (rules is null)
        {
            yield break;
        }

        foreach (var rule in rules)
        {
            if (IpAddressRange.TryParse(rule, out var range))
            {
                yield return range;
            }
        }
    }
}
