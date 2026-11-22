using System.CommandLine;
using System.Net;
using PortAtlas.Cli.Configuration;
using PortAtlas.Cli.Diagnostics;

namespace PortAtlas.Cli.CommandLine.Commands;

/// <summary>
/// <c>portatlas lookup &lt;ip&gt;</c>: step-by-step reverse DNS and RDAP diagnostic of one address.
/// </summary>
public sealed class LookupCommand : Command
{
    private readonly Argument<string> _address = new("ip") { Description = "IPv4 or IPv6 address to diagnose." };
    private readonly Option<int?> _dnsTimeout = OptionFactory.CreateDnsTimeout();
    private readonly Option<int?> _rdapTimeout = OptionFactory.CreateRdapTimeout();
    private readonly Option<string?> _configFile;

    /// <summary>
    /// Creates the command.
    /// </summary>
    /// <param name="configFile">Global <c>--config</c> option.</param>
    public LookupCommand(Option<string?> configFile)
        : base("lookup", "Diagnose the reverse DNS and RDAP resolution of one IP address (cache, OS settings, PTR query, RDAP query).")
    {
        _configFile = configFile;
        _address.Validators.Add(result =>
        {
            if (!IPAddress.TryParse(result.GetValueOrDefault<string>(), out _))
            {
                result.AddError($"'{result.GetValueOrDefault<string>()}' is not an IPv4 or IPv6 address.");
            }
        });
        Arguments.Add(_address);
        Options.Add(_dnsTimeout);
        Options.Add(_rdapTimeout);

        SetAction(async (parseResult, cancellationToken) =>
        {
            var config = BuildConfiguration(parseResult, AppConfigurationFile.Load(parseResult.GetValue(_configFile)).Configuration);
            var address = parseResult.GetValue(_address) ?? string.Empty;
            await new IpResolutionDiagnosticReporter().RunDiagnosticAsync(address, config, cancellationToken).ConfigureAwait(false);
            return 0;
        });
    }

    /// <summary>
    /// Overrides the configuration file values with the options given on the command line.
    /// </summary>
    /// <param name="parseResult">Parsed command line of this command.</param>
    /// <param name="fileConfiguration">Values read from the configuration file.</param>
    /// <returns>The configuration the diagnostic runs with.</returns>
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
