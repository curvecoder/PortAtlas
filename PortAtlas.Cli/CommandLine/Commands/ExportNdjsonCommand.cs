using System.CommandLine;
using PortAtlas.Cli.CommandLine.OptionSets;
using PortAtlas.Cli.Configuration;
using PortAtlas.Core.Export;

namespace PortAtlas.Cli.CommandLine.Commands;

/// <summary>
/// <c>portatlas export ndjson</c>: conversion of CSV and NDJSON audit logs into a single NDJSON (JSON Lines) file.
/// </summary>
public sealed class ExportNdjsonCommand : Command
{
    private readonly AuditInputOptionSet _inputs = new();
    private readonly Option<string?> _output = OptionFactory.CreatePath("--output", $"NDJSON file to write. Tokens {{ServerName}}, {{HostName}}, {{Date}}, {{Timestamp}} are expanded. Default: {DefaultOutputFileNames.AuditNdjson}.", "-o");
    private readonly Option<string?> _configFile;

    /// <summary>
    /// Creates the command.
    /// </summary>
    /// <param name="configFile">Global <c>--config</c> option.</param>
    public ExportNdjsonCommand(Option<string?> configFile)
        : base("ndjson", "Convert CSV and NDJSON audit logs into one NDJSON (JSON Lines) file.")
    {
        _configFile = configFile;
        _inputs.AddTo(this);
        Options.Add(_output);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var loaded = AppConfigurationFile.Load(parseResult.GetValue(_configFile));
            if (await _inputs.RefuseTimeDependentDefaultAsync(parseResult, loaded.OutputCsvPathTemplate).ConfigureAwait(false))
            {
                return 1;
            }

            var config = loaded.Configuration;
            var outputPath = Path.GetFullPath(OutputFilePathResolver.Resolve(parseResult.GetValue(_output) ?? DefaultOutputFileNames.AuditNdjson));

            // The output replaces its previous version once every log has been read: written over a log named by its
            // path, it would replace that log by a copy of the logs read.
            if (_inputs.NamesFile(parseResult, config.OutputCsvPath, outputPath))
            {
                await Console.Error.WriteLineAsync($"The output '{outputPath}' is also an audit log to read: choose another --output.").ConfigureAwait(false);
                return 1;
            }

            var selected = await _inputs.ResolveFilesAsync(parseResult, config.OutputCsvPath, AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit, cancellationToken).ConfigureAwait(false);

            // An earlier output found in a directory or a pattern holds flows of the logs next to it: read again, it
            // would repeat them.
            var files = selected.Where(file => !string.Equals(file, outputPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (files.Count < selected.Count)
            {
                Console.WriteLine($"Skipped {outputPath}: it is the output of this command.");
            }

            if (files.Count == 0)
            {
                await Console.Error.WriteLineAsync($"No audit log found for: {_inputs.Describe(parseResult, config.OutputCsvPath)}").ConfigureAwait(false);
                return 1;
            }

            var delimiter = !string.IsNullOrEmpty(config.CsvDelimiter) ? config.CsvDelimiter[0] : ',';

            var count = await new AuditNdjsonExporter(Console.WriteLine).ExportAuditLogsToNdjsonAsync(files, outputPath, delimiter, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Exported {count} audit flow entries from {files.Count} file(s) to NDJSON: {outputPath}");
            return 0;
        });
    }
}
