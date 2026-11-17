using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace PortAtlas.Cli.CommandLine;

/// <summary>
/// One <c>--alias</c> value: an IP address and the host name shown for it instead of reverse DNS.
/// </summary>
/// <param name="Ip">IP address, as written by the operator.</param>
/// <param name="Host">Host name shown for the address.</param>
internal sealed record HostAliasArgument(string Ip, string Host)
{
    /// <summary>
    /// Reads <c>ip=host</c>, or <c>ip:host</c> for an IPv4 address; an IPv4 address is written with its four parts.
    /// </summary>
    /// <param name="pair">Value given to <c>--alias</c>.</param>
    /// <param name="alias">The address and the name, when <paramref name="pair"/> holds both.</param>
    /// <param name="error">Why <paramref name="pair"/> was refused, naming it, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="pair"/> names an IP address and a host name.</returns>
    public static bool TryParse(string pair, [NotNullWhen(true)] out HostAliasArgument? alias, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(pair);

        // An IPv6 address holds colons itself, so '=' is looked for first and ':' only separates an IPv4 address.
        var separatorIndex = pair.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            separatorIndex = pair.IndexOf(':', StringComparison.Ordinal);
        }

        var ip = separatorIndex < 0 ? string.Empty : pair[..separatorIndex].Trim();
        var host = separatorIndex < 0 ? string.Empty : pair[(separatorIndex + 1)..].Trim();
        alias = null;
        if (ip.Length == 0 || host.Length == 0 || !IsWrittenInFull(ip))
        {
            error = $"'{pair}' is not accepted by --alias: write ip=host, for example 192.168.1.20=nas.home.arpa or fe80::10=nas.home.arpa.";
            return false;
        }

        alias = new HostAliasArgument(ip, host);
        error = null;
        return true;
    }

    // IPAddress.TryParse reads "10" or "2001" as a shortened IPv4 address: requiring four dotted parts keeps "10:nas" or
    // "2001:db8::1" from being read as an alias of 0.0.0.10 or 0.0.7.209.
    private static bool IsWrittenInFull(string ip) =>
        IPAddress.TryParse(ip, out var address)
        && (address.AddressFamily != AddressFamily.InterNetwork || ip.Count(c => c == '.') == 3);
}
