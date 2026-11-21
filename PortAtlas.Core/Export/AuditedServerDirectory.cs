using PortAtlas.Core.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Addresses of the audited servers, read from their own audit logs.
/// </summary>
public sealed class AuditedServerDirectory
{
    private readonly Dictionary<string, (string Name, DateTimeOffset SeenUtc)> _servers;

    private AuditedServerDirectory(Dictionary<string, (string Name, DateTimeOffset SeenUtc)> servers) => _servers = servers;

    /// <summary>
    /// Directory that knows no server.
    /// </summary>
    public static AuditedServerDirectory Empty { get; } = new(new Dictionary<string, (string, DateTimeOffset)>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Collects the local addresses of every audited server, before any filter: a server whose flows were all filtered
    /// out still has a log.
    /// </summary>
    /// <param name="entries">Audit entries of every log read.</param>
    /// <returns>The directory of the servers found.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="entries"/> is null.</exception>
    public static AuditedServerDirectory FromEntries(IEnumerable<NetworkAuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var servers = new Dictionary<string, (string Name, DateTimeOffset SeenUtc)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            var address = entry.Socket.LocalEndpoint.Address;
            if (!NetworkZoneClassifier.NamesOneMachine(address) || string.IsNullOrWhiteSpace(entry.Server.HostName))
            {
                continue;
            }

            // An address moved from one server to another (a cluster address, a rebuilt machine) belongs to the server
            // that held it last; the name order settles a tie so that the result does not depend on the reading order.
            var key = address.ToString();
            var candidate = (entry.Server.HostName, entry.LastSeenUtc);
            if (!servers.TryGetValue(key, out var known) ||
                candidate.LastSeenUtc > known.SeenUtc ||
                (candidate.LastSeenUtc == known.SeenUtc && string.CompareOrdinal(candidate.HostName, known.Name) < 0))
            {
                servers[key] = candidate;
            }
        }

        return new AuditedServerDirectory(servers);
    }

    /// <summary>
    /// Finds the audited server that owns an address.
    /// </summary>
    /// <param name="ip">Address, as written in the matrices.</param>
    /// <param name="serverName">Name of the server, or an empty string when no log of this address was read.</param>
    /// <returns><see langword="true"/> when a log of this address was read.</returns>
    public bool TryGetServerName(string ip, out string serverName)
    {
        if (_servers.TryGetValue(ip, out var server))
        {
            serverName = server.Name;
            return true;
        }

        serverName = string.Empty;
        return false;
    }
}
