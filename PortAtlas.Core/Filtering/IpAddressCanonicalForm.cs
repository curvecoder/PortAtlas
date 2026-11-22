using System.Net;
using System.Net.Sockets;

namespace PortAtlas.Core.Filtering;

/// <summary>
/// Brings an address to the form <c>IpAddressRange</c> keeps: IPv4 for an IPv4-mapped address, no zone for a
/// link-local one.
/// </summary>
/// <remarks>
/// TopologyForge keeps the same normalization internal; one host seen as <c>::ffff:10.0.0.5</c> and <c>10.0.0.5</c>,
/// or as <c>fe80::1%3</c> and <c>fe80::1</c>, must give one key.
/// </remarks>
internal static class IpAddressCanonicalForm
{
    public static IPAddress Of(IPAddress address)
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
