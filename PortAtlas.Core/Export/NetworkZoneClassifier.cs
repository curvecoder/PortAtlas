using System.Net;
using PortAtlas.Core.Filtering;

namespace PortAtlas.Core.Export;

/// <summary>
/// Sorts the addresses of the matrices into the internal and public zones.
/// </summary>
internal static class NetworkZoneClassifier
{
    public const string Internal = "Internal";
    public const string Public = "Public";

    private static readonly IpAddressRangeMatcher PrivateNetworkMatcher = new(
    [
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "::1/128",
        "fc00::/7",
        "fe80::/10"
    ]);

    /// <summary>
    /// A loopback address and <c>0.0.0.0</c> / <c>::</c> are internal: a listener bound to the unspecified address is
    /// reached through the server's own addresses.
    /// </summary>
    public static bool IsInternal(IPAddress address) =>
        IPAddress.IsLoopback(address) || PrivateNetworkMatcher.IsMatch(address) ||
        address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);

    public static string ZoneOf(string ip) =>
        IPAddress.TryParse(ip, out var address) && IsInternal(address) ? Internal : Public;

    /// <summary>
    /// A loopback or unspecified address is the same text on every machine: it names no machine of its own.
    /// </summary>
    public static bool NamesOneMachine(IPAddress address) =>
        !IPAddress.IsLoopback(address) && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);

    public static bool NamesOneMachine(string ip) => IPAddress.TryParse(ip, out var address) && NamesOneMachine(address);
}
