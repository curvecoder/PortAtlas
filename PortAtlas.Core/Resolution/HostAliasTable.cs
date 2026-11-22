using System.Diagnostics.CodeAnalysis;
using System.Net;
using PortAtlas.Core.Filtering;
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
    private readonly IpAddressRangeLookup<HostAlias> _aliases;

    private HostAliasTable(IpAddressRangeLookup<HostAlias> aliases) => _aliases = aliases;

    /// <summary>
    /// <see langword="true"/> when no alias is configured.
    /// </summary>
    public bool IsEmpty => _aliases.IsEmpty;

    /// <summary>
    /// Reads the aliases of the configuration.
    /// </summary>
    /// <param name="customHostAliases">Host names by IP address or CIDR subnet, as written in the configuration.</param>
    /// <returns>The table; empty when <paramref name="customHostAliases"/> is null.</returns>
    /// <remarks>
    /// A key that is neither an address nor a subnet, or an empty name, cannot name anything: it is left out rather than
    /// refused, so that one wrong line of the configuration file does not stop the audit or the resolve command. Two
    /// keys naming the same range (<c>192.168.10.0/24</c> and <c>192.168.10.9/24</c>) keep the name read last.
    /// </remarks>
    public static HostAliasTable Parse(IReadOnlyDictionary<string, string>? customHostAliases)
    {
        var aliases = new List<(IpAddressRange Range, HostAlias Value)>();
        foreach (var (key, hostName) in customHostAliases ?? new Dictionary<string, string>())
        {
            if (IpAddressRange.TryParse(key, out var range) && !string.IsNullOrWhiteSpace(hostName))
            {
                aliases.Add((range, new HostAlias(range, hostName.Trim())));
            }
        }

        return new HostAliasTable(new IpAddressRangeLookup<HostAlias>(aliases));
    }

    /// <summary>
    /// Finds the alias of an address: the one of its most precise range.
    /// </summary>
    /// <param name="address">Address to name; its IPv4-mapped form and the zone of a link-local address are ignored.</param>
    /// <param name="alias">The alias found, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when an alias names the address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public bool TryFind(IPAddress address, [NotNullWhen(true)] out HostAlias? alias) => _aliases.TryFind(address, out alias);
}
