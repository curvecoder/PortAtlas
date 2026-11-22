using System.Net;
using System.Net.Sockets;
using TopologyForge.Filtering;

namespace PortAtlas.Core.Filtering;

/// <summary>
/// Orders the texts of the export matrices that may hold IP addresses: addresses by value (IPv4 first, then IPv6), so
/// that <c>192.168.1.2</c> comes before <c>192.168.1.10</c>, then the other texts (group names, <c>Any</c>) as text.
/// </summary>
/// <remarks>
/// An address is read as <see cref="IpAddressRange"/> reads a filter, four decimal bytes for IPv4: a group named
/// <c>10</c> stays a name, where <see cref="IPAddress.TryParse(string?, out IPAddress?)"/> would read 0.0.0.10. Two
/// forms of one host (<c>::ffff:10.0.0.5</c> and <c>10.0.0.5</c>) have the same value; the text comparer then decides,
/// which keeps the order total and the row numbers stable.
/// </remarks>
internal sealed class IpAddressTextComparer(StringComparer textComparer) : IComparer<string>
{
    public static IpAddressTextComparer Ordinal { get; } = new(StringComparer.Ordinal);

    public static IpAddressTextComparer OrdinalIgnoreCase { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return textComparer.Compare(x, y);
        }

        var xAddress = ReadAddress(x);
        var yAddress = ReadAddress(y);
        var byValue = (xAddress, yAddress) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            _ => CompareValues(xAddress, yAddress)
        };

        return byValue != 0 ? byValue : textComparer.Compare(x, y);
    }

    private static IPAddress? ReadAddress(string text) =>
        !text.Contains('/', StringComparison.Ordinal) && IpAddressRange.TryParse(text, out var range) && range.IsSingleAddress
            ? range.NetworkAddress
            : null;

    private static int CompareValues(IPAddress x, IPAddress y)
    {
        if (x.AddressFamily != y.AddressFamily)
        {
            return x.AddressFamily == AddressFamily.InterNetwork ? -1 : 1;
        }

        return x.GetAddressBytes().AsSpan().SequenceCompareTo(y.GetAddressBytes());
    }
}
