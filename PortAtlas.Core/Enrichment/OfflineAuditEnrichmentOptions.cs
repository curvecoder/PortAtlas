namespace PortAtlas.Core.Enrichment;

/// <summary>
/// Configuration options governing offline audit enrichment and consolidation.
/// </summary>
public sealed record OfflineAuditEnrichmentOptions
{
    /// <summary>
    /// When true, overwrites original audit files in-place; otherwise produces files suffixed with '_enriched'.
    /// </summary>
    public bool InPlace { get; init; } = false;

    /// <summary>
    /// When true, forcibly overwrites existing remote host names even if already populated.
    /// </summary>
    public bool ForceResolve { get; init; } = false;

    /// <summary>
    /// When true, strictly restricts resolution to provided cache files without making external DNS or RDAP queries.
    /// </summary>
    public bool Isolated { get; init; } = false;

    /// <summary>
    /// Throttle delay applied between successive live RDAP requests to respect Internet registry rate limits.
    /// </summary>
    public TimeSpan RdapThrottleDelay { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Networks (CIDR ranges or single addresses) that are never sent to a DNS or RDAP server, as for the audit's
    /// <c>DnsExcludedNetworks</c>. Their host names may still come from the merged caches.
    /// </summary>
    public IReadOnlyList<string> DnsExcludedNetworks { get; init; } = [];

    /// <summary>
    /// Fixed host names by IP address, as for the audit's <c>CustomHostAliases</c>: an aliased address is never resolved,
    /// its alias names it in the audit logs (under the same rule as a resolved name: it fills a missing name, and replaces
    /// a known one only with <see cref="ForceResolve"/>), and the alias is not written to the master cache.
    /// </summary>
    public IReadOnlyDictionary<string, string> CustomHostAliases { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// Target file path where the consolidated master DNS cache should be written.
    /// </summary>
    public string OutputCachePath { get; init; } = "netflow_dns_cache.json";
}
