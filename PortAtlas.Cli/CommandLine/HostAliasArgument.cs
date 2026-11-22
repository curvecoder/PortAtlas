using System.Diagnostics.CodeAnalysis;
using TopologyForge.Filtering;

namespace PortAtlas.Cli.CommandLine;

/// <summary>
/// One <c>--alias</c> value: an IP address or a CIDR subnet and the host name shown for its addresses instead of
/// reverse DNS.
/// </summary>
/// <param name="Range">IP address or CIDR subnet, as written by the operator.</param>
/// <param name="Host">Host name shown for the addresses of the range.</param>
internal sealed record HostAliasArgument(string Range, string Host)
{
    /// <summary>
    /// Reads <c>range=host</c>, or <c>range:host</c> for an IPv4 range; an IPv4 address is written with its four parts.
    /// </summary>
    /// <param name="pair">Value given to <c>--alias</c>.</param>
    /// <param name="alias">The range and the name, when <paramref name="pair"/> holds both.</param>
    /// <param name="error">Why <paramref name="pair"/> was refused, naming it, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="pair"/> names an IP address or a subnet and a host name.</returns>
    public static bool TryParse(string pair, [NotNullWhen(true)] out HostAliasArgument? alias, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(pair);

        // An IPv6 address holds colons itself, so '=' is looked for first and ':' only separates an IPv4 range.
        var separatorIndex = pair.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            separatorIndex = pair.IndexOf(':', StringComparison.Ordinal);
        }

        var range = separatorIndex < 0 ? string.Empty : pair[..separatorIndex].Trim();
        var host = separatorIndex < 0 ? string.Empty : pair[(separatorIndex + 1)..].Trim();
        alias = null;

        // IpAddressRange requires the four parts of an IPv4 address: "10:nas" or "2001:db8::1" cannot be read as an
        // alias of 0.0.0.10 or 0.0.7.209.
        if (range.Length == 0 || host.Length == 0 || !IpAddressRange.TryParse(range, out _))
        {
            error = $"'{pair}' is not accepted by --alias: write address=host or subnet=host, for example 192.168.1.20=nas.home.arpa, 192.168.10.0/24=lab.home.arpa or fe80::10=nas.home.arpa.";
            return false;
        }

        alias = new HostAliasArgument(range, host);
        error = null;
        return true;
    }
}
