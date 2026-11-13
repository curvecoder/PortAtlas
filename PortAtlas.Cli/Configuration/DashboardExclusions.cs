namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Exclusions added from the dashboard since the audit started, as opposed to those read from the configuration
/// file or given on the command line.
/// </summary>
/// <param name="LocalPorts">Local ports excluded during the run.</param>
/// <param name="RemotePorts">Remote ports excluded during the run.</param>
/// <param name="RemoteIps">Remote IP addresses or CIDR subnets excluded during the run.</param>
/// <param name="ExactNames">Process or target names excluded during the run.</param>
internal sealed record DashboardExclusions(
    IReadOnlyList<int> LocalPorts,
    IReadOnlyList<int> RemotePorts,
    IReadOnlyList<string> RemoteIps,
    IReadOnlyList<string> ExactNames);
