using System.Diagnostics.CodeAnalysis;
using System.Net;
using PortAtlas.Core.Filtering;
using TopologyForge.Filtering;

namespace PortAtlas.Core.Export;

/// <summary>
/// One range of an address group: <c>192.168.10.0/24</c> in the group <c>lab</c>.
/// </summary>
/// <param name="GroupName">Name of the group, as written in the configuration or on the command line.</param>
/// <param name="Range">Address or CIDR subnet that belongs to the group.</param>
public sealed record AddressGroupRange(string GroupName, IpAddressRange Range);

/// <summary>
/// The address groups (VLAN, application security group…) given to <c>export rules</c>: the group of an address is the
/// one of its most precise range.
/// </summary>
public sealed class AddressGroupDirectory
{
    private readonly IpAddressRangeLookup<string> _groups;

    private AddressGroupDirectory(IpAddressRangeLookup<string> groups) => _groups = groups;

    /// <summary>
    /// A directory without any group.
    /// </summary>
    public static AddressGroupDirectory Empty { get; } = new(new IpAddressRangeLookup<string>([]));

    /// <summary>
    /// <see langword="true"/> when no group is defined.
    /// </summary>
    public bool IsEmpty => _groups.IsEmpty;

    /// <summary>
    /// Builds the directory, unless a range belongs to two groups.
    /// </summary>
    /// <param name="ranges">Ranges of every group; a range may be repeated within one group.</param>
    /// <param name="directory">The directory, when every range belongs to one group only.</param>
    /// <param name="conflict">The message naming a range given to two groups or a group name the matrix reserves,
    /// otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the directory was built.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ranges"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A range given to two groups would put the same rules in a row picked by the order of the configuration: it is
    /// refused instead. A range written in two ways (<c>192.168.10.0/24</c> and <c>192.168.10.9/24</c>) is one range.
    /// A group named <c>Any</c>, or named as an address or a subnet, is refused too: its rows could not be told apart
    /// from those of a listening socket or of a host in no group.
    /// </remarks>
    public static bool TryCreate(
        IEnumerable<AddressGroupRange> ranges,
        [NotNullWhen(true)] out AddressGroupDirectory? directory,
        [NotNullWhen(false)] out string? conflict)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var groupByRange = new Dictionary<IpAddressRange, string>();
        foreach (var (groupName, range) in ranges)
        {
            if (IsReservedName(groupName))
            {
                directory = null;
                conflict = $"'{groupName}' cannot name an address group: the group rule matrix writes {FirewallGroupRuleAggregator.AnySource} for the source of a listening socket and its address for a host in no group. Give the group another name (AddressGroups, --address-group).";
                return false;
            }

            if (groupByRange.TryGetValue(range, out var otherGroup) && !string.Equals(otherGroup, groupName, StringComparison.Ordinal))
            {
                directory = null;
                conflict = $"{range} is in two address groups, '{otherGroup}' and '{groupName}': give each address or subnet to one group only (AddressGroups, --address-group).";
                return false;
            }

            groupByRange[range] = groupName;
        }

        directory = new AddressGroupDirectory(new IpAddressRangeLookup<string>(groupByRange.Select(entry => (entry.Key, entry.Value))));
        conflict = null;
        return true;
    }

    /// <summary>
    /// Finds the group of an address.
    /// </summary>
    /// <param name="ip">Address as written in a rule.</param>
    /// <returns>The name of the group, or <see langword="null"/> when no group holds the address, the address stands for
    /// any host (<c>0.0.0.0</c>, <c>::</c>) or the text is not an address.</returns>
    public string? FindGroup(string ip)
    {
        if (!IPAddress.TryParse(ip, out var address) || IsUnspecified(address))
        {
            return null;
        }

        return _groups.TryFind(address, out var group) ? group : null;
    }

    /// <summary>
    /// Tells whether a rule address stands for any host, as the source of a listening socket does.
    /// </summary>
    /// <param name="ip">Address as written in a rule.</param>
    /// <returns><see langword="true"/> for <c>0.0.0.0</c>, <c>::</c> and <c>::ffff:0.0.0.0</c>.</returns>
    public static bool IsUnspecified(string ip) => IPAddress.TryParse(ip, out var address) && IsUnspecified(address);

    // The source of a listening socket is written 0.0.0.0 or ::, which stands for any peer: it is no host of a group,
    // even of a group covering 0.0.0.0/0.
    private static bool IsUnspecified(IPAddress address)
    {
        var canonical = IpAddressCanonicalForm.Of(address);
        return canonical.Equals(IPAddress.Any) || canonical.Equals(IPAddress.IPv6Any);
    }

    private static bool IsReservedName(string groupName) =>
        string.Equals(groupName, FirewallGroupRuleAggregator.AnySource, StringComparison.OrdinalIgnoreCase)
        || IpAddressRange.TryParse(groupName, out _);
}
