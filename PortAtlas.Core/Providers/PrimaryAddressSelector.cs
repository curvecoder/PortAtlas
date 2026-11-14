using System.Net;
using System.Net.Sockets;

namespace PortAtlas.Core.Providers;

/// <summary>
/// Chooses the address the local host is known by on the network.
/// </summary>
public static class PrimaryAddressSelector
{
    private const string LoopbackAddress = "127.0.0.1";

    /// <summary>
    /// Returns the first IPv4 address of an operational, non-loopback interface, preferring an interface with a gateway.
    /// </summary>
    /// <param name="interfaces">Interfaces of the local host, in the order the operating system lists them.</param>
    /// <returns>The chosen address, or <c>127.0.0.1</c> when no interface has an IPv4 address.</returns>
    public static string SelectPrimaryAddress(IReadOnlyList<HostInterfaceSnapshot> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);

        var operational = interfaces.Where(candidate => candidate.IsUp && !candidate.IsLoopback).ToList();

        // Hyper-V and WSL virtual switches, and host-only adapters, are often listed first but have no gateway:
        // the interface that routes out of the local network is the one the other hosts see.
        var address = FindFirstIPv4Address(operational.Where(candidate => candidate.HasIPv4Gateway))
            ?? FindFirstIPv4Address(operational);

        return address?.ToString() ?? LoopbackAddress;
    }

    private static IPAddress? FindFirstIPv4Address(IEnumerable<HostInterfaceSnapshot> candidates)
        => candidates.Select(FindIPv4Address).FirstOrDefault(found => found is not null);

    private static IPAddress? FindIPv4Address(HostInterfaceSnapshot hostInterface)
        => hostInterface.UnicastAddresses.FirstOrDefault(address =>
            address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address));
}
