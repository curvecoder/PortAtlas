using System.Net;

namespace PortAtlas.Core.Providers;

/// <summary>
/// State of a local network interface at the time it was read.
/// </summary>
/// <param name="IsUp">True when the interface is operational.</param>
/// <param name="IsLoopback">True for the loopback interface.</param>
/// <param name="UnicastAddresses">Addresses bound to the interface, IPv4 and IPv6.</param>
/// <param name="HasIPv4Gateway">True when the interface has an IPv4 gateway, that is a route out of the local network.</param>
public sealed record HostInterfaceSnapshot(
    bool IsUp,
    bool IsLoopback,
    IReadOnlyList<IPAddress> UnicastAddresses,
    bool HasIPv4Gateway);
