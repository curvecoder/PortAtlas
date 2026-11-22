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
    private readonly Option<string?> _flowsOutput = OptionFactory.CreatePath("--flows-output", "Also write a flow matrix CSV (one row per flow, the two ends of a connection merged: processes and observations of each end), with the same filters. Same tokens as --output. Default: not written.");
    private readonly Option<string?> _hostRulesOutput = OptionFactory.CreatePath("--host-rules-output", "Also write a host rule matrix CSV for network security groups (an outbound rule on the source and an inbound rule on the destination of each flow, private addresses only), with the same filters. Same tokens as --output. Default: not written.");
    private readonly Option<string?> _listenersOutput = OptionFactory.CreatePath("--listeners-output", "Also write the listening sockets CSV, as --listeners-only would, in the same pass. Same tokens as --output. Default: not written.");
    private readonly Option<string?> _groupRulesOutput = OptionFactory.CreatePath("--group-rules-output", "Also write a group rule matrix CSV (one row per source group, destination group, protocol and port, from AddressGroups and --address-group; an address in no group stands for itself), with the same filters. Same tokens as --output. Default: not written.");
    private readonly Option<string[]> _addressGroup = OptionFactory.CreateList("--address-group", "name=range", "Address group of the group rule matrix: an IP address or a CIDR subnet and the name of its group (lab=192.168.10.0/24), added to AddressGroups; an address belongs to the group of its most precise range. Repeatable.");
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
        Options.Add(_flowsOutput);
        Options.Add(_hostRulesOutput);
        Options.Add(_listenersOutput);
        Options.Add(_groupRulesOutput);
        _addressGroup.Validators.Add(result =>
        {
            foreach (var value in result.GetValueOrDefault<string[]>() ?? [])
            {
                if (!AddressGroupParser.TryParseArgument(value, out _, out var error))
                {
                    result.AddError(error);
                }
            }
        });
        Options.Add(_addressGroup);
        Options.Add(_minObservations);
        Options.Add(_activeWithinDays);
        Options.Add(_zone);
        _filters.AddTo(this);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var loaded = AppConfigurationFile.Load(parseResult.GetValue(_configFile));
            if (await _inputs.RefuseTimeDependentDefaultAsync(parseResult, loaded.OutputCsvPathTemplate).ConfigureAwait(false))
            {
                return 1;
            }

            var config = _filters.Apply(parseResult, loaded.Configuration);
            var files = await _inputs.ResolveFilesAsync(parseResult, config.OutputCsvPath, AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit, cancellationToken).ConfigureAwait(false);
            if (files.Count == 0)
            {
                await Console.Error.WriteLineAsync($"No audit log found for: {_inputs.Describe(parseResult, config.OutputCsvPath)}").ConfigureAwait(false);
                return 1;
            }

            var outputPath = OutputFilePathResolver.Resolve(parseResult.GetValue(_output) ?? DefaultOutputFileNames.RulesCsv);
            var pairsOutputPath = ResolveOptional(parseResult, _pairsOutput);
            var flowsOutputPath = ResolveOptional(parseResult, _flowsOutput);
            var hostRulesOutputPath = ResolveOptional(parseResult, _hostRulesOutput);
            var listenersOutputPath = ResolveOptional(parseResult, _listenersOutput);
            var groupRulesOutputPath = ResolveOptional(parseResult, _groupRulesOutput);

            (string Option, string? Path)[] requested =
            [
                (_output.Name, outputPath),
                (_pairsOutput.Name, pairsOutputPath),
                (_flowsOutput.Name, flowsOutputPath),
                (_hostRulesOutput.Name, hostRulesOutputPath),
                (_listenersOutput.Name, listenersOutputPath),
                (_groupRulesOutput.Name, groupRulesOutputPath)
            ];
            var conflict = MatrixOutputPathValidator.FindConflict(requested, files);
            if (conflict is not null)
            {
                await Console.Error.WriteLineAsync(conflict).ConfigureAwait(false);
                return 1;
            }

            // Read before any log: a range given to two groups would otherwise be found once every log is read.
            if (!AddressGroupParser.TryBuild(config.AddressGroups, parseResult.GetValue(_addressGroup) ?? [], out var addressGroups, out var groupError))
            {
                await Console.Error.WriteLineAsync(groupError).ConfigureAwait(false);
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
                PairsOutputPath = pairsOutputPath,
                FlowsOutputPath = flowsOutputPath,
                HostRulesOutputPath = hostRulesOutputPath,
                ListenersOutputPath = listenersOutputPath,
                GroupRulesOutputPath = groupRulesOutputPath,
                AddressGroups = addressGroups
            };

            return await new FirewallRuleExportCommandRunner().RunAsync(files, outputPath, options, cancellationToken).ConfigureAwait(false);
        });
    }

    private static string? ResolveOptional(ParseResult parseResult, Option<string?> option) =>
        parseResult.GetValue(option) is { } path ? OutputFilePathResolver.Resolve(path) : null;
}
