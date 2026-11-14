using System.Collections.Concurrent;
using System.Net;
using PortAtlas.Core.Models;

namespace PortAtlas.Core.Deduplication;

/// <summary>
/// Thread-safe deduplication register aggregating active network flow records over time.
/// </summary>
public sealed class ConnectionDeduplicationRegister
{
    private readonly ConcurrentDictionary<FlowIdentityKey, NetworkAuditEntry> _activeFlows = new();

    /// <summary>
    /// Hydrates the register with pre-existing audit entries reloaded from disk.
    /// </summary>
    /// <param name="existingEntries">Collection of pre-existing network audit entries.</param>
    public void Hydrate(IEnumerable<NetworkAuditEntry> existingEntries)
    {
        ArgumentNullException.ThrowIfNull(existingEntries);

        foreach (var entry in existingEntries)
        {
            if (entry is null)
            {
                continue;
            }

            var key = FlowIdentityKey.FromEntry(entry);
            _activeFlows.AddOrUpdate(
                key,
                _ => entry,
                (_, existing) =>
                {
                    var firstSeen = existing.FirstSeenUtc < entry.FirstSeenUtc ? existing.FirstSeenUtc : entry.FirstSeenUtc;
                    var lastSeen = existing.LastSeenUtc > entry.LastSeenUtc ? existing.LastSeenUtc : entry.LastSeenUtc;
                    var obsCount = Math.Max(existing.ObservationCount, entry.ObservationCount);
                    return existing with
                    {
                        FirstSeenUtc = firstSeen,
                        LastSeenUtc = lastSeen,
                        ObservationCount = obsCount,
                        RemoteHostName = !string.IsNullOrWhiteSpace(existing.RemoteHostName)
                            ? existing.RemoteHostName
                            : entry.RemoteHostName
                    };
                });
        }
    }

    /// <summary>
    /// Attempts to register an audit entry. Returns true if this is the first observation cycle for this flow.
    /// </summary>
    /// <param name="candidate">Candidate audit entry.</param>
    /// <param name="registered">Registered or updated entry in the register.</param>
    /// <returns>True if newly discovered; false if updating an existing flow record.</returns>
    public bool TryRegisterOrUpdate(NetworkAuditEntry candidate, out NetworkAuditEntry registered)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var key = FlowIdentityKey.FromEntry(candidate);
        var isNew = false;

        var result = _activeFlows.AddOrUpdate(
            key,
            addValueFactory: _ =>
            {
                isNew = true;
                return candidate;
            },
            updateValueFactory: (_, existing) =>
                existing with
                {
                    LastSeenUtc = candidate.LastSeenUtc,
                    ObservationCount = existing.ObservationCount + 1,
                    Socket = existing.Socket with { State = candidate.Socket.State },

                    // A flow reloaded from a log written before its name was resolved has no name, and the resolver
                    // is not asked again because its cache already answers: the candidate carries that cached name.
                    RemoteHostName = string.IsNullOrWhiteSpace(existing.RemoteHostName)
                        ? candidate.RemoteHostName
                        : existing.RemoteHostName
                });

        registered = result;
        return isNew;
    }

    /// <summary>
    /// Updates the remote host name for all registered flows matching a specific remote IP address.
    /// </summary>
    /// <param name="remoteAddress">Remote IP address whose host name was resolved.</param>
    /// <param name="resolvedHostName">Resolved host name or FQDN.</param>
    public void UpdateRemoteHostName(IPAddress remoteAddress, string resolvedHostName)
    {
        ArgumentNullException.ThrowIfNull(remoteAddress);
        ArgumentNullException.ThrowIfNull(resolvedHostName);

        foreach (var (key, entry) in _activeFlows)
        {
            if (entry.Socket.RemoteEndpoint.Address.Equals(remoteAddress))
            {
                NameFlow(key, remoteAddress, resolvedHostName);
            }
        }
    }

    // The scan loop updates the same flows while names arrive from the resolver: the name is written only over
    // the value it was computed from, and computed again from the newer value when an observation got in between.
    private void NameFlow(FlowIdentityKey key, IPAddress remoteAddress, string resolvedHostName)
    {
        while (_activeFlows.TryGetValue(key, out var entry)
            && entry.Socket.RemoteEndpoint.Address.Equals(remoteAddress)
            && string.IsNullOrWhiteSpace(entry.RemoteHostName))
        {
            if (_activeFlows.TryUpdate(key, entry with { RemoteHostName = resolvedHostName }, entry))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Returns a point-in-time snapshot of all active audit entries stored in memory.
    /// </summary>
    /// <returns>Read-only list of audit entries.</returns>
    public IReadOnlyList<NetworkAuditEntry> GetAllEntries() => [.. _activeFlows.Values];

    /// <summary>
    /// Total count of unique flows recorded.
    /// </summary>
    public int UniqueCount => _activeFlows.Count;
}
