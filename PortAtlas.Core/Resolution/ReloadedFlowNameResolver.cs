using PortAtlas.Core.Models;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Gives the flows reloaded from an audit log the remote host names already held in the resolver cache.
/// </summary>
/// <param name="hostNameResolver">Resolver whose cache is read; no resolution is requested.</param>
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
}
