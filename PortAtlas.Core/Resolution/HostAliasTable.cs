using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using TopologyForge.Filtering;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// One line of <c>CustomHostAliases</c>: the host name shown for every address of a range.
/// </summary>
/// <param name="Range">Address or CIDR subnet named by the alias, in its network form.</param>
/// <param name="HostName">Host name shown instead of reverse DNS, trimmed.</param>
public sealed record HostAlias(IpAddressRange Range, string HostName);

/// <summary>
/// The <c>CustomHostAliases</c> of the configuration: fixed host names by IP address or CIDR subnet, as the audit,
/// <c>resolve</c> and <c>lookup</c> apply them.
/// </summary>
public sealed class HostAliasTable
{
    // Single addresses are looked up by key, since a configuration may still hold thousands of them; subnets are few
    // and kept from the narrowest to the widest, so that the first one holding an address is the most precise.
    private readonly Dictionary<IPAddress, HostAlias> _addresses;
    private readonly IReadOnlyList<HostAlias> _subnets;

    private HostAliasTable(Dictionary<IPAddress, HostAlias> addresses, IReadOnlyList<HostAlias> subnets)
    {
        _addresses = addresses;
        _subnets = subnets;
    }

    /// <summary>
    /// <see langword="true"/> when no alias is configured.
    /// </summary>
    public bool IsEmpty => _addresses.Count == 0 && _subnets.Count == 0;

    /// <summary>
    /// Reads the aliases of the configuration.
    /// </summary>
    /// <param name="customHostAliases">Host names by IP address or CIDR subnet, as written in the configuration.</param>
    /// <returns>The table; empty when <paramref name="customHostAliases"/> is null.</returns>
    /// <remarks>
    /// A key that is neither an address nor a subnet, or an empty name, cannot name anything: it is left out rather than
    /// refused, so that one wrong line of the configuration file does not stop the audit or the resolve command. Two
    /// keys naming the same range (<c>192.168.10.0/24</c> and <c>192.168.10.9/24</c>) keep the name read last, which
    /// lets an <c>--alias</c> value replace the alias of the configuration file.
    /// </remarks>
    public static HostAliasTable Parse(IReadOnlyDictionary<string, string>? customHostAliases)
    {
        var byRange = new Dictionary<IpAddressRange, HostAlias>();
        foreach (var (key, hostName) in customHostAliases ?? new Dictionary<string, string>())
        {
            if (IpAddressRange.TryParse(key, out var range) && !string.IsNullOrWhiteSpace(hostName))
            {
                byRange[range] = new HostAlias(range, hostName.Trim());
            }
        }

        var addresses = byRange.Values
            .Where(alias => alias.Range.IsSingleAddress)
            .ToDictionary(alias => alias.Range.NetworkAddress);
        var subnets = byRange.Values
            .Where(alias => !alias.Range.IsSingleAddress)
            .OrderByDescending(alias => alias.Range.PrefixLength)
            .ToList();
        return new HostAliasTable(addresses, subnets);
    }

    /// <summary>
    /// Finds the alias of an address: the one of its most precise range.
    /// </summary>
    /// <param name="address">Address to name; its IPv4-mapped form and the zone of a link-local address are ignored.</param>
    /// <param name="alias">The alias found, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when an alias names the address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public bool TryFind(IPAddress address, [NotNullWhen(true)] out HostAlias? alias)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_addresses.TryGetValue(AsRangeKey(address), out alias))
        {
            return true;
        }

        foreach (var subnet in _subnets)
        {
            if (subnet.Range.Contains(address))
            {
                alias = subnet;
                return true;
            }
        }

        alias = null;
        return false;
    }

    // The single-address keys are kept in the form IpAddressRange gives them: IPv4 for an IPv4-mapped address, no zone
    // for a link-local one. The address looked up is brought to that form, as IpAddressRange.Contains does.
    private static IPAddress AsRangeKey(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4();
        }

        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0
            ? new IPAddress(address.GetAddressBytes())
            : address;
    }
}
