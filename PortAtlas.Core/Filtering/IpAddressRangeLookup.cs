using System.Diagnostics.CodeAnalysis;
using System.Net;
using TopologyForge.Filtering;

namespace PortAtlas.Core.Filtering;

/// <summary>
/// Values attached to IP addresses and CIDR subnets, looked up by address: an address in several ranges gets the value
/// of the most precise one.
/// </summary>
/// <typeparam name="T">Value attached to each range.</typeparam>
public sealed class IpAddressRangeLookup<T>
    where T : class
{
    // Single addresses are looked up by key, since a configuration may hold thousands of them; subnets are few and kept
    // from the narrowest to the widest, so that the first one holding an address is the most precise.
    private readonly Dictionary<IPAddress, T> _addresses;
    private readonly List<(IpAddressRange Range, T Value)> _subnets;

    /// <summary>
    /// Builds the lookup.
    /// </summary>
    /// <param name="entries">Ranges and their values; a range given twice keeps the value given last.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is <see langword="null"/>.</exception>
    public IpAddressRangeLookup(IEnumerable<(IpAddressRange Range, T Value)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var byRange = new Dictionary<IpAddressRange, T>();
        foreach (var (range, value) in entries)
        {
            byRange[range] = value;
        }

        _addresses = byRange
            .Where(entry => entry.Key.IsSingleAddress)
            .ToDictionary(entry => entry.Key.NetworkAddress, entry => entry.Value);
        _subnets = byRange
            .Where(entry => !entry.Key.IsSingleAddress)
            .OrderByDescending(entry => entry.Key.PrefixLength)
            .Select(entry => (entry.Key, entry.Value))
            .ToList();
    }

    /// <summary>
    /// <see langword="true"/> when no range is held.
    /// </summary>
    public bool IsEmpty => _addresses.Count == 0 && _subnets.Count == 0;

    /// <summary>
    /// Finds the value of the most precise range holding an address.
    /// </summary>
    /// <param name="address">Address to look up; its IPv4-mapped form and the zone of a link-local address are ignored.</param>
    /// <param name="value">The value found, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a range holds the address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public bool TryFind(IPAddress address, [NotNullWhen(true)] out T? value)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_addresses.TryGetValue(IpAddressCanonicalForm.Of(address), out value))
        {
            return true;
        }

        foreach (var (range, subnetValue) in _subnets)
        {
            if (range.Contains(address))
            {
                value = subnetValue;
                return true;
            }
        }

        value = null;
        return false;
    }
}
