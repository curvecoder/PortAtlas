using PortAtlas.Core.Resolution;

namespace PortAtlas.Core.Enrichment;

/// <summary>
/// Counters and cache reports produced at the end of an offline audit enrichment.
/// </summary>
/// <param name="ProcessedAuditFiles">List of original audit files processed.</param>
/// <param name="GeneratedAuditFiles">List of generated or modified audit files.</param>
/// <param name="TotalRecordsScanned">Gross number of connection rows/records examined.</param>
/// <param name="TotalRecordsUpdated">Number of rows where the remote hostname was updated.</param>
/// <param name="DistinctIpsEnriched">Number of unique IP addresses enriched.</param>
/// <param name="OnlineResolutionsCount">Number of real-time online DNS/RDAP queries performed.</param>
/// <param name="CacheReport">Cache consolidation and conflict analysis report.</param>
/// <param name="Resolution">Outcome of the online resolutions on the master cache.</param>
public sealed record OfflineAuditEnrichmentReport(
    IReadOnlyList<string> ProcessedAuditFiles,
    IReadOnlyList<string> GeneratedAuditFiles,
    int TotalRecordsScanned,
    int TotalRecordsUpdated,
    int DistinctIpsEnriched,
    int OnlineResolutionsCount,
    DnsCacheConsolidationReport CacheReport,
    CacheResolutionSummary? Resolution = null);
