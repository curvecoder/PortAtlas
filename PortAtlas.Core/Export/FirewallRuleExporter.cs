using System.Text;
using FileForge.IO;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Ingests raw historical network audits and exports consolidated network flow matrices to CSV or Markdown.
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
    private readonly FirewallGroupRuleAggregator _groupRuleAggregator = new();
    private readonly FirewallGroupRuleCsvFormatter _groupRuleFormatter = new();
    private readonly MarkdownMatrixFormatter _markdownFormatter = new();

    /// <summary>
    /// Ingests audit files, aggregates firewall rules, and exports the matrices in the format of
    /// <see cref="FirewallRuleExportOptions.Format"/>.
    /// </summary>
    /// <param name="auditFiles">Collection of source CSV or NDJSON audit file paths.</param>
    /// <param name="outputPath">Destination path for the generated firewall matrix.</param>
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
        var ruleMatrix = new MatrixSpecification<FirewallRuleItem>(
            options.ListenersOnly ? FirewallMatrixDescriptions.Listeners : FirewallMatrixDescriptions.Rules,
            _formatter,
            _formatter.FormatRow);

        await WriteMatrixAsync(outputPath, ruleMatrix, rules, options, cancellationToken).ConfigureAwait(false);
        _logger?.Invoke($"Firewall rule matrix with {rules.Count} rule(s) successfully generated: {Path.GetFullPath(outputPath)}");

        // The listening sockets come from the same logs and filters as the rules: only the listener switch differs.
        var listeners = await WriteOptionalMatrixAsync(
            options.ListenersOutputPath,
            ruleMatrix with { Description = FirewallMatrixDescriptions.Listeners },
            () => _aggregator.Aggregate(allEntries, options with { ListenersOnly = true }),
            count => $"Listening socket matrix with {count} socket(s)",
            options,
            cancellationToken).ConfigureAwait(false);

        // The pair and host rule matrices are built on the flow matrix, which merges the two ends of each connection.
        var servers = new Lazy<AuditedServerDirectory>(() => AuditedServerDirectory.FromEntries(allEntries));
        var flows = new Lazy<IReadOnlyList<FirewallFlowItem>>(() => _flowAggregator.Aggregate(rules, servers.Value));

        var flowFile = await WriteOptionalMatrixAsync(
            options.FlowsOutputPath,
            new MatrixSpecification<FirewallFlowItem>(FirewallMatrixDescriptions.Flows, _flowFormatter, _flowFormatter.FormatRow),
            () => flows.Value,
            count => $"Flow matrix with {count} flow(s)",
            options,
            cancellationToken).ConfigureAwait(false);

        var pairs = await WriteOptionalMatrixAsync(
            options.PairsOutputPath,
            new MatrixSpecification<FirewallRulePairItem>(FirewallMatrixDescriptions.Pairs, _pairFormatter, _pairFormatter.FormatRow),
            () => _pairAggregator.Aggregate(flows.Value),
            count => $"Pair matrix with {count} source/destination pair(s)",
            options,
            cancellationToken).ConfigureAwait(false);

        var hostRules = await WriteOptionalMatrixAsync(
            options.HostRulesOutputPath,
            new MatrixSpecification<FirewallHostRuleItem>(FirewallMatrixDescriptions.HostRules, _hostRuleFormatter, _hostRuleFormatter.FormatRow),
            () => _hostRuleAggregator.Aggregate(flows.Value, servers.Value),
            count => $"Host rule matrix with {count} rule(s)",
            options,
            cancellationToken).ConfigureAwait(false);

        var groupRules = await WriteOptionalMatrixAsync(
            options.GroupRulesOutputPath,
            new MatrixSpecification<FirewallGroupRuleItem>(FirewallMatrixDescriptions.GroupRules, _groupRuleFormatter, _groupRuleFormatter.FormatRow),
            () => _groupRuleAggregator.Aggregate(rules, options.AddressGroups),
            count => $"Group rule matrix with {count} rule(s)",
            options,
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
            Listeners = listeners,
            GroupRules = groupRules
        };
    }

    private async Task<ExportedMatrixFile?> WriteOptionalMatrixAsync<T>(
        string? path,
        MatrixSpecification<T> matrix,
        Func<IReadOnlyList<T>> build,
        Func<int, string> describe,
        FirewallRuleExportOptions options,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var items = build();
        await WriteMatrixAsync(path, matrix, items, options, cancellationToken).ConfigureAwait(false);
        var file = new ExportedMatrixFile(Path.GetFullPath(path), items.Count);
        _logger?.Invoke($"{describe(file.RowCount)} successfully generated: {file.Path}");
        return file;
    }

    // The CSV matrix keeps the byte order mark of the CSV audit log: spreadsheet programs need it to show accented
    // names. A Markdown file goes to text editors, renderers and version control, where the mark is a stray character.
    private Task WriteMatrixAsync<T>(
        string path,
        MatrixSpecification<T> matrix,
        IReadOnlyList<T> items,
        FirewallRuleExportOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Format == MatrixFileFormat.Markdown)
        {
            var document = _markdownFormatter.Format(matrix.Description, matrix.Layout, items);
            return WriteLinesAsync(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), document, cancellationToken);
        }

        var header = string.Join(options.Delimiter, matrix.Layout.ColumnNames);
        var rows = items.Select(item => matrix.CsvRow(item, options.Delimiter));
        return WriteLinesAsync(path, Encoding.UTF8, rows.Prepend(header), cancellationToken);
    }

    // The matrix replaces the previous one only once complete, so that an interrupted export does not leave it truncated.
    private static Task WriteLinesAsync(string path, Encoding encoding, IEnumerable<string> lines, CancellationToken cancellationToken)
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
                    var writer = new StreamWriter(stream, encoding, leaveOpen: true);
                    await using (writer.ConfigureAwait(false))
                    {
                        foreach (var line in lines)
                        {
                            await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
                        }
                    }
                },
                cancellationToken);
        });

    /// <summary>
    /// Resolves file paths, directories, or wildcards, aggregates rules, and exports the matrices.
    /// </summary>
    /// <param name="auditPathsOrPatterns">Paths, directories, or glob patterns for audit files.</param>
    /// <param name="outputPath">Destination path for the generated firewall matrix.</param>
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

    // A matrix in both formats: its explanation and columns for Markdown, and for CSV its row formatter, which escapes
    // the values for a spreadsheet.
    private sealed record MatrixSpecification<T>(
        MatrixDescription Description,
        IMatrixColumnLayout<T> Layout,
        Func<T, char, string> CsvRow);
}
