using PortAtlas.Cli.Terminal;
using PortAtlas.Core.Enrichment;
using PortAtlas.Core.Resolution;

namespace PortAtlas.Cli.Enrichment;

/// <summary>
/// Runs the <c>resolve</c> command: consolidates DNS caches, enriches audit logs and reports the outcome on the console.
/// </summary>
/// <param name="enricher">Enricher orchestrating offline analysis and file updates.</param>
/// <param name="dnsResolutionTimeout">Reverse DNS timeout of the default enricher (<c>DnsResolutionTimeoutMs</c>); ignored when <paramref name="enricher"/> is given.</param>
public sealed class OfflineResolutionCommandRunner(OfflineAuditEnricher? enricher = null, TimeSpan? dnsResolutionTimeout = null)
{
    // 128 + SIGINT, the exit code shells report for a command stopped by Ctrl+C.
    private const int InterruptedExitCode = 130;

    private readonly OfflineAuditEnricher _enricher = enricher ?? new OfflineAuditEnricher(
        offlineResolver: new OfflineDnsResolver(
            organizationResolver: new RdapIpOrganizationResolver(),
            resolutionTimeout: dnsResolutionTimeout,
            logThrottlingNotice: Console.WriteLine),
        logger: Console.WriteLine);

    /// <summary>
    /// Consolidates the caches and enriches the audit logs.
    /// </summary>
    /// <param name="auditFiles">Audit log files (CSV or NDJSON) to enrich.</param>
    /// <param name="cacheFiles">DNS cache files to merge into the master cache.</param>
    /// <param name="options">Enrichment options, including the master cache path.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Exit code: 0 on success, 130 when stopped by Ctrl+C or SIGTERM (the master cache is saved first).</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public async Task<int> RunAsync(
        IReadOnlyList<string> auditFiles,
        IReadOnlyList<string> cacheFiles,
        OfflineAuditEnrichmentOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditFiles);
        ArgumentNullException.ThrowIfNull(cacheFiles);
        ArgumentNullException.ThrowIfNull(options);

        Console.WriteLine("========================================================================");
        Console.WriteLine("  PortAtlas - Offline DNS Resolution & Cache Consolidation (resolve)");
        Console.WriteLine("========================================================================");
        Console.WriteLine($"Execution mode: {(options.Isolated ? "Cache only, no DNS or RDAP query (--cache-only)" : "Active enrichment with DNS & RDAP")}");
        Console.WriteLine(auditFiles.Count == 0
            ? "Audit log updates: none (cache only, no audit log given)"
            : $"Audit log updates: {(options.InPlace ? "Direct in-place overwrite (--in-place)" : "Generate new '*_enriched' files")}");
        Console.WriteLine($"Resolution scope: {(options.ForceResolve ? "Every address, names already known are kept when a lookup fails (--force-resolve)" : "Addresses without a name only")}");
        Console.WriteLine();

        using var interruption = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var terminationSignals = new TerminationSignalListener(interruption);

        OfflineAuditEnrichmentReport report;
        try
        {
            report = await _enricher.EnrichPathsAsync(auditFiles, cacheFiles, options, interruption.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (interruption.IsCancellationRequested)
        {
            // Ctrl+C or SIGTERM (or a cancellation by the caller, handled the same way) is the documented way to stop a
            // long resolve: the enricher has already saved the addresses resolved before the stop, so the stop is
            // reported and ends the command with the usual exit code 130.
            Console.WriteLine();
            Console.WriteLine($"Interrupted: the addresses resolved before the stop are saved to {Path.GetFullPath(options.OutputCachePath)}; the audit logs not processed yet are left as they were. Run resolve again to finish.");
            return InterruptedExitCode;
        }

        if (report.CacheReport.Conflicts.Count > 0)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[WARNING] {report.CacheReport.Conflicts.Count} conflict(s) detected during cache merge:");
            foreach (var conflict in report.CacheReport.Conflicts)
            {
                Console.WriteLine($"  * IP {conflict.IpAddress}: Retained '{conflict.WinningHostName}', discarded '{conflict.ConflictingHostName}' ({conflict.Reason})");
            }
            Console.ResetColor();
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine("------------------------------------------------------------------------");
        Console.WriteLine("  Offline Consolidation & Enrichment Summary");
        Console.WriteLine("------------------------------------------------------------------------");
        Console.WriteLine($"  * Source caches processed: {report.CacheReport.TotalCachesProcessed} ({report.CacheReport.TotalEntriesLoaded} raw entries)");
        Console.WriteLine($"  * Deduplicated cache entries: {report.CacheReport.MergedEntries.Count}");
        Console.WriteLine($"  * Online resolutions completed: {report.OnlineResolutionsCount}");
        if (report.Resolution is { } resolution && report.OnlineResolutionsCount > 0)
        {
            Console.WriteLine($"      newly named: {resolution.NewlyNamed}, renamed: {resolution.Renamed}, confirmed: {resolution.Unchanged}, kept after a failed lookup: {resolution.KeptAfterFailure}, still unresolved: {resolution.StillUnresolved}");
        }
        Console.WriteLine($"  * Audit files processed: {report.ProcessedAuditFiles.Count}");
        Console.WriteLine($"  * Audit records scanned: {report.TotalRecordsScanned}");
        Console.WriteLine($"  * Audit records updated: {report.TotalRecordsUpdated}");
        Console.WriteLine($"  * Distinct IP addresses enriched: {report.DistinctIpsEnriched}");
        Console.WriteLine($"  * Master cache saved to: {Path.GetFullPath(options.OutputCachePath)}");

        if (report.GeneratedAuditFiles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Generated enriched audit file(s):");
            foreach (var file in report.GeneratedAuditFiles)
            {
                Console.WriteLine($"    -> {Path.GetFullPath(file)}");
            }
        }
        Console.WriteLine("------------------------------------------------------------------------");

        return 0;
    }
}
