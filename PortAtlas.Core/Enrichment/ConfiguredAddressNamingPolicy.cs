using System.Net;
using PortAtlas.Core.Resolution;

namespace PortAtlas.Core.Enrichment;

/// <summary>
/// Applies the <c>CustomHostAliases</c> and <c>DnsExcludedNetworks</c> of the configuration to the resolve command, with
/// the meaning they have for the audit: neither kind of address is sent to a DNS or RDAP server.
/// </summary>
/// <param name="options">Enrichment options holding the aliases and the excluded networks.</param>
internal sealed class ConfiguredAddressNamingPolicy(OfflineAuditEnrichmentOptions options)
{
    // The audit never caches these names either: an alias written to the master cache would keep naming the address
    // after its removal from the configuration.
    private const string AliasSource = "Custom";

    private readonly HostAliasTable _aliases = HostAliasTable.Parse(options.CustomHostAliases);
    private readonly DnsAddressExclusionEvaluator _exclusions = new(options.DnsExcludedNetworks);

    public bool AllowsLookup(string ip) =>
        !IPAddress.TryParse(ip, out var address) || (!_aliases.TryFind(address, out _) && !_exclusions.ShouldExclude(address));

    // An alias may name a whole subnet, so its addresses cannot be listed: each address that the enrichment pass may
    // look up is named instead, under the spelling it has in the cache or in the logs. Those are the cache entries and
    // the addresses read in the logs without a name (or all of them with --force-resolve), since a log line that
    // already holds a name is not renamed otherwise.
    public IReadOnlyDictionary<string, DnsCacheEntry> WithAliases(
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        IEnumerable<string> addressesRead)
    {
        if (_aliases.IsEmpty)
        {
            return cache;
        }

        var named = new Dictionary<string, DnsCacheEntry>(cache, StringComparer.OrdinalIgnoreCase);
        foreach (var ip in cache.Keys.Concat(addressesRead))
        {
            if (IPAddress.TryParse(ip, out var address) && _aliases.TryFind(address, out var alias))
            {
                named[ip] = new DnsCacheEntry { HostName = alias.HostName, Source = AliasSource };
            }
        }

        return named;
    }
}
