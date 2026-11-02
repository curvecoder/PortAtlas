namespace PortAtlas.Core.Enrichment;

/// <summary>
/// Outcome of the online resolutions applied to the master cache.
/// </summary>
/// <param name="NewlyNamed">Entries that had no name and obtained one.</param>
/// <param name="Renamed">Entries whose name was replaced by a different one.</param>
/// <param name="Unchanged">Entries confirmed with the name they already had.</param>
/// <param name="KeptAfterFailure">Entries whose lookup found nothing: the name resolved earlier was kept.</param>
/// <param name="StillUnresolved">Entries without a name before and after the lookup.</param>
public sealed record CacheResolutionSummary(
    int NewlyNamed,
    int Renamed,
    int Unchanged,
    int KeptAfterFailure,
    int StillUnresolved);
