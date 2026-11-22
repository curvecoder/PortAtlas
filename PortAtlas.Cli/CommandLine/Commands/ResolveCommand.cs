using System.CommandLine;
using PortAtlas.Cli.CommandLine.OptionSets;
using PortAtlas.Cli.Configuration;
using PortAtlas.Cli.Enrichment;
using PortAtlas.Core.Enrichment;
using PortAtlas.Core.Export;

namespace PortAtlas.Cli.CommandLine.Commands;

/// <summary>
/// <c>portatlas resolve</c>: DNS cache consolidation and host name enrichment of audit logs, typically on a connected
/// workstation for logs collected on isolated servers.
/// </summary>
public sealed class ResolveCommand : Command
{
    private readonly AuditInputOptionSet _inputs = new();
    private readonly Option<string[]> _cache = new("--cache")
    {
        Description = "DNS cache to merge: a file, a directory or a wildcard pattern. Repeatable. Default: the DNS cache of the configuration. Without --input, the audit logs are left alone and only the caches are resolved.",
        HelpName = "path"
    };
    private readonly Option<string?> _outputCache = OptionFactory.CreatePath("--output-cache", $"Merged master cache to write. Default: the DNS cache of the configuration ({DefaultOutputFileNames.DnsCache}).");
    private readonly Option<bool> _inPlace = new("--in-place") { Description = "Overwrite the audit logs instead of writing *_enriched files." };
    private readonly Option<bool> _forceResolve = new("--force-resolve") { Description = "Resolve every address again, those that already have a name included, and replace the host names already present in the audit logs. A lookup that finds nothing never erases a known name. Without this option only the addresses without a name are resolved." };
    private readonly Option<int?> _rdapDelayMs = OptionFactory.CreateInteger("--rdap-delay-ms", "milliseconds", "Delay between two RDAP queries. Default: 200.", minimum: 0);
    private readonly Option<int?> _dnsTimeout = OptionFactory.CreateDnsTimeout();
    private readonly Option<int?> _rdapTimeout = OptionFactory.CreateRdapTimeout();
    private readonly Option<bool> _cacheOnly = new("--cache-only") { Description = "Use only the merged caches: no DNS or RDAP query is sent." };
    private readonly Option<string?> _configFile;

    /// <summary>
    /// Creates the command.
    /// </summary>
    /// <param name="configFile">Global <c>--config</c> option.</param>
    public ResolveCommand(Option<string?> configFile)
        : base("resolve", "Merge DNS caches, resolve missing host names and write enriched audit logs.")
    {
        _configFile = configFile;
        _inputs.AddTo(this);
        Options.Add(_cache);
        Options.Add(_outputCache);
        Options.Add(_inPlace);
        Options.Add(_forceResolve);
        Options.Add(_rdapDelayMs);
        Options.Add(_cacheOnly);
        Options.Add(_dnsTimeout);
        Options.Add(_rdapTimeout);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var loaded = AppConfigurationFile.Load(parseResult.GetValue(_configFile));
            var config = BuildConfiguration(parseResult, loaded.Configuration);
            var cacheGiven = parseResult.GetValue(_cache) is { Length: > 0 };
            var caches = parseResult.GetValue(_cache) is { Length: > 0 } given ? given : [config.DnsCacheFilePath];

            // Naming a cache without naming audit logs asks for a cache-only run, so that the audit file of the
            // configuration is not rewritten as a side effect.
            IReadOnlyList<string> auditFiles = [];
            if (_inputs.HasExplicitInput(parseResult) || !cacheGiven)
            {
                if (await _inputs.RefuseTimeDependentDefaultAsync(parseResult, loaded.OutputCsvPathTemplate).ConfigureAwait(false))
                {
                    return 1;
                }

                // Each log is rewritten, the CSV and the NDJSON of an audit alike; an enriched copy next to its
                // original is the output of an earlier run and is not enriched again.
                auditFiles = await _inputs.ResolveFilesAsync(parseResult, config.OutputCsvPath, AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.EachOriginalLog, cancellationToken).ConfigureAwait(false);
                if (auditFiles.Count == 0)
                {
                    await Console.Error.WriteLineAsync($"No audit log found for: {_inputs.Describe(parseResult, config.OutputCsvPath)}").ConfigureAwait(false);
                    return 1;
                }
            }

            var options = new OfflineAuditEnrichmentOptions
            {
                InPlace = parseResult.GetValue(_inPlace),
                ForceResolve = parseResult.GetValue(_forceResolve),
                Isolated = parseResult.GetValue(_cacheOnly),
                RdapThrottleDelay = TimeSpan.FromMilliseconds(parseResult.GetValue(_rdapDelayMs) ?? 200),
                OutputCachePath = OutputFilePathResolver.Resolve(parseResult.GetValue(_outputCache) ?? config.DnsCacheFilePath),
                DnsExcludedNetworks = config.DnsExcludedNetworks,
                CustomHostAliases = config.CustomHostAliases
            };

            var runner = new OfflineResolutionCommandRunner(
                dnsResolutionTimeout: TimeSpan.FromMilliseconds(config.DnsResolutionTimeoutMs),
                resolveHostNames: config.ResolveRemoteHostNames,
                resolveIpOwners: config.ResolveIpOwners,
                rdapTimeout: TimeSpan.FromMilliseconds(config.RdapTimeoutMs));
            return await runner.RunAsync(auditFiles, caches, options, cancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Overrides the configuration file values with the options given on the command line.
    /// </summary>
    /// <param name="parseResult">Parsed command line of this command.</param>
    /// <param name="fileConfiguration">Values read from the configuration file.</param>
    /// <returns>The configuration the resolution runs with.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public AppConfiguration BuildConfiguration(ParseResult parseResult, AppConfiguration fileConfiguration)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(fileConfiguration);

        return fileConfiguration with
        {
            DnsResolutionTimeoutMs = parseResult.GetValue(_dnsTimeout) ?? fileConfiguration.DnsResolutionTimeoutMs,
            RdapTimeoutMs = parseResult.GetValue(_rdapTimeout) ?? fileConfiguration.RdapTimeoutMs
        };
    }
}
