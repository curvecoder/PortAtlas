using System.Text;
using FileForge.IO;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Ingests raw historical network audits and exports consolidated network flow matrices to CSV.
/// </summary>
/// <param name="csvParser">Parser for historical CSV audit logs.</param>
/// <param name="ndjsonParser">Parser for NDJSON streaming logs.</param>
/// <param name="aggregator">Aggregator consolidating flows into firewall rules.</param>
/// <param name="formatter">Formatter writing RFC 4180 firewall rule rows.</param>
/// <param name="logger">Optional diagnostic reporting callback.</param>
/// <param name="pairAggregator">Aggregator merging the rules into source and destination pairs.</param>
/// <param name="pairFormatter">Formatter writing RFC 4180 pair rows.</param>
public sealed class FirewallRuleExporter(
    IAuditCsvHistoryParser? csvParser = null,
    AuditNdjsonParser? ndjsonParser = null,
    FirewallRuleAggregator? aggregator = null,
    FirewallRuleCsvFormatter? formatter = null,
    Action<string>? logger = null,
    FirewallRulePairAggregator? pairAggregator = null,
    FirewallRulePairCsvFormatter? pairFormatter = null)
{
    private readonly IAuditCsvHistoryParser _csvParser = csvParser ?? new AuditCsvHistoryParser();
    private readonly AuditNdjsonParser _ndjsonParser = ndjsonParser ?? new AuditNdjsonParser();
    private readonly FirewallRuleAggregator _aggregator = aggregator ?? new FirewallRuleAggregator();
    private readonly FirewallRuleCsvFormatter _formatter = formatter ?? new FirewallRuleCsvFormatter();
    private readonly Action<string>? _logger = logger;
    private readonly FirewallRulePairAggregator _pairAggregator = pairAggregator ?? new FirewallRulePairAggregator();
    private readonly FirewallRulePairCsvFormatter _pairFormatter = pairFormatter ?? new FirewallRulePairCsvFormatter();
    private readonly FirewallFlowAggregator _flowAggregator = new();
    private readonly FirewallFlowCsvFormatter _flowFormatter = new();
    private readonly FirewallHostRuleAggregator _hostRuleAggregator = new();
    private readonly FirewallHostRuleCsvFormatter _hostRuleFormatter = new();

    /// <summary>
    /// Ingests audit files, aggregates firewall rules, and exports a formatted CSV matrix.
    /// </summary>
    /// <param name="auditFiles">Collection of source CSV or NDJSON audit file paths.</param>
    /// <param name="outputPath">Destination path for the generated firewall matrix CSV.</param>
    /// <param name="options">Consolidation and filtering options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Execution report detailing processed files, row counts, and exported rule counts.</returns>
    /// <exception cref="OutputFileUnwritableException">Thrown when a matrix file or its folder cannot be created or written.</exception>
    public async Task<FirewallRuleExportReport> ExportRulesAsync(
        IReadOnlyList<string> auditFiles,
        string outputPath,
        FirewallRuleExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditFiles);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        var allEntries = new List<NetworkAuditEntry>();
        var validFiles = new List<string>();

        foreach (var file in auditFiles)
        {
            if (!File.Exists(file))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            validFiles.Add(file);

            if (AuditLogRecognizer.IsNdjsonPath(file))
            {
                var ndjsonEntries = await _ndjsonParser.ParseHistoryAsync(file, cancellationToken: cancellationToken).ConfigureAwait(false);
                allEntries.AddRange(ndjsonEntries);
            }
            else
            {
                var delimiter = await AuditCsvDelimiterDetector.DetectAsync(file, options.Delimiter, cancellationToken).ConfigureAwait(false);
                var csvEntries = await _csvParser.ParseHistoryAsync(file, delimiter: delimiter, cancellationToken: cancellationToken).ConfigureAwait(false);
                allEntries.AddRange(csvEntries);
            }
        }

        _logger?.Invoke($"Consolidating {allEntries.Count} raw flow(s) from {validFiles.Count} file(s)...");

        var rules = _aggregator.Aggregate(allEntries, options);

        await WriteCsvAsync(
            outputPath,
            _formatter.BuildHeader(options.Delimiter),
            rules.Select(rule => _formatter.FormatRow(rule, options.Delimiter)),
            cancellationToken).ConfigureAwait(false);
        _logger?.Invoke($"Firewall rule matrix with {rules.Count} rule(s) successfully generated: {Path.GetFullPath(outputPath)}");

        // The listening sockets come from the same logs and filters as the rules: only the listener switch differs.
        var listeners = await WriteOptionalMatrixAsync(
            options.ListenersOutputPath,
            _formatter.BuildHeader(options.Delimiter),
            () => _aggregator.Aggregate(allEntries, options with { ListenersOnly = true }),
            rule => _formatter.FormatRow(rule, options.Delimiter),
            count => $"Listening socket matrix with {count} socket(s)",
            cancellationToken).ConfigureAwait(false);

        // The pair and host rule matrices are built on the flow matrix, which merges the two ends of each connection.
        var servers = new Lazy<AuditedServerDirectory>(() => AuditedServerDirectory.FromEntries(allEntries));
        var flows = new Lazy<IReadOnlyList<FirewallFlowItem>>(() => _flowAggregator.Aggregate(rules, servers.Value));

        var flowFile = await WriteOptionalMatrixAsync(
            options.FlowsOutputPath,
            _flowFormatter.BuildHeader(options.Delimiter),
            () => flows.Value,
            flow => _flowFormatter.FormatRow(flow, options.Delimiter),
            count => $"Flow matrix with {count} flow(s)",
            cancellationToken).ConfigureAwait(false);

        var pairs = await WriteOptionalMatrixAsync(
            options.PairsOutputPath,
            _pairFormatter.BuildHeader(options.Delimiter),
            () => _pairAggregator.Aggregate(flows.Value),
            pair => _pairFormatter.FormatRow(pair, options.Delimiter),
            count => $"Pair matrix with {count} source/destination pair(s)",
            cancellationToken).ConfigureAwait(false);

        var hostRules = await WriteOptionalMatrixAsync(
            options.HostRulesOutputPath,
            _hostRuleFormatter.BuildHeader(options.Delimiter),
            () => _hostRuleAggregator.Aggregate(flows.Value, servers.Value),
            rule => _hostRuleFormatter.FormatRow(rule, options.Delimiter),
            count => $"Host rule matrix with {count} rule(s)",
            cancellationToken).ConfigureAwait(false);

        return new FirewallRuleExportReport(
            validFiles,
            Path.GetFullPath(outputPath),
            rules.Count,
            allEntries.Count,
            pairs?.Path,
            pairs?.RowCount ?? 0)
        {
            Flows = flowFile,
            HostRules = hostRules,
            Listeners = listeners
        };
    }

    private async Task<ExportedMatrixFile?> WriteOptionalMatrixAsync<T>(
        string? path,
        string header,
        Func<IReadOnlyList<T>> build,
        Func<T, string> format,
        Func<int, string> describe,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var items = build();
        await WriteCsvAsync(path, header, items.Select(format), cancellationToken).ConfigureAwait(false);
        var file = new ExportedMatrixFile(Path.GetFullPath(path), items.Count);
        _logger?.Invoke($"{describe(file.RowCount)} successfully generated: {file.Path}");
        return file;
    }

    // UTF-8 with a byte order mark, as the CSV audit log: spreadsheet programs need it to show accented names. The
    // matrix replaces the previous one only once complete, so that an interrupted export does not leave it truncated.
    private static Task WriteCsvAsync(string path, string header, IEnumerable<string> rows, CancellationToken cancellationToken)
        => OutputFileUnwritableException.GuardAsync(path, () =>
        {
            // AtomicFileWriter would write the whole matrix beside the folder before failing to replace it, and leave
            // it there as <path>.pending.
            if (Directory.Exists(path))
            {
                throw new IOException("A folder has this name.");
            }

            return AtomicFileWriter.WriteAsync(
                path,
                async (stream, token) =>
                {
                    // The stream stays open for AtomicFileWriter, which flushes it to disk.
                    var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
                    await using (writer.ConfigureAwait(false))
                    {
                        await writer.WriteLineAsync(header.AsMemory(), token).ConfigureAwait(false);
                        foreach (var row in rows)
                        {
                            await writer.WriteLineAsync(row.AsMemory(), token).ConfigureAwait(false);
                        }
                    }
                },
                cancellationToken);
        });

    /// <summary>
    /// Resolves file paths, directories, or wildcards, aggregates rules, and exports the CSV matrix.
    /// </summary>
    /// <param name="auditPathsOrPatterns">Paths, directories, or glob patterns for audit files.</param>
    /// <param name="outputPath">Destination path for the generated firewall matrix CSV.</param>
    /// <param name="options">Consolidation and filtering options.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>Execution report detailing processed files, row counts, and exported rule counts.</returns>
    public async Task<FirewallRuleExportReport> ExportRulesFromPathsAsync(
        IReadOnlyList<string> auditPathsOrPatterns,
        string outputPath,
        FirewallRuleExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(auditPathsOrPatterns);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);

        var selection = await AuditLogSelector.SelectAsync(
            auditPathsOrPatterns,
            AuditInputPathResolver.AuditLogExtensions,
            AuditDuplicatePolicy.OneFilePerAudit,
            cancellationToken).ConfigureAwait(false);

        foreach (var skipped in selection.Skipped)
        {
            _logger?.Invoke(skipped.Describe());
        }

        return await ExportRulesAsync(selection.Files, outputPath, options, cancellationToken).ConfigureAwait(false);
    }
}
