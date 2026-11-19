using System.CommandLine;
using PortAtlas.Cli.CommandLine.OptionSets;
using PortAtlas.Cli.Configuration;
using PortAtlas.Cli.Export;
using PortAtlas.Core.Export;

namespace PortAtlas.Cli.CommandLine.Commands;

/// <summary>
/// <c>portatlas export rules</c>: network flow matrix (source, destination, port) for firewall rules.
/// </summary>
public sealed class ExportRulesCommand : Command
{
    private readonly AuditInputOptionSet _inputs = new();
    private readonly FlowFilterOptionSet _filters = new();
    private readonly Option<string?> _output = OptionFactory.CreatePath("--output", $"Matrix CSV to write. Tokens {{ServerName}}, {{HostName}}, {{Date}}, {{Timestamp}} are expanded. Default: {DefaultOutputFileNames.RulesCsv}.", "-o");
    private readonly Option<int?> _minObservations = OptionFactory.CreateInteger("--min-observations", "count", "Keep only flows observed at least this number of times. Default: 1.", minimum: 1);
    private readonly Option<int?> _activeWithinDays = OptionFactory.CreateInteger("--active-within-days", "days", "Keep only flows seen during the last N days. Default: no limit.", minimum: 1);
    private readonly Option<string?> _pairsOutput = OptionFactory.CreatePath("--pairs-output", "Also write a pair matrix CSV (one row per source and destination: flow count, ports, observations), with the same filters. Same tokens as --output. Default: not written.");
    private readonly Option<string?> _zone = OptionFactory.CreateChoice("--zone", "Destination zone to keep: internal (private address ranges), public, or all. Default: all.", "all", "internal", "public");
    private readonly Option<string?> _configFile;

    /// <summary>
    /// Creates the command.
    /// </summary>
    /// <param name="configFile">Global <c>--config</c> option.</param>
    public ExportRulesCommand(Option<string?> configFile)
        : base("rules", "Consolidate audit logs (CSV or NDJSON) into a source/destination/port matrix for firewall rules.")
    {
        _configFile = configFile;
        _inputs.AddTo(this);
        Options.Add(_output);
        Options.Add(_pairsOutput);
        Options.Add(_minObservations);
        Options.Add(_activeWithinDays);
        Options.Add(_zone);
        _filters.AddTo(this);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var config = _filters.Apply(parseResult, AppConfigurationFile.Load(parseResult.GetValue(_configFile)).Configuration);
            var files = await _inputs.ResolveFilesAsync(parseResult, config.OutputCsvPath, AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit, cancellationToken).ConfigureAwait(false);
            if (files.Count == 0)
            {
                await Console.Error.WriteLineAsync($"No audit log found for: {_inputs.Describe(parseResult, config.OutputCsvPath)}").ConfigureAwait(false);
                return 1;
            }

            var outputPath = OutputFilePathResolver.Resolve(parseResult.GetValue(_output) ?? DefaultOutputFileNames.RulesCsv);
            var pairsOutputPath = parseResult.GetValue(_pairsOutput) is { } pairsOutput ? OutputFilePathResolver.Resolve(pairsOutput) : null;

            // The matrices are written once every log has been read: written over one of them, they would replace an
            // audit by a matrix, which no later command reads as flows.
            var overwrittenLog = new[] { outputPath, pairsOutputPath }
                .OfType<string>()
                .Select(Path.GetFullPath)
                .FirstOrDefault(path => files.Contains(path, StringComparer.OrdinalIgnoreCase));
            if (overwrittenLog is not null)
            {
                await Console.Error.WriteLineAsync($"The output '{overwrittenLog}' is also an audit log to read: choose another --output or --pairs-output.").ConfigureAwait(false);
                return 1;
            }

            var filters = FlowFilters.Parse(config);
            var options = new FirewallRuleExportOptions
            {
                // MapExcludeEphemeralPorts tunes the topology map only: a firewall matrix keeps excluding
                // ephemeral destinations unless the option asks for them on this command line.
                ExcludeEphemeralPorts = !parseResult.GetValue(_filters.IncludeEphemeralPorts),
                MinObservations = parseResult.GetValue(_minObservations) ?? 1,
                ActiveWithinDays = parseResult.GetValue(_activeWithinDays),
                DestinationZoneFilter = parseResult.GetValue(_zone) ?? "all",
                ListenersOnly = config.MapListenersOnly,
                Direction = config.MapDirection,
                IncludedPorts = filters.IncludedPorts,
                ExcludedPorts = filters.ExcludedPorts,
                IncludedHosts = config.MapIncludedHosts,
                ExcludedHosts = config.MapExcludedHosts,
                IncludedIps = filters.IncludedIps,
                ExcludedIps = filters.ExcludedIps,
                IncludedProcesses = config.MapIncludedProcesses,
                ExcludedProcesses = config.MapExcludedProcesses,
                Delimiter = !string.IsNullOrEmpty(config.CsvDelimiter) ? config.CsvDelimiter[0] : ',',
                PairsOutputPath = pairsOutputPath
            };

            return await new FirewallRuleExportCommandRunner().RunAsync(files, outputPath, options, cancellationToken).ConfigureAwait(false);
        });
    }
}
