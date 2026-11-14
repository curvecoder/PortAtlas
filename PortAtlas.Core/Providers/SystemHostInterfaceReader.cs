using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace PortAtlas.Core.Providers;

/// <summary>
/// Reads the network interfaces of the local host from the operating system.
/// </summary>
public sealed class SystemHostInterfaceReader : IHostInterfaceReader
{
    /// <inheritdoc />
    public IReadOnlyList<HostInterfaceSnapshot> ReadInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Select(ToSnapshot).ToList();
        }
        catch (NetworkInformationException)
        {
            // An account or container that may not list its interfaces still runs the audit: the host is then known
            // by its loopback address only, which the provider falls back to.
            return [];
        }
    }

    private static HostInterfaceSnapshot ToSnapshot(NetworkInterface networkInterface)
    {
        var properties = networkInterface.GetIPProperties();
        return new HostInterfaceSnapshot(
            networkInterface.OperationalStatus == OperationalStatus.Up,
            networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback,
            properties.UnicastAddresses.Select(unicast => unicast.Address).ToList(),
            properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork && !gateway.Address.Equals(IPAddress.Any)));
    }
}
