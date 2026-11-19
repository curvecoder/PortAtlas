using PortAtlas.Core.Models;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Gives the flows reloaded from an audit log the remote host names already held in the resolver cache, and asks for
/// the names still missing.
/// </summary>
/// <param name="hostNameResolver">Resolver whose cache is read by <see cref="ResolveFromCache"/> and to which
/// <see cref="QueueUnnamed"/> hands the addresses without name.</param>
public sealed class ReloadedFlowNameResolver(IHostNameResolver hostNameResolver)
{
    /// <summary>
    /// Returns the reloaded flows, with the cached remote host name on each flow that has none.
    /// </summary>
    /// <param name="reloadedFlows">Flows parsed from the audit log.</param>
    /// <returns>The flows in the same order; a flow that already has a name is returned unchanged.</returns>
    public IReadOnlyList<NetworkAuditEntry> ResolveFromCache(IReadOnlyList<NetworkAuditEntry> reloadedFlows)
    {
        ArgumentNullException.ThrowIfNull(reloadedFlows);

        var named = new List<NetworkAuditEntry>(reloadedFlows.Count);
        foreach (var flow in reloadedFlows)
        {
            if (string.IsNullOrWhiteSpace(flow.RemoteHostName)
                && hostNameResolver.TryGetCachedHostName(flow.Socket.RemoteEndpoint.Address, out var cachedName)
                && !string.IsNullOrWhiteSpace(cachedName))
            {
                named.Add(flow with { RemoteHostName = cachedName });
                continue;
            }

            named.Add(flow);
        }

        return named;
    }

    /// <summary>
    /// Queues the resolution of the remote address of each reloaded flow that still has no name.
    /// </summary>
    /// <param name="reloadedFlows">Flows returned by <see cref="ResolveFromCache"/>.</param>
    public void QueueUnnamed(IReadOnlyList<NetworkAuditEntry> reloadedFlows)
    {
        ArgumentNullException.ThrowIfNull(reloadedFlows);

        // A flow logged by an earlier run whose lookup got no answer is not in the cache; without this request it
        // would stay unnamed unless a scan saw the connection again.
        var unnamedAddresses = reloadedFlows
            .Where(flow => string.IsNullOrWhiteSpace(flow.RemoteHostName))
            .Select(flow => flow.Socket.RemoteEndpoint.Address)
            .Distinct();
        foreach (var address in unnamedAddresses)
        {
            hostNameResolver.QueueResolution(address);
        }
    }
}
