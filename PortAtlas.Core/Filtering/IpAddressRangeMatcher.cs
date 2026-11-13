using System.Net;
using System.Net.Sockets;

namespace PortAtlas.Core.Filtering;

/// <summary>
/// Evaluates whether an IP address matches a configured set of address rules (exact IP or CIDR subnets).
/// </summary>
public sealed class IpAddressRangeMatcher
{
    private readonly List<CidrRange> _subnets = [];
    private readonly HashSet<IPAddress> _addresses = [];

    /// <summary>
    /// Initializes a new matcher instance with the given filtering rules (CIDR subnets or exact IP addresses).
    /// A rule that cannot be read is left out.
    /// </summary>
    /// <param name="rules">Exclusion rules (e.g. "192.168.1.50", "10.0.0.0/8").</param>
    public IpAddressRangeMatcher(IEnumerable<string>? rules)
    {
        if (rules is null)
        {
            return;
        }

        foreach (var rule in rules)
        {
            if (CidrRange.TryParse(rule, out var range))
            {
                Add(range);
            }
        }
    }

    /// <summary>
    /// Creates a matcher from ranges already validated.
    /// </summary>
    /// <param name="ranges">Addresses and subnets to match.</param>
    /// <returns>The matcher.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is <see langword="null"/>.</exception>
    public static IpAddressRangeMatcher FromRanges(IEnumerable<CidrRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var matcher = new IpAddressRangeMatcher(rules: null);
        foreach (var range in ranges)
        {
            matcher.Add(range);
        }

        return matcher;
    }

    /// <summary>
    /// Indicates whether at least one matching rule is configured.
    /// </summary>
    public bool HasRules => _addresses.Count > 0 || _subnets.Count > 0;

    /// <summary>
    /// Determines whether the specified IP address matches any configured address or CIDR range.
    /// </summary>
    /// <param name="address">IP address to evaluate.</param>
    /// <returns>True if the address matches any rule; otherwise false.</returns>
    public bool IsMatch(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        // A link-local peer comes with the zone of the interface it was seen on (fe80::1%12). The zone belongs to
        // this machine, not to the peer, and the rules are kept without it: the set would otherwise never match.
        var candidate = address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes())
            : address;

        if (_addresses.Contains(candidate))
        {
            return true;
        }

        if (candidate.IsIPv4MappedToIPv6 && _addresses.Contains(candidate.MapToIPv4()))
        {
            return true;
        }

        for (var i = 0; i < _subnets.Count; i++)
        {
            if (_subnets[i].Contains(candidate))
            {
                return true;
            }
        }

        return false;
    }

    // Exact addresses are the bulk of the rules (every exclusion added from the dashboard is one): they are looked up
    // in a set, in both their IPv4 and IPv4-mapped IPv6 forms, instead of being walked as one-address subnets.
    private void Add(CidrRange range)
    {
        if (!range.IsSingleAddress)
        {
            _subnets.Add(range);
            return;
        }

        var address = range.NetworkAddress;
        _addresses.Add(address);
        if (address.IsIPv4MappedToIPv6)
        {
            _addresses.Add(address.MapToIPv4());
        }
        else if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            _addresses.Add(address.MapToIPv6());
        }
    }
}
