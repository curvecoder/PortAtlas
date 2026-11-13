using System.Net;
using System.Text.Json;
using PortAtlas.Core.Filtering;
using ProcessForge.Filtering;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Applies the exclusions chosen in the dashboard to the in-memory filters and adds them to the configuration file.
/// </summary>
public sealed class FlowExclusionConfigurator(
    AppConfiguration initialConfiguration,
    string configurationFilePath,
    INetworkFlowFilter networkFlowFilter,
    ProcessExclusionFilter processExclusionFilter) : IFlowExclusionConfigurator
{
    private readonly object _syncLock = new();
    private readonly ConfigurationFileExclusionWriter _configurationFile = new(configurationFilePath);
    private readonly List<int> _addedLocalPorts = [];
    private readonly List<int> _addedRemotePorts = [];
    private readonly List<string> _addedRemoteIps = [];
    private readonly List<string> _addedExactNames = [];
    private readonly INetworkFlowFilter _networkFlowFilter = networkFlowFilter ?? throw new ArgumentNullException(nameof(networkFlowFilter));
    private readonly ProcessExclusionFilter _processExclusionFilter = processExclusionFilter ?? throw new ArgumentNullException(nameof(processExclusionFilter));

    private readonly string _configurationFileName = Path.GetFileName(configurationFilePath);

    private AppConfiguration _currentConfiguration = initialConfiguration ?? throw new ArgumentNullException(nameof(initialConfiguration));
    private string? _unsavedReason;

    /// <inheritdoc />
    public AppConfiguration CurrentConfiguration
    {
        get
        {
            lock (_syncLock)
            {
                return _currentConfiguration;
            }
        }
    }

    /// <inheritdoc />
    public string ExcludeLocalPort(int port)
    {
        lock (_syncLock)
        {
            _networkFlowFilter.ExcludeLocalPort(port);

            if (!_currentConfiguration.ExcludedLocalPorts.Contains(port))
            {
                var updatedPorts = new List<int>(_currentConfiguration.ExcludedLocalPorts) { port };
                _currentConfiguration = _currentConfiguration with { ExcludedLocalPorts = updatedPorts };
                _addedLocalPorts.Add(port);
                SaveAddedExclusions();
            }

            return WithSaveStatus($"Excluded local port {port}");
        }
    }

    /// <inheritdoc />
    public string ExcludeRemoteIp(string ipOrCidr)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ipOrCidr);

        lock (_syncLock)
        {
            var cleanIp = ipOrCidr.Trim();
            if (IPAddress.TryParse(cleanIp, out var parsedIp) && parsedIp.IsIPv4MappedToIPv6)
            {
                cleanIp = parsedIp.MapToIPv4().ToString();
            }

            _networkFlowFilter.ExcludeRemoteIp(cleanIp);

            if (!_currentConfiguration.ExcludedRemoteIps.Contains(cleanIp, StringComparer.OrdinalIgnoreCase))
            {
                var updatedIps = new List<string>(_currentConfiguration.ExcludedRemoteIps) { cleanIp };
                _currentConfiguration = _currentConfiguration with { ExcludedRemoteIps = updatedIps };
                _addedRemoteIps.Add(cleanIp);
                SaveAddedExclusions();
            }

            return WithSaveStatus($"Excluded remote IP {cleanIp}");
        }
    }

    /// <inheritdoc />
    public string ExcludeRemotePort(int port)
    {
        lock (_syncLock)
        {
            _networkFlowFilter.ExcludeRemotePort(port);

            if (!_currentConfiguration.ExcludedRemotePorts.Contains(port))
            {
                var updatedPorts = new List<int>(_currentConfiguration.ExcludedRemotePorts) { port };
                _currentConfiguration = _currentConfiguration with { ExcludedRemotePorts = updatedPorts };
                _addedRemotePorts.Add(port);
                SaveAddedExclusions();
            }

            return WithSaveStatus($"Excluded remote port {port}");
        }
    }

    /// <inheritdoc />
    public string ExcludeProcess(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);

        lock (_syncLock)
        {
            var cleanName = processName.Trim();
            _processExclusionFilter.ExcludeExactName(cleanName);

            var updatedNames = new List<string>(_currentConfiguration.ExcludedExactNames);
            var changed = false;

            if (!updatedNames.Contains(cleanName, StringComparer.OrdinalIgnoreCase))
            {
                updatedNames.Add(cleanName);
                _addedExactNames.Add(cleanName);
                changed = true;
            }

            var baseName = Path.GetFileNameWithoutExtension(cleanName);
            if (!string.IsNullOrWhiteSpace(baseName) && !baseName.Equals(cleanName, StringComparison.OrdinalIgnoreCase))
            {
                _processExclusionFilter.ExcludeExactName(baseName);
                if (!updatedNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                {
                    updatedNames.Add(baseName);
                    _addedExactNames.Add(baseName);
                    changed = true;
                }
            }

            if (changed)
            {
                _currentConfiguration = _currentConfiguration with { ExcludedExactNames = updatedNames };
                SaveAddedExclusions();
            }

            return WithSaveStatus($"Excluded process '{cleanName}'");
        }
    }

    /// <inheritdoc />
    public string ExcludeTarget(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        lock (_syncLock)
        {
            var cleanTarget = target.Trim().Trim('"', '\'');
            if (cleanTarget.Contains('/') || cleanTarget.Contains('\\'))
            {
                var extracted = Path.GetFileName(cleanTarget);
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    cleanTarget = extracted;
                }
            }

            _processExclusionFilter.ExcludeExactName(cleanTarget);

            var updatedNames = new List<string>(_currentConfiguration.ExcludedExactNames);
            var changed = false;

            if (!updatedNames.Contains(cleanTarget, StringComparer.OrdinalIgnoreCase))
            {
                updatedNames.Add(cleanTarget);
                _addedExactNames.Add(cleanTarget);
                changed = true;
            }

            var baseName = Path.GetFileNameWithoutExtension(cleanTarget);
            if (!string.IsNullOrWhiteSpace(baseName) && !baseName.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase))
            {
                _processExclusionFilter.ExcludeExactName(baseName);
                if (!updatedNames.Contains(baseName, StringComparer.OrdinalIgnoreCase))
                {
                    updatedNames.Add(baseName);
                    _addedExactNames.Add(baseName);
                    changed = true;
                }
            }

            if (changed)
            {
                _currentConfiguration = _currentConfiguration with { ExcludedExactNames = updatedNames };
                SaveAddedExclusions();
            }

            return WithSaveStatus($"Excluded target '{cleanTarget}'");
        }
    }

    // Every exclusion of the run is handed over each time, not only the latest one: a save that failed (file locked
    // by an editor or a backup) is caught up by the next one. An exclusion removed from the file by hand during the
    // run therefore comes back with the next save, in line with the filters, which keep it until the audit stops.
    private void SaveAddedExclusions()
    {
        try
        {
            _configurationFile.Add(new DashboardExclusions(_addedLocalPorts, _addedRemotePorts, _addedRemoteIps, _addedExactNames));
            _unsavedReason = null;
        }
        catch (JsonException)
        {
            // A file edited into something that is no longer a configuration document must not interrupt live
            // monitoring: it is left as it is, the exclusion stays active in memory for this run, and the banner
            // tells the operator that the file does not hold it.
            _unsavedReason = "the file is no longer a valid configuration";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same tolerance for a file that is locked, read-only or held open by another program: the next
            // exclusion saves this one too.
            _unsavedReason = "the file is in use or cannot be written";
        }
    }

    // The reason stays until a save succeeds, so that an exclusion that needs no new save (value already excluded
    // in memory) does not read as if the file were up to date.
    private string WithSaveStatus(string action) =>
        _unsavedReason is null ? action : $"{action} - not saved to {_configurationFileName}: {_unsavedReason}";
}
