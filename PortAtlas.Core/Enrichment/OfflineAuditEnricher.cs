using System.Net;
using System.Text;
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
    // The audit's default CsvDelimiter. The separator of each log is read from its first line, which may come from
    // another machine's configuration, so this one is kept only on a tie or when the first line gives no separator.
    private const char AuditCsvDefaultDelimiter = ',';

    private static readonly JsonSerializerOptions CompactJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    // The audit writes camelCase keys, and export ndjson reads the keys in any case (AuditNdjsonParser): resolve reads
    // them the same way, so a log saved by another tool is named too, under the key the line already has.
    private static readonly JsonNodeOptions CaseInsensitiveKeys = new() { PropertyNameCaseInsensitive = true };
    private const string RemoteHostNameKey = "remoteHostName";

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
    public Task<OfflineAuditEnrichmentReport> EnrichFilesAsync(
        IReadOnlyList<string> auditFiles,
        IReadOnlyList<IReadOnlyDictionary<string, DnsCacheEntry>> initialCaches,
        OfflineAuditEnrichmentOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditFiles);
        ArgumentNullException.ThrowIfNull(initialCaches);
        ArgumentNullException.ThrowIfNull(options);

        return EnrichAsync(auditFiles, initialCaches, masterCache: null, options, cancellationToken);
    }

    private async Task<OfflineAuditEnrichmentReport> EnrichAsync(
        IReadOnlyList<string> auditFiles,
        IReadOnlyList<IReadOnlyDictionary<string, DnsCacheEntry>> initialCaches,
        IReadOnlyDictionary<string, DnsCacheEntry>? masterCache,
        OfflineAuditEnrichmentOptions options,
        CancellationToken cancellationToken)
    {
        // 1. Consolidate input caches
        var cacheReport = _consolidator.Consolidate(initialCaches, masterCache);
        var activeCache = new Dictionary<string, DnsCacheEntry>(cacheReport.MergedEntries, StringComparer.OrdinalIgnoreCase);
        var namingPolicy = new ConfiguredAddressNamingPolicy(options);

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

        distinctUnresolvedIps.RemoveWhere(ip => !namingPolicy.AllowsLookup(ip));

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
        var namingCache = namingPolicy.WithAliases(activeCache);

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
                namingCache,
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
    /// <exception cref="DnsCacheUnreadableException">When the output cache exists but cannot be read; nothing is resolved
    /// or written.</exception>
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
        // every name that the other caches do not carry. It is merged first but not counted as a source cache.
        // For the same reason a master cache that cannot be read stops the run, listed or not, before anything is
        // resolved or written: reading it as empty would overwrite it with the names of this run only.
        var outputCacheFullPath = string.IsNullOrWhiteSpace(options.OutputCachePath) ? null : Path.GetFullPath(options.OutputCachePath);
        IReadOnlyDictionary<string, DnsCacheEntry>? masterCache = null;
        if (outputCacheFullPath is not null &&
            File.Exists(outputCacheFullPath) &&
            !cacheFiles.Contains(outputCacheFullPath, StringComparer.OrdinalIgnoreCase))
        {
            masterCache = new PersistentDnsCacheStore(options.OutputCachePath).LoadOrThrow();
        }

        var loadedCaches = new List<IReadOnlyDictionary<string, DnsCacheEntry>>();
        foreach (var cachePath in cacheFiles)
        {
            var store = new PersistentDnsCacheStore(cachePath);
            var isOutputCache = string.Equals(cachePath, outputCacheFullPath, StringComparison.OrdinalIgnoreCase);
            loadedCaches.Add(isOutputCache ? store.LoadOrThrow() : store.Load());
        }

        return await EnrichAsync(auditFiles, loadedCaches, masterCache, options, cancellationToken).ConfigureAwait(false);
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
        var saved = await store.SaveAsync(activeCache, CancellationToken.None).ConfigureAwait(false);
        _logger?.Invoke(saved
            ? $"Consolidated cache successfully saved to: {outputCachePath}"
            : $"The consolidated cache could not be written to {outputCachePath}; it keeps its previous version.");
    }

    private static async Task<IReadOnlyList<string>> ExtractIpsRequiringResolutionAsync(
        string filePath,
        IReadOnlyDictionary<string, DnsCacheEntry> knownCache,
        bool forceResolve,
        CancellationToken cancellationToken)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var isNdjson = AuditLogRecognizer.IsNdjsonPath(filePath);

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
                    if (ReadNdjsonRemoteAddress(line) is { } remote && ShouldResolve(remote.Ip, remote.HostName, knownCache, forceResolve))
                    {
                        result.Add(remote.Ip);
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
            var delimiter = await AuditCsvDelimiterDetector.DetectAsync(filePath, AuditCsvDefaultDelimiter, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(filePath);
            CsvNameColumns? located = null;
            await foreach (var record in AuditCsvRecordReader.ReadRecordsAsync(reader, filePath, delimiter, cancellationToken).ConfigureAwait(false))
            {
                var fields = record.Fields;
                if (located is null)
                {
                    located = CsvNameColumns.Locate(fields);
                    if (located.RemoteIpIndex < 0)
                    {
                        return [];
                    }

                    if (located.FirstRecordIsHeader)
                    {
                        continue;
                    }
                }

                if (located.RemoteIpIndex < fields.Count)
                {
                    var ip = fields[located.RemoteIpIndex];
                    var host = located.RemoteHostIndex >= 0 && located.RemoteHostIndex < fields.Count ? fields[located.RemoteHostIndex] : string.Empty;

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
        var isNdjson = AuditLogRecognizer.IsNdjsonPath(inputPath);
        var tally = new EnrichmentTally();

        // The audit writes its CSV with a byte order mark so that Excel reads it as UTF-8: the rewritten log keeps the
        // signature of the original, and a log written without one stays byte for byte the same when nothing changes.
        var outputEncoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: await StartsWithUtf8ByteOrderMarkAsync(inputPath, cancellationToken).ConfigureAwait(false));

        await AtomicFileWriter.WriteAsync(
            outputPath,
            async (stream, token) =>
            {
                // The stream stays open: the writer flushes it to disk and closes it once the content is written.
                var writer = new StreamWriter(stream, outputEncoding, leaveOpen: true);
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

    private static async Task<bool> StartsWithUtf8ByteOrderMarkAsync(string path, CancellationToken cancellationToken)
    {
        byte[] utf8ByteOrderMark = [0xEF, 0xBB, 0xBF];
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, bufferSize: 1, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            var start = new byte[utf8ByteOrderMark.Length];
            var read = await stream.ReadAtLeastAsync(start, start.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
            return read == start.Length && start.AsSpan().SequenceEqual(utf8ByteOrderMark);
        }
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
        var delimiter = await AuditCsvDelimiterDetector.DetectAsync(inputPath, AuditCsvDefaultDelimiter, cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(inputPath);
        CsvNameColumns? located = null;
        await foreach (var record in AuditCsvRecordReader.ReadRecordsAsync(reader, inputPath, delimiter, cancellationToken).ConfigureAwait(false))
        {
            if (located is null)
            {
                located = CsvNameColumns.Locate(record.Fields);
                if (located.FirstRecordIsHeader)
                {
                    await writer.WriteLineAsync(JoinCsvRecord(record.Fields, delimiter).AsMemory(), cancellationToken).ConfigureAwait(false);
                    continue;
                }
            }

            var (modifiedLine, wasUpdated, ip) = EnrichCsvRecord(record.Fields, delimiter, located.RemoteIpIndex, located.RemoteHostIndex, cache, forceResolve);
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

    // ToString accepts a value of any JSON type, and a JSON null gives no host name: the scan and the enrichment pass read
    // each line alike. A value that is not an address is dropped by the IPAddress.TryParse of the resolution loop.
    private static NdjsonRemoteAddress? ReadNdjsonRemoteAddress(string line)
    {
        if (JsonNode.Parse(line, CaseInsensitiveKeys) is not JsonObject record)
        {
            return null;
        }

        JsonNode? ipNode;
        try
        {
            if (!record.TryGetPropertyValue("remoteIp", out ipNode) || ipNode is null)
            {
                return null;
            }
        }
        catch (ArgumentException)
        {
            // Two keys that differ only in case (remoteIp and RemoteIp) leave the address ambiguous: the line is neither
            // resolved nor named, and the enrichment pass copies it unchanged, as a line that is not a JSON object.
            return null;
        }

        var ip = ipNode.ToString();
        if (string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var hostName = record.TryGetPropertyValue(RemoteHostNameKey, out var hostNode) ? hostNode?.ToString() : null;
        return new NdjsonRemoteAddress(record, ip, hostName);
    }

    private sealed record NdjsonRemoteAddress(JsonObject Record, string Ip, string? HostName);

    private static (string ModifiedLine, bool WasUpdated, string? Ip) EnrichNdjsonLine(
        string line,
        IReadOnlyDictionary<string, DnsCacheEntry> cache,
        bool forceResolve)
    {
        try
        {
            if (ReadNdjsonRemoteAddress(line) is not { } remote
                || !ShouldUpdateHost(remote.Ip, remote.HostName, cache, forceResolve, out var newHost))
            {
                return (line, false, null);
            }

            // Setting an existing key through the indexer would rename it to the spelling given here.
            var hostNameKey = remote.Record.Select(property => property.Key)
                .FirstOrDefault(key => key.Equals(RemoteHostNameKey, StringComparison.OrdinalIgnoreCase)) ?? RemoteHostNameKey;
            remote.Record[hostNameKey] = newHost;
            return (remote.Record.ToJsonString(CompactJsonOptions), true, remote.Ip);
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

    /// <summary>
    /// Where the address and name columns of a CSV audit log are, read from its first record.
    /// </summary>
    /// <param name="FirstRecordIsHeader">False for a log written without header, whose first record is a flow.</param>
    /// <param name="RemoteIpIndex">Index of the remote address column, or -1 when the log has none.</param>
    /// <param name="RemoteHostIndex">Index of the remote name column, or -1 when the log has none.</param>
    private sealed record CsvNameColumns(bool FirstRecordIsHeader, int RemoteIpIndex, int RemoteHostIndex)
    {
        private const int StandardColumnCount = 20;
        private const int StandardRemoteIpIndex = 11;
        private const int StandardRemoteHostIndex = 13;

        public static CsvNameColumns Locate(IReadOnlyList<string> firstRecord)
        {
            if (AuditCsvHistoryParser.IsHeaderRecord(firstRecord))
            {
                return new(FirstRecordIsHeader: true, IndexOf(firstRecord, nameof(AuditCsvColumn.RemoteIp)), IndexOf(firstRecord, nameof(AuditCsvColumn.RemoteHostName)));
            }

            return firstRecord.Count >= StandardColumnCount
                ? new(FirstRecordIsHeader: false, StandardRemoteIpIndex, StandardRemoteHostIndex)
                : new(FirstRecordIsHeader: false, -1, -1);
        }

        private static int IndexOf(IReadOnlyList<string> header, string columnName)
        {
            for (var i = 0; i < header.Count; i++)
            {
                if (string.Equals(header[i].Trim(), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }
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
