namespace PortAtlas.Core.Providers;

/// <summary>
/// Reads the network interfaces of the local host.
/// </summary>
public interface IHostInterfaceReader
{
    /// <summary>
    /// Reads every network interface of the local host, in the order the operating system lists them.
    /// </summary>
    /// <returns>The interfaces; empty when the operating system refuses to list them.</returns>
    IReadOnlyList<HostInterfaceSnapshot> ReadInterfaces();
}
