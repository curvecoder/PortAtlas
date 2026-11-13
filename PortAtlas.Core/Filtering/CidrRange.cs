using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace PortAtlas.Core.Filtering;

/// <summary>
/// An IP address or a CIDR subnet given as a filter, read once and kept in its network form: a subnet written with
/// the address of one of its hosts (<c>192.168.1.10/24</c>) is the subnet of that host (<c>192.168.1.0/24</c>).
/// </summary>
public sealed record CidrRange
{
    private readonly IPNetwork _network;

    private CidrRange(IPNetwork network) => _network = network;

    /// <summary>
    /// First address of the range, host bits cleared.
    /// </summary>
    public IPAddress NetworkAddress => _network.BaseAddress;

    /// <summary>
    /// Number of leading bits shared by every address of the range.
    /// </summary>
    public int PrefixLength => _network.PrefixLength;

    /// <summary>
    /// <see langword="true"/> when the range holds one address only.
    /// </summary>
    public bool IsSingleAddress => PrefixLength == BitLengthOf(NetworkAddress);

    /// <summary>
    /// Reads an address (<c>192.168.1.50</c>) or a CIDR subnet (<c>192.168.1.0/24</c>, <c>fd00::/8</c>).
    /// </summary>
    /// <param name="text">Text to read; surrounding spaces are ignored.</param>
    /// <returns>The range, host bits cleared.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is not an address, or its prefix length is not a number between 0 and the bit length of the address.</exception>
    public static CidrRange Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TryParse(text, out var range)
            ? range
            : throw new FormatException($"'{text}' is not an IP address or a CIDR subnet (expected 192.168.1.50 or 192.168.1.0/24).");
    }

    /// <summary>
    /// Reads an address or a CIDR subnet without throwing.
    /// </summary>
    /// <param name="text">Text to read; surrounding spaces are ignored.</param>
    /// <param name="range">The range, host bits cleared, when the text is readable.</param>
    /// <returns><see langword="true"/> when the text is an address or a CIDR subnet.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out CidrRange? range)
    {
        range = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        var slashIndex = trimmed.IndexOf('/', StringComparison.Ordinal);
        var addressText = slashIndex < 0 ? trimmed : trimmed[..slashIndex];
        if (!IPAddress.TryParse(addressText, out var address) || IsShortenedIPv4(address, addressText))
        {
            return false;
        }

        var prefixLength = BitLengthOf(address);
        if (slashIndex >= 0 && !TryReadPrefixLength(trimmed[(slashIndex + 1)..], prefixLength, out prefixLength))
        {
            return false;
        }

        range = new CidrRange(new IPNetwork(ClearHostBits(address, prefixLength), prefixLength));
        return true;
    }

    /// <summary>
    /// Tells whether an address belongs to the range.
    /// </summary>
    /// <param name="address">Address to test.</param>
    /// <returns><see langword="true"/> when the address is in the range.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public bool Contains(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_network.Contains(address))
        {
            return true;
        }

        // A dual-stack socket reports an IPv4 peer in its IPv4-mapped IPv6 form (::ffff:192.168.1.50): both forms name
        // the same host and must meet the same rule, whichever form the rule was written in.
        if (address.IsIPv4MappedToIPv6)
        {
            return _network.Contains(address.MapToIPv4());
        }

        return address.AddressFamily == AddressFamily.InterNetwork && _network.Contains(address.MapToIPv6());
    }

    /// <summary>
    /// Formats the range as it is read back: the address alone for a single address, <c>network/prefix</c> otherwise.
    /// </summary>
    /// <returns>The text form of the range.</returns>
    public override string ToString() =>
        IsSingleAddress
            ? NetworkAddress.ToString()
            : $"{NetworkAddress}/{PrefixLength.ToString(CultureInfo.InvariantCulture)}";

    private static int BitLengthOf(IPAddress address) => address.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;

    // The address parser reads IPv4 as inet_aton does: "443" is 0.0.1.187, "192.168.1" is 192.168.0.1 and a leading
    // zero or "0x" switches a byte to octal or hexadecimal. A filter written that way is a typing mistake (a port in
    // an address filter, a subnet missing a byte) and would silently name another address: only four decimal bytes
    // are read, which is what the parsed address formats back to.
    private static bool IsShortenedIPv4(IPAddress address, string addressText) =>
        address.AddressFamily == AddressFamily.InterNetwork &&
        !string.Equals(address.ToString(), addressText, StringComparison.Ordinal);

    private static bool TryReadPrefixLength(string text, int bitLength, out int prefixLength) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out prefixLength) && prefixLength <= bitLength;

    private static IPAddress ClearHostBits(IPAddress address, int prefixLength)
    {
        var bytes = address.GetAddressBytes();
        var firstHostByte = prefixLength / 8;
        var networkBitsInThatByte = prefixLength % 8;

        if (networkBitsInThatByte > 0)
        {
            bytes[firstHostByte] &= (byte)(0xFF << (8 - networkBitsInThatByte));
            firstHostByte++;
        }

        Array.Clear(bytes, firstHostByte, bytes.Length - firstHostByte);
        return new IPAddress(bytes);
    }
}
