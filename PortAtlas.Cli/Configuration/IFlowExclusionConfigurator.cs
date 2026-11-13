namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Contract for dynamic configuration and persistent storage of flow exclusion rules.
/// </summary>
public interface IFlowExclusionConfigurator
{
    /// <summary>
    /// Current active application configuration.
    /// </summary>
    AppConfiguration CurrentConfiguration { get; }

    /// <summary>
    /// Excludes a local port in memory and persists the modification to configuration file.
    /// </summary>
    /// <param name="port">Local port number to exclude.</param>
    /// <returns>Description of the action for the dashboard banner; it says so when the configuration file could not be updated.</returns>
    string ExcludeLocalPort(int port);

    /// <summary>
    /// Excludes a remote IP address or CIDR subnet in memory and persists the modification to configuration file.
    /// </summary>
    /// <param name="ipOrCidr">Remote IP address or CIDR subnet to exclude.</param>
    /// <returns>Description of the action for the dashboard banner; it says so when the configuration file could not be updated.</returns>
    string ExcludeRemoteIp(string ipOrCidr);

    /// <summary>
    /// Excludes a remote port in memory and persists the modification to configuration file.
    /// </summary>
    /// <param name="port">Remote port number to exclude.</param>
    /// <returns>Description of the action for the dashboard banner; it says so when the configuration file could not be updated.</returns>
    string ExcludeRemotePort(int port);

    /// <summary>
    /// Excludes an exact process name in memory and persists the modification to configuration file.
    /// </summary>
    /// <param name="processName">Exact process name to exclude.</param>
    /// <returns>Description of the action for the dashboard banner; it says so when the configuration file could not be updated.</returns>
    string ExcludeProcess(string processName);

    /// <summary>
    /// Excludes an application target or script executable name in memory and persists the modification to configuration file.
    /// </summary>
    /// <param name="target">Application target or binary payload name to exclude.</param>
    /// <returns>Description of the action for the dashboard banner; it says so when the configuration file could not be updated.</returns>
    string ExcludeTarget(string target);
}
