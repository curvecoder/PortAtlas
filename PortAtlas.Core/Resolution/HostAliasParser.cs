using System.Net;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Reads the <c>CustomHostAliases</c> of the configuration into fixed host names by IP address.
/// </summary>
public static class HostAliasParser
{
    /// <summary>
    /// Reads the aliases as the audit, <c>resolve</c> and <c>lookup</c> apply them.
    /// </summary>
    /// <param name="customHostAliases">Host names by IP address, as written in the configuration.</param>
    /// <returns>The trimmed host names by parsed address; empty when <paramref name="customHostAliases"/> is null.</returns>
    /// <remarks>
    /// A key that is not an IP address, or an empty name, cannot name anything: it is left out rather than refused, so
    /// that one wrong line of the configuration file does not stop the audit or the resolve command.
    /// </remarks>
    public static IReadOnlyDictionary<IPAddress, string> Parse(IReadOnlyDictionary<string, string>? customHostAliases)
    {
        var aliases = new Dictionary<IPAddress, string>();
        if (customHostAliases is null)
        {
            return aliases;
        }

        foreach (var (ipStr, hostName) in customHostAliases)
        {
            if (IPAddress.TryParse(ipStr, out var address) && !string.IsNullOrWhiteSpace(hostName))
            {
                aliases[address] = hostName.Trim();
            }
        }

        return aliases;
    }
}
