using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileForge.IO;
using PortAtlas.Core.Export;
using PortAtlas.Core.Resolution;

namespace PortAtlas.Core.Enrichment;

/// <summary>
/// Orchestrates offline DNS cache consolidation and historical audit enrichment for air-gapped environments.
/// </summary>
/// <param name="consolidator">Consolidator responsible for merging multi-source DNS caches.</param>
/// <param name="offlineResolver">Resolver for executing throttled online DNS and RDAP resolution.</param>
/// <param name="logger">Optional progress reporting callback.</param>
public sealed class OfflineAuditEnricher(
    DnsCacheConsolidator? consolidator = null,
    IOfflineIpResolver? offlineResolver = null,
    Action<string>? logger = null)
{
    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    private readonly DnsCacheConsolidator _consolidator = consolidator ?? new DnsCacheConsolidator();
    private readonly IOfflineIpResolver _offlineResolver = offlineResolver ?? new OfflineDnsResolver();
    private readonly Action<string>? _logger = logger;

    /// <summary>
    /// Enriches audit files using in-memory or loaded DNS caches.
    /// </summary>
    /// <param name="auditFiles">Collection of CSV or NDJSON audit file paths to enrich.</param>
    /// <param name="initialCaches">Collection of pre-loaded DNS cache dictionaries.</param>
    /// <param name="options">Enrichment and output options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The outcome report: processed and generated file paths, and counters.</returns>
    public async Task<OfflineAuditEnrichmentReport> EnrichFilesAsync(
        IReadOnlyList<string> auditFiles,
        IReadOnlyList<IReadOnlyDictionary<string, DnsCacheEntry>> initialCaches,
        OfflineAuditEnrichmentOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditFiles);
        ArgumentNullException.ThrowIfNull(initialCaches);
        ArgumentNullException.ThrowIfNull(options);

        // 1. Consolidate input caches
        var cacheReport = _consolidator.Consolidate(initialCaches);
        var activeCache = new Dictionary<string, DnsCacheEntry>(cacheReport.MergedEntries, StringComparer.OrdinalIgnoreCase);

        // 2. Scan audit files to identify missing IP addresses requiring resolution
        var distinctUnresolvedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var auditFile in auditFiles)
        {
            if (!File.Exists(auditFile))
            {
                continue;
            }

            var ips = await ExtractIpsRequiringResolutionAsync(auditFile, activeCache, options.ForceResolve, cancellationToken).ConfigureAwait(false);
            foreach (var ip in ips)
            {
                distinctUnresolvedIps.Add(ip);
            }
        }

        // Without audit file the cache itself is the list of addresses to resolve.
        if (auditFiles.Count == 0)
        {
            foreach (var ip in activeCache.Keys.Where(ip => !IsUnspecifiedAddress(ip) && (options.ForceResolve || !HasHostName(activeCache, ip))))
            {
                distinctUnresolvedIps.Add(ip);
            }
        }

        // 3. Online resolution of missing IPs if not isolated
        var onlineCount = 0;
        var outcomes = new ResolutionOutcomeCounter();
        try
        {
            if (!options.Isolated && distinctUnresolvedIps.Count > 0)
            {
                onlineCount = await ResolveUnknownAddressesAsync(distinctUnresolvedIps, activeCache, options.ForceResolve, outcomes, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            // A run stopped during the lookups (Ctrl+C, or a lookup that fails unexpectedly) keeps the addresses
            // resolved so far: the cache is saved before the exception goes on, and the next run resumes from it.
            await SaveOutputCacheAsync(activeCache, options.OutputCachePath).ConfigureAwait(false);
        }

        // 4. Enrich audit files
        var processedFiles = new List<string>();
        var generatedFiles = new List<string>();
        var totalScanned = 0;
        var totalUpdated = 0;
        var enrichedIps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var auditFile in auditFiles)
        {
            if (!File.Exists(auditFile))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            processedFiles.Add(auditFile);

            var (scanned, updated, outputPath, ipsUpdated) = await EnrichSingleAuditFileAsync(
                auditFile,
                activeCache,
                options,
                cancellationToken).ConfigureAwait(false);

            totalScanned += scanned;
            totalUpdated += updated;
            generatedFiles.Add(outputPath);

            foreach (var ip in ipsUpdated)
            {
                enrichedIps.Add(ip);
            }
        }

        return new OfflineAuditEnrichmentReport(
            processedFiles,
            generatedFiles,
            totalScanned,
            totalUpdated,
            enrichedIps.Count,
            onlineCount,
            cacheReport,
            outcomes.ToSummary());
    }

    /// <summary>
    /// Resolves file paths and wildcards, loads cache files, and executes enrichment.
    /// </summary>
    /// <param name="auditPathsOrPatterns">Audit file paths, directories, or glob patterns.</param>
    /// <param name="cachePathsOrPatterns">Cache file paths, directories, or glob patterns.</param>
    /// <param name="options">Enrichment and output options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>The outcome report: processed and generated file paths, and counters.</returns>
    public async Task<OfflineAuditEnrichmentReport> EnrichPathsAsync(
        IReadOnlyList<string> auditPathsOrPatterns,
        IReadOnlyList<string> cachePathsOrPatterns,
        OfflineAuditEnrichmentOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditPathsOrPatterns);
        ArgumentNullException.ThrowIfNull(cachePathsOrPatterns);
        ArgumentNullException.ThrowIfNull(options);

        // An enriched copy found next to its original is the output of an earlier run: enriching it again would
        // write a copy of the copy.
        var selection = await AuditLogSelector.SelectAsync(
            auditPathsOrPatterns,
            AuditInputPathResolver.AuditLogExtensions,
            AuditDuplicatePolicy.EachOriginalLog,
            cancellationToken).ConfigureAwait(false);

        foreach (var skipped in selection.Skipped)
        {
            _logger?.Invoke(skipped.Describe());
        }

        var auditFiles = selection.Files;
        var cacheFiles = AuditInputPathResolver.Resolve(cachePathsOrPatterns, [".json"]);

        // The master cache is read even when the caller did not list it: writing it back without merging would erase
        // every name that the other caches do not carry.
        var inputCaches = cacheFiles.ToList();
        if (File.Exists(options.OutputCachePath) &&
            !inputCaches.Contains(Path.GetFullPath(options.OutputCachePath), StringComparer.OrdinalIgnoreCase))
        {
            inputCaches.Insert(0, options.OutputCachePath);
        }

        var loadedCaches = new List<IReadOnlyDictionary<string, DnsCacheEntry>>();
        foreach (var cachePath in inputCaches)
        {
            var store = new PersistentDnsCacheStore(cachePath);
            loadedCaches.Add(store.Load());
        }

        return await EnrichFilesAsync(auditFiles, loadedCaches, options, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ResolveUnknownAddressesAsync(
        IReadOnlyCollection<string> addresses,
        Dictionary<string, DnsCacheEntry> activeCache,
        bool forceResolve,
        ResolutionOutcomeCounter outcomes,
        CancellationToken cancellationToken)
    {
        _logger?.Invoke($"Resolving {addresses.Count} unknown IP address(es) online...");

        var onlineCount = 0;
        foreach (var ipStr in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((!forceResolve && HasHostName(activeCache, ipStr)) || !IPAddress.TryParse(ipStr, out var address))
            {
                continue;
            }

            var resolvedEntry = await _offlineResolver.ResolveSingleAsync(address, cancellationToken).ConfigureAwait(false);
            var previousEntry = activeCache.GetValueOrDefault(ipStr);
            var keptEntry = KeepResolvedName(previousEntry, resolvedEntry);
            if (keptEntry is not null)
            {
                activeCache[ipStr] = keptEntry;
            }

            outcomes.Record(previousEntry, resolvedEntry, keptEntry);
            onlineCount++;
        }

        return onlineCount;
    }

    private async Task SaveOutputCacheAsync(IReadOnlyDictionary<string, DnsCacheEntry> activeCache, string outputCachePath)
    {
        if (string.IsNullOrWhiteSpace(outputCachePath))
        {
            return;
        }

        // Not cancellable: this save also runs after a cancellation, whose token is already cancelled.
        var store = new PersistentDnsCacheStore(outputCachePath);
        await store.SaveAsync(activeCache, CancellationToken.None).ConfigureAwait(false);
        _logger?.Invoke($"Consolidated cache successfully saved to: {outputCachePath}");
    }

    private static async Task<IReadOnlyList<string>> ExtractIpsRequiringResolutionAsync(
        string filePath,
        IReadOnlyDictionary<string, DnsCacheEntry> knownCache,
        bool forceResolve,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isNdjson = filePath.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase) ||
                       filePath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

        if (isNdjson)
        {
            using var reader = new StreamReader(filePath);
            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("remoteIp", out var ipProp))
                    {
                        continue;
                    }

                    // ToString, unlike GetString, accepts a value of any JSON type (a JSON null gives an empty string),
                    // so the scan reads each line as the enrichment pass does with JsonNode. A value that is not an
                    // address is dropped by the IPAddress.TryParse of the resolution loop.
                    var ip = ipProp.ToString();
                    if (string.IsNullOrWhiteSpace(ip))
                    {
                        continue;
                    }

                    var host = root.TryGetProperty("remoteHostName", out var hostProp) ? hostProp.ToString() : null;
                    if (ShouldResolve(ip, host, knownCache, forceResolve))
                    {
                        result.Add(ip);
                    }
                }
                catch (JsonException)
                {
                    // A line cut short by an abrupt stop of the audit must not block the scan of the rest of the log:
                    // its address is not resolved, and the enrichment pass leaves the line as it is.
                }
            }
        }
        else
        {
            if (await ReadCsvDelimiterAsync(filePath, cancellationToken).ConfigureAwait(false) is not { } delimiter)
            {
                return [];
            }

            using var reader = new StreamReader(filePath);
            var (remoteIpIdx, remoteHostIdx) = (-1, -1);
            var isHeader = true;
            await foreach (var record in AuditCsvRecordReader.ReadRecordsAsync(reader, filePath, delimiter, cancellationToken).ConfigureAwait(false))
            {
                var fields = record.Fields;
                if (isHeader)
                {
                    (remoteIpIdx, remoteHostIdx) = ResolveCsvColumnIndices(fields);
                    if (remoteIpIdx < 0)
                    {
                        return [];
                    }

                    isHeader = false;
                    continue;
                }

                if (remoteIpIdx < fields.Count)
                {
                    var ip = fields[remoteIpIdx];
                    var host = remoteHostIdx >= 0 && remoteHostIdx < fields.Count ? fields[remoteHostIdx] : string.Empty;

                    if (ShouldResolve(ip, host, knownCache, forceResolve))
                    {
                        result.Add(ip);
                    }
                }
            }
        }

        return result.ToList();
    }

    private static bool ShouldResolve(
        string ip,
        string? host,
        IReadOnlyDictionary<string, DnsCacheEntry> knownCache,
        bool forceResolve)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return false;
        }

        if (IsUnspecifiedAddress(ip))
        {
            return false;
        }

        if (forceResolve)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(host) || string.Equals(host, ip, StringComparison.OrdinalIgnoreCase))
        {
            return !HasHostName(knownCache, ip);
        }

        return false;
    }

    private static bool IsUnspecifiedAddress(string ip) =>
        IPAddress.TryParse(ip, out var address) &&
        (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None));

    private static bool HasHostName(IReadOnlyDictionary<string, DnsCacheEntry> cache, string ip) =>
        cache.TryGetValue(ip, out var entry) && !string.IsNullOrWhiteSpace(entry.HostName);

    // A machine that cannot reach the DNS servers answers with an empty entry, or with the registry owner only: neither
    // may replace a name that another machine resolved earlier (a reverse DNS name outranks the registry owner, as in
    // DnsCacheConsolidator). A lookup that no server answered (null) changes nothing, so an unknown address stays out of
    // the cache instead of being written as a negative result.
    private static DnsCacheEntry? KeepResolvedName(DnsCacheEntry? existing, DnsCacheEntry? resolved)
    {
        if (resolved is null)
        {
            return existing;
        }

        if (existing is null || string.IsNullOrWhiteSpace(existing.HostName))
        {
            return resolved;
        }

        var lookupFoundNothing = string.IsNullOrWhiteSpace(resolved.HostName);
        var lookupDowngradesDnsName = string.Equals(existing.Source, "DNS", StringComparison.OrdinalIgnoreCase) &&
                                      !string.Equals(resolved.Source, "DNS", StringComparison.OrdinalIgnoreCase);
        return lookupFoundNothing || lookupDowngradesDnsName ? existing : resolved;
    }

    private static async Task<(int Scanned, int Updated, string OutputPath, IReadOnlyList<string> EnrichedIps)> EnrichSingleAuditFileAsync(
        string inputPath,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        OfflineAuditEnrichmentOptions options,
        CancellationToken cancellationToken)
    {
        var outputPath = options.InPlace ? inputPath : ComputeEnrichedOutputPath(inputPath);
        var isNdjson = inputPath.EndsWith(".ndjson", StringComparison.OrdinalIgnoreCase) ||
                       inputPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);
        var tally = new EnrichmentTally();

        await AtomicFileWriter.WriteAsync(
            outputPath,
            async (stream, token) =>
            {
                // The stream stays open: the writer flushes it to disk and closes it once the content is written.
                var writer = new StreamWriter(stream, leaveOpen: true);
                await using (writer.ConfigureAwait(false))
                {
                    var write = isNdjson
                        ? WriteEnrichedNdjsonAsync(inputPath, writer, cache, options.ForceResolve, tally, token)
                        : WriteEnrichedCsvAsync(inputPath, writer, cache, options.ForceResolve, tally, token);
                    await write.ConfigureAwait(false);
                }
            },
            cancellationToken).ConfigureAwait(false);

        return (tally.Scanned, tally.Updated, outputPath, [.. tally.EnrichedIps]);
    }

    private static async Task WriteEnrichedNdjsonAsync(
        string inputPath,
        StreamWriter writer,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        bool forceResolve,
        EnrichmentTally tally,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(inputPath);
        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var (modifiedLine, wasUpdated, ip) = EnrichNdjsonLine(line, cache, forceResolve);
            tally.Count(wasUpdated, ip);
            await writer.WriteLineAsync(modifiedLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteEnrichedCsvAsync(
        string inputPath,
        StreamWriter writer,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        bool forceResolve,
        EnrichmentTally tally,
        CancellationToken cancellationToken)
    {
        if (await ReadCsvDelimiterAsync(inputPath, cancellationToken).ConfigureAwait(false) is not { } delimiter)
        {
            return;
        }

        using var reader = new StreamReader(inputPath);
        var (remoteIpIdx, remoteHostIdx) = (-1, -1);
        var isHeader = true;
        await foreach (var record in AuditCsvRecordReader.ReadRecordsAsync(reader, inputPath, delimiter, cancellationToken).ConfigureAwait(false))
        {
            if (isHeader)
            {
                (remoteIpIdx, remoteHostIdx) = ResolveCsvColumnIndices(record.Fields);
                await writer.WriteLineAsync(JoinCsvRecord(record.Fields, delimiter).AsMemory(), cancellationToken).ConfigureAwait(false);
                isHeader = false;
                continue;
            }

            var (modifiedLine, wasUpdated, ip) = EnrichCsvRecord(record.Fields, delimiter, remoteIpIdx, remoteHostIdx, cache, forceResolve);
            tally.Count(wasUpdated, ip);
            await writer.WriteLineAsync(modifiedLine.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class EnrichmentTally
    {
        public int Scanned { get; private set; }

        public int Updated { get; private set; }

        public HashSet<string> EnrichedIps { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Count(bool wasUpdated, string? ip)
        {
            Scanned++;
            if (!wasUpdated)
            {
                return;
            }

            Updated++;
            if (!string.IsNullOrWhiteSpace(ip))
            {
                EnrichedIps.Add(ip);
            }
        }
    }

    private static (string ModifiedLine, bool WasUpdated, string? Ip) EnrichNdjsonLine(
        string line,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        bool forceResolve)
    {
        try
        {
            var node = JsonNode.Parse(line);
            if (node is not JsonObject obj)
            {
                return (line, false, null);
            }

            if (!obj.TryGetPropertyValue("remoteIp", out var ipNode) || ipNode is null)
            {
                return (line, false, null);
            }

            var ip = ipNode.ToString();
            if (string.IsNullOrWhiteSpace(ip))
            {
                return (line, false, null);
            }

            var currentHost = obj.TryGetPropertyValue("remoteHostName", out var hostNode) ? hostNode?.ToString() : null;

            if (ShouldUpdateHost(ip, currentHost, cache, forceResolve, out var newHost))
            {
                obj["remoteHostName"] = newHost;
                return (obj.ToJsonString(CompactJsonOptions), true, ip);
            }

            return (line, false, null);
        }
        catch (JsonException)
        {
            return (line, false, null);
        }
    }

    private static (string ModifiedLine, bool WasUpdated, string? Ip) EnrichCsvRecord(
        IReadOnlyList<string> fields,
        char delimiter,
        int remoteIpIdx,
        int remoteHostIdx,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        bool forceResolve)
    {
        if (remoteIpIdx < 0 || remoteHostIdx < 0 || remoteIpIdx >= fields.Count || remoteHostIdx >= fields.Count)
        {
            return (JoinCsvRecord(fields, delimiter), false, null);
        }

        var ip = fields[remoteIpIdx];
        var currentHost = fields[remoteHostIdx];

        if (ShouldUpdateHost(ip, currentHost, cache, forceResolve, out var newHost))
        {
            var updatedFields = fields.ToArray();
            updatedFields[remoteHostIdx] = newHost;
            return (JoinCsvRecord(updatedFields, delimiter), true, ip);
        }

        return (JoinCsvRecord(fields, delimiter), false, null);
    }

    // A record is written back from its fields, not from its text, since a quoted field may span several lines.
    private static string JoinCsvRecord(IEnumerable<string> fields, char delimiter)
        => string.Join(delimiter, fields.Select(field => PersistentDelimitedFileWriter.EscapeField(field, delimiter)));

    private static async Task<char?> ReadCsvDelimiterAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path);
        var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        return headerLine is null ? null : headerLine.Contains(';') ? ';' : ',';
    }

    private static bool ShouldUpdateHost(
        string ip,
        string? currentHost,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        bool forceResolve,
        out string newHost)
    {
        newHost = string.Empty;

        if (string.IsNullOrWhiteSpace(ip) || !cache.TryGetValue(ip, out var entry))
        {
            return false;
        }

        var resolvedName = !string.IsNullOrWhiteSpace(entry.HostName) ? entry.HostName : (entry.Organization ?? string.Empty);
        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            return false;
        }

        if (forceResolve)
        {
            if (!string.Equals(currentHost, resolvedName, StringComparison.OrdinalIgnoreCase))
            {
                newHost = resolvedName;
                return true;
            }
            return false;
        }

        if (string.IsNullOrWhiteSpace(currentHost) || string.Equals(currentHost, ip, StringComparison.OrdinalIgnoreCase))
        {
            newHost = resolvedName;
            return true;
        }

        return false;
    }

    private static string ComputeEnrichedOutputPath(string inputPath)
    {
        var dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath);
        return Path.Combine(dir, $"{fileNameWithoutExt}{AuditLogSelector.EnrichedCopySuffix}{ext}");
    }

    private static (int RemoteIpIdx, int RemoteHostIdx) ResolveCsvColumnIndices(IReadOnlyList<string> headers)
    {
        var remoteIpIdx = -1;
        var remoteHostIdx = -1;

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            if (string.Equals(header, "RemoteIp", StringComparison.OrdinalIgnoreCase))
            {
                remoteIpIdx = i;
            }
            else if (string.Equals(header, "RemoteHostName", StringComparison.OrdinalIgnoreCase))
            {
                remoteHostIdx = i;
            }
        }

        // Positional fallbacks for headerless standard 20-column files
        if (remoteIpIdx < 0 && headers.Count >= 20)
        {
            remoteIpIdx = 11;
        }
        if (remoteHostIdx < 0 && headers.Count >= 20)
        {
            remoteHostIdx = 13;
        }

        return (remoteIpIdx, remoteHostIdx);
    }

    private sealed class ResolutionOutcomeCounter
    {
        private int _newlyNamed;
        private int _renamed;
        private int _unchanged;
        private int _keptAfterFailure;
        private int _stillUnresolved;

        public void Record(DnsCacheEntry? previous, DnsCacheEntry? resolved, DnsCacheEntry? kept)
        {
            var hadName = previous is not null && !string.IsNullOrWhiteSpace(previous.HostName);
            if (hadName && ReferenceEquals(kept, previous))
            {
                _keptAfterFailure++;
                return;
            }

            if (string.IsNullOrWhiteSpace(resolved?.HostName))
            {
                _stillUnresolved++;
                return;
            }

            if (!hadName)
            {
                _newlyNamed++;
            }
            else if (string.Equals(previous?.HostName, resolved.HostName, StringComparison.OrdinalIgnoreCase))
            {
                _unchanged++;
            }
            else
            {
                _renamed++;
            }
        }

        public CacheResolutionSummary ToSummary() => new(_newlyNamed, _renamed, _unchanged, _keptAfterFailure, _stillUnresolved);
    }
}
