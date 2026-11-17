namespace PortAtlas.Cli.Dashboard;

/// <summary>
/// Selectable interactive columns for inline flow exclusion in the TUI dashboard.
/// </summary>
public enum InteractiveExclusionColumn
{
    /// <summary>
    /// Local listening or communicating port.
    /// </summary>
    Local = 0,

    /// <summary>
    /// Remote IP address.
    /// </summary>
    Remote = 1,

    /// <summary>
    /// Remote port, selected within the same cell as the remote address.
    /// </summary>
    RemotePort = 2,

    /// <summary>
    /// Exact owning process name.
    /// </summary>
    Process = 3,

    /// <summary>
    /// Application target executable or payload command line.
    /// </summary>
    Target = 4
}
