using System.Net;
using PortAtlas.Core.Filtering;
using PortAtlas.Core.Models;
using SocketForge.Models;
using TopologyForge.Filtering;
using TopologyForge.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Normalizes and aggregates raw network connection flows into consolidated firewall rule records.
/// </summary>
public sealed class FirewallRuleAggregator(TimeProvider? timeProvider = null)
{
    private const string AnySourceHost = "Any";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private static readonly Dictionary<int, string> WellKnownServices = new()
    {
        [22] = "SSH",
        [25] = "SMTP",
        [53] = "DNS",
        [80] = "HTTP",
        [123] = "NTP",
        [139] = "NetBIOS",
        [389] = "LDAP",
        [443] = "HTTPS",
        [445] = "SMB",
        [587] = "Submission",
        [636] = "LDAPS",
        [1433] = "MS-SQL",
        [1521] = "Oracle-TNS",
        [3306] = "MySQL",
        [3389] = "RDP",
        [5432] = "PostgreSQL",
        [6379] = "Redis",
        [8080] = "HTTP-Alt",
        [8443] = "HTTPS-Alt",
        [27017] = "MongoDB"
    };

    /// <summary>
    /// Aggregates historical audit entries into a consolidated, deduplicated list of firewall rule items.
    /// </summary>
    /// <param name="entries">Raw network audit entries.</param>
    /// <param name="options">Filtering and consolidation options.</param>
    /// <returns>A sorted, indexed list of consolidated firewall rule items.</returns>
    public IReadOnlyList<FirewallRuleItem> Aggregate(
        IEnumerable<NetworkAuditEntry> entries,
        FirewallRuleExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(options);

        var groups = new Dictionary<(string Direction, string SourceIp, string DestIp, int DestPort, string Protocol, string Process, string Server), AggregationAccumulator>();

        var portFilter = new PortMatcher(options.IncludedPorts, options.ExcludedPorts);
        var ipMatcher = BuildIpMatcher(options.IncludedIps, options.ExcludedIps);

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            // A listening socket becomes an inbound rule from any source to its local port. It is exported only on
            // request: in the default matrix the established inbound flows already describe the real clients.
            // The direction decides, not the state text: a bound UDP socket has no "Listen" state ("UDP" on Windows,
            // "Close" on Linux).
            var isListener = entry.Socket.Direction == SocketFlowDirection.Listening;
            if (isListener != options.ListenersOnly)
            {
                continue;
            }

            var isOutbound = entry.Socket.Direction == SocketFlowDirection.Outbound;
            if (!isOutbound && !isListener && entry.Socket.Direction != SocketFlowDirection.Inbound)
            {
                continue;
            }

            var directionStr = isOutbound ? "Outbound" : "Inbound";
            if (string.Equals(options.Direction, "out", StringComparison.OrdinalIgnoreCase) && !isOutbound)
            {
                continue;
            }
            if (string.Equals(options.Direction, "in", StringComparison.OrdinalIgnoreCase) && isOutbound)
            {
                continue;
            }

            var sourceIp = isOutbound ? entry.Socket.LocalEndpoint.Address.ToString() : entry.Socket.RemoteEndpoint.Address.ToString();
            var destAddress = isOutbound ? entry.Socket.RemoteEndpoint.Address : entry.Socket.LocalEndpoint.Address;
            var destIp = destAddress.ToString();
            var destPort = isOutbound ? entry.Socket.RemoteEndpoint.Port : entry.Socket.LocalEndpoint.Port;

            // Ephemeral destination port exclusion
            if (options.ExcludeEphemeralPorts && destPort >= 49152)
            {
                continue;
            }

            // Port inclusion / exclusion
            if (!portFilter.IsAllowed(destPort))
            {
                continue;
            }

            // IP inclusion / exclusion
            if (!ipMatcher.IsAllowed(entry.Socket))
            {
                continue;
            }

            var processName = entry.Socket.Process.ProcessName;
            if (!IsProcessAllowed(processName, entry.Socket.Process.TargetExecutable, options.IncludedProcesses, options.ExcludedProcesses))
            {
                continue;
            }

            var protocolStr = entry.Socket.Protocol == SocketTransportProtocol.Udp ? "UDP" : "TCP";
            var key = (directionStr, sourceIp, destIp, destPort, protocolStr, processName, AuditedServerScope(entry, destAddress));

            if (!groups.TryGetValue(key, out var acc))
            {
                acc = new AggregationAccumulator
                {
                    IsOutbound = isOutbound,
                    IsListener = isListener,
                    SourceIp = sourceIp,
                    DestinationIp = destIp,
                    DestinationPort = destPort,
                    Protocol = protocolStr,
                    ProcessName = processName
                };
                groups[key] = acc;
            }

            acc.Observe(entry);
        }

        var results = new List<FirewallRuleItem>();
        var now = _timeProvider.GetUtcNow();

        foreach (var acc in groups.Values)
        {
            // The host patterns are tested once the whole group is read: a name known from one row only would
            // otherwise let the other rows of the same rule through an exclusion, or drop them from an inclusion.
            if (!IsAllowedByHostPatterns(acc, options))
            {
                continue;
            }

            if (acc.ObservationCount < options.MinObservations)
            {
                continue;
            }

            if (options.ActiveWithinDays.HasValue && options.ActiveWithinDays.Value > 0)
            {
                var cutoff = now.AddDays(-options.ActiveWithinDays.Value);
                if (acc.LastSeenUtc < cutoff)
                {
                    continue;
                }
            }

            var zone = NetworkZoneClassifier.ZoneOf(acc.DestinationIp);
            var isInternal = zone == NetworkZoneClassifier.Internal;

            if (string.Equals(options.DestinationZoneFilter, "internal", StringComparison.OrdinalIgnoreCase) && !isInternal)
            {
                continue;
            }
            if (string.Equals(options.DestinationZoneFilter, "public", StringComparison.OrdinalIgnoreCase) && isInternal)
            {
                continue;
            }

            var serviceName = DeduceServiceName(acc.DestinationPort, acc.Protocol, acc.ProcessName);

            results.Add(new FirewallRuleItem
            {
                RuleId = string.Empty, // Assigned sequentially after sorting
                Direction = acc.Direction,
                SourceIp = acc.SourceIp,
                SourceHost = acc.SourceHost,
                DestinationIp = acc.DestinationIp,
                DestinationHost = acc.DestinationHost,
                DestinationZone = zone,
                Protocol = acc.Protocol,
                DestinationPort = acc.DestinationPort,
                ServiceName = serviceName,
                ProcessName = acc.ProcessName,
                ExecutablePath = acc.ExecutablePath,
                UserName = acc.UserName,
                ObservationCount = acc.ObservationCount,
                FirstSeenUtc = acc.FirstSeenUtc,
                LastSeenUtc = acc.LastSeenUtc,
                AuditedServer = acc.ServerHostName
            });
        }

        // The sort covers every field of the group key, so the rule numbers do not depend on the order in which the
        // audit files were read. The process name is compared case-sensitively, as the key tells "Caddy" from "caddy".
        var sorted = results
            .OrderBy(r => r.Direction, StringComparer.Ordinal)
            .ThenBy(r => r.DestinationZone, StringComparer.Ordinal)
            .ThenBy(r => r.DestinationPort)
            .ThenBy(r => r.DestinationIp, IpAddressTextComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SourceIp, IpAddressTextComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Protocol, StringComparer.Ordinal)
            .ThenBy(r => r.ProcessName, StringComparer.Ordinal)
            .ThenBy(r => r.SourceHost, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.DestinationHost, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i] = sorted[i] with { RuleId = $"RULE-{(i + 1):D3}" };
        }

        return sorted;
    }

    // A loopback or unspecified destination names no machine: the same address in the logs of two servers is two
    // endpoints, so the audited server joins the rule key. A routable address names one machine whatever the log that
    // saw it, so the server stays out of the key.
    private static string AuditedServerScope(NetworkAuditEntry entry, IPAddress destination) =>
        !NetworkZoneClassifier.NamesOneMachine(destination)
            ? entry.Server.HostName.ToUpperInvariant()
            : string.Empty;

    private static string DeduceServiceName(int port, string protocol, string processName)
    {
        if (WellKnownServices.TryGetValue(port, out var wellKnown))
        {
            return wellKnown;
        }

        if (!string.IsNullOrWhiteSpace(processName) &&
            !string.Equals(processName, "Unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(processName, "System", StringComparison.OrdinalIgnoreCase))
        {
            return processName;
        }

        return $"{port}/{protocol}";
    }

    // As in the topology map, a pattern is tested against both ends of the flow, by name and by address, and one match
    // is enough to exclude the rule as to include it. The addresses are the ones the rule shows, where the map tests
    // the primary address of the server. The "Any" source of a listener is a label, not a host.
    private static bool IsAllowedByHostPatterns(AggregationAccumulator group, FirewallRuleExportOptions options)
    {
        string[] endpoints = [group.ServerHostName, group.RemoteHostName, group.SourceIp, group.DestinationIp];

        if (endpoints.Any(endpoint => MatchesAnyPattern(endpoint, options.ExcludedHosts)))
        {
            return false;
        }

        return options.IncludedHosts.Count == 0 ||
               endpoints.Any(endpoint => MatchesAnyPattern(endpoint, options.IncludedHosts));
    }

    private static bool IsProcessAllowed(string processName, string targetExecutable, IReadOnlyList<string> included, IReadOnlyList<string> excluded)
    {
        if (excluded.Count > 0 && (MatchesAnyPattern(processName, excluded) || MatchesAnyPattern(targetExecutable, excluded)))
        {
            return false;
        }

        if (included.Count > 0 && !(MatchesAnyPattern(processName, included) || MatchesAnyPattern(targetExecutable, included)))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesAnyPattern(string text, IReadOnlyList<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                if (text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace(@"\*", ".*", StringComparison.Ordinal)
                    .Replace(@"\?", ".", StringComparison.Ordinal) + "$";

                if (System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IpMatcher BuildIpMatcher(IReadOnlyList<IpAddressRange> included, IReadOnlyList<IpAddressRange> excluded)
    {
        var inc = included.Count > 0 ? IpAddressRangeMatcher.FromRanges(included) : null;
        var exc = excluded.Count > 0 ? IpAddressRangeMatcher.FromRanges(excluded) : null;
        return new IpMatcher(inc, exc);
    }

    private sealed class AggregationAccumulator
    {
        private readonly MostRecentlySeenValue _serverHostName = new();
        private readonly MostRecentlySeenValue _remoteHostName = new();
        private readonly MostRecentlySeenValue _executablePath = new();
        private readonly MostRecentlySeenValue _userName = new();

        public required bool IsOutbound { get; init; }
        public required bool IsListener { get; init; }
        public required string SourceIp { get; init; }
        public required string DestinationIp { get; init; }
        public required int DestinationPort { get; init; }
        public required string Protocol { get; init; }
        public required string ProcessName { get; init; }
        public string ExecutablePath => _executablePath.Value;
        public string UserName => _userName.Value;
        public string Direction => IsOutbound ? "Outbound" : "Inbound";
        public string ServerHostName => _serverHostName.Value;
        public string RemoteHostName => _remoteHostName.Value;
        public long ObservationCount { get; private set; }
        public DateTimeOffset FirstSeenUtc { get; private set; } = DateTimeOffset.MaxValue;
        public DateTimeOffset LastSeenUtc { get; private set; } = DateTimeOffset.MinValue;

        public string SourceHost
        {
            get
            {
                if (IsOutbound)
                {
                    return ServerHostName;
                }

                return IsListener ? AnySourceHost : RemoteHostNameOr(SourceIp);
            }
        }

        public string DestinationHost => IsOutbound ? RemoteHostNameOr(DestinationIp) : ServerHostName;

        public void Observe(NetworkAuditEntry entry)
        {
            ObservationCount += entry.ObservationCount;
            if (entry.FirstSeenUtc < FirstSeenUtc)
            {
                FirstSeenUtc = entry.FirstSeenUtc;
            }
            if (entry.LastSeenUtc > LastSeenUtc)
            {
                LastSeenUtc = entry.LastSeenUtc;
            }

            _serverHostName.Offer(entry.Server.HostName, entry.LastSeenUtc);
            _remoteHostName.Offer(entry.RemoteHostName, entry.LastSeenUtc);
            _executablePath.Offer(entry.Socket.Process.ExecutablePath, entry.LastSeenUtc);
            _userName.Offer(entry.Socket.Process.UserName, entry.LastSeenUtc);
        }

        private string RemoteHostNameOr(string remoteIp) => RemoteHostName.Length > 0 ? RemoteHostName : remoteIp;
    }

    // The first row of a flow is written before the DNS answer comes back, a server can be renamed and a program updated
    // or run under another account during an audit: a rule takes each name, path and account from whichever row
    // carries one. The most recently seen value wins and the ordinal order settles a tie, so that the matrix does not
    // depend on the order in which the audit files are read.
    private sealed class MostRecentlySeenValue
    {
        private DateTimeOffset _seenUtc = DateTimeOffset.MinValue;

        public string Value { get; private set; } = string.Empty;

        public void Offer(string value, DateTimeOffset seenUtc)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var isMoreRecent = seenUtc > _seenUtc;
            var settlesTie = seenUtc == _seenUtc && string.CompareOrdinal(value, Value) < 0;
            if (Value.Length == 0 || isMoreRecent || settlesTie)
            {
                Value = value;
                _seenUtc = seenUtc;
            }
        }
    }

    private sealed class PortMatcher(IReadOnlyList<NetworkPortRange> included, IReadOnlyList<NetworkPortRange> excluded)
    {
        public bool IsAllowed(int port)
        {
            if (Holds(excluded, port))
            {
                return false;
            }

            return included.Count == 0 || Holds(included, port);
        }

        // Called once per audit row: an indexed loop, where a LINQ query would allocate on each call.
        private static bool Holds(IReadOnlyList<NetworkPortRange> ranges, int port)
        {
            for (var i = 0; i < ranges.Count; i++)
            {
                if (ranges[i].Contains(port))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // As in the topology map, an address filter is tested against both ends of the flow, whatever its direction, and
    // one match is enough to exclude the rule as to include it: excluding a remote address must also drop the flows
    // it opens towards the server. A listening socket has no remote end (its remote address is the unset 0.0.0.0 or
    // ::), so only the address it is bound to is tested. The local address is the one the rule shows, where the map
    // tests the primary address of the server.
    private sealed class IpMatcher(IpAddressRangeMatcher? included, IpAddressRangeMatcher? excluded)
    {
        public bool IsAllowed(ActiveSocketConnection socket)
        {
            var hasRemoteEnd = socket.Direction != SocketFlowDirection.Listening;
            var localAddress = socket.LocalEndpoint.Address;
            var remoteAddress = socket.RemoteEndpoint.Address;

            if (excluded is not null &&
                (excluded.IsMatch(localAddress) || (hasRemoteEnd && excluded.IsMatch(remoteAddress))))
            {
                return false;
            }

            return included is null ||
                   included.IsMatch(localAddress) ||
                   (hasRemoteEnd && included.IsMatch(remoteAddress));
        }
    }
}
