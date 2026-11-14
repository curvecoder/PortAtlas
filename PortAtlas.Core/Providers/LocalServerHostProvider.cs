using System.Net;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Providers;

/// <summary>
/// Provides host machine metadata and network interface IP addresses for the local runtime environment.
/// </summary>
/// <param name="interfaceReader">Reader of the local network interfaces.</param>
/// <param name="timeProvider">Clock that dates each reading of the interfaces.</param>
public sealed class LocalServerHostProvider(IHostInterfaceReader interfaceReader, TimeProvider timeProvider) : IServerHostProvider
{
    // A DHCP lease, a VPN connection or a virtual switch changes the addresses during a long audit. Listing every
    // interface at each scan (every 5 s by default) costs more than such a change is worth, so they are read once
    // a minute.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private HostNetworkState? _state;

    /// <summary>
    /// Initializes a provider that reads the interfaces from the operating system.
    /// </summary>
    public LocalServerHostProvider()
        : this(new SystemHostInterfaceReader(), TimeProvider.System)
    {
    }

    /// <summary>
    /// Retrieves identification and environment metadata of the local server.
    /// </summary>
    public ServerHostMetadata GetCurrentHostMetadata() => ReadState().Metadata;

    /// <summary>
    /// Retrieves all IP addresses bound to active local network interfaces.
    /// </summary>
    public IReadOnlySet<IPAddress> GetLocalIpAddresses() => ReadState().Addresses;

    private HostNetworkState ReadState()
    {
        var now = timeProvider.GetUtcNow();
        var state = Volatile.Read(ref _state);
        if (state is not null && now - state.ReadAtUtc < RefreshInterval)
        {
            return state;
        }

        // The scan loop and the dashboard may both find the state stale and read the interfaces at the same time:
        // each reading is complete, and the last one written is kept.
        var interfaces = interfaceReader.ReadInterfaces();
        if (interfaces.Count == 0 && state is not null)
        {
            // An empty reading means the operating system refused to list the interfaces this time; dropping the
            // known addresses would stop the local-flow filter and show 127.0.0.1 until the next reading.
            state = state with { ReadAtUtc = now };
            Volatile.Write(ref _state, state);
            return state;
        }

        state = new HostNetworkState(
            new ServerHostMetadata(
                Environment.MachineName,
                Environment.OSVersion.VersionString,
                PrimaryAddressSelector.SelectPrimaryAddress(interfaces)),
            CollectAddresses(interfaces),
            now);
        Volatile.Write(ref _state, state);
        return state;
    }

    private static HashSet<IPAddress> CollectAddresses(IReadOnlyList<HostInterfaceSnapshot> interfaces)
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };
        foreach (var hostInterface in interfaces.Where(candidate => candidate.IsUp))
        {
            addresses.UnionWith(hostInterface.UnicastAddresses);
        }

        return addresses;
    }

    private sealed record HostNetworkState(ServerHostMetadata Metadata, IReadOnlySet<IPAddress> Addresses, DateTimeOffset ReadAtUtc);
}
