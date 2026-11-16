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

    private readonly IReadOnlyDictionary<IPAddress, string> _aliases = HostAliasParser.Parse(options.CustomHostAliases);
    private readonly DnsAddressExclusionEvaluator _exclusions = new(options.DnsExcludedNetworks);

    public bool AllowsLookup(string ip) =>
        !IPAddress.TryParse(ip, out var address) || (!_aliases.ContainsKey(address) && !_exclusions.ShouldExclude(address));

    public IReadOnlyDictionary<string, DnsCacheEntry> WithAliases(IReadOnlyDictionary<string, DnsCacheEntry> cache)
    {
        if (_aliases.Count == 0)
        {
            return cache;
        }

        var named = new Dictionary<string, DnsCacheEntry>(cache, StringComparer.OrdinalIgnoreCase);
        foreach (var (address, hostName) in _aliases)
        {
            named[address.ToString()] = new DnsCacheEntry { HostName = hostName, Source = AliasSource };
        }

        return named;
    }
}
