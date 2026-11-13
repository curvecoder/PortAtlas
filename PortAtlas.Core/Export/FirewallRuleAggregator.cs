using System.Net;
using PortAtlas.Core.Filtering;
using PortAtlas.Core.Models;
using SocketForge.Models;

namespace PortAtlas.Core.Export;

/// <summary>
/// Normalizes and aggregates raw network connection flows into consolidated firewall rule records.
/// </summary>
public sealed class FirewallRuleAggregator
{
    private const string AnySourceHost = "Any";

    private static readonly IpAddressRangeMatcher PrivateNetworkMatcher = new(
    [
        "10.0.0.0/8",
        "172.16.0.0/12",
        "192.168.0.0/16",
        "127.0.0.0/8",
        "169.254.0.0/16",
        "::1/128",
        "fc00::/7",
        "fe80::/10"
    ]);

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

        var groups = new Dictionary<(string Direction, string SourceIp, string DestIp, int DestPort, string Protocol, string Process), AggregationAccumulator>();

        var portFilter = BuildPortMatcher(options.IncludedPorts, options.ExcludedPorts);
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
            var destIp = isOutbound ? entry.Socket.RemoteEndpoint.Address.ToString() : entry.Socket.LocalEndpoint.Address.ToString();
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
            if (IPAddress.TryParse(destIp, out var parsedDestIp) && !ipMatcher.IsAllowed(parsedDestIp))
            {
                continue;
            }

            var processName = entry.Socket.Process.ProcessName;
            if (!IsProcessAllowed(processName, entry.Socket.Process.TargetExecutable, options.IncludedProcesses, options.ExcludedProcesses))
            {
                continue;
            }

            var protocolStr = entry.Socket.Protocol == SocketTransportProtocol.Udp ? "UDP" : "TCP";
            var key = (directionStr, sourceIp, destIp, destPort, protocolStr, processName);

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
                    ProcessName = processName,
                    ExecutablePath = entry.Socket.Process.ExecutablePath,
                    UserName = entry.Socket.Process.UserName
                };
                groups[key] = acc;
            }

            acc.Observe(entry);
        }

        var results = new List<FirewallRuleItem>();
        var now = DateTimeOffset.UtcNow;

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

            // A listener bound to 0.0.0.0 or :: is reached through the server's own addresses, hence internal.
            var isInternal = IPAddress.TryParse(acc.DestinationIp, out var parsedIp) &&
                             (IPAddress.IsLoopback(parsedIp) || PrivateNetworkMatcher.IsMatch(parsedIp) ||
                              parsedIp.Equals(IPAddress.Any) || parsedIp.Equals(IPAddress.IPv6Any));
            var zone = isInternal ? "Internal" : "Public";

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
                LastSeenUtc = acc.LastSeenUtc
            });
        }

        // Deterministic sorting for firewall rule consistency
        var sorted = results
            .OrderBy(r => r.Direction)
            .ThenBy(r => r.DestinationZone)
            .ThenBy(r => r.DestinationPort)
            .ThenBy(r => r.DestinationIp, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SourceIp, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (var i = 0; i < sorted.Count; i++)
        {
            sorted[i] = sorted[i] with { RuleId = $"FLUX-{(i + 1):D3}" };
        }

        return sorted;
    }

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

    private static PortMatcher BuildPortMatcher(IReadOnlyList<string> included, IReadOnlyList<string> excluded)
    {
        var inc = ParsePortTokens(included);
        var exc = ParsePortTokens(excluded);
        return new PortMatcher(inc, exc);
    }

    private static HashSet<int> ParsePortTokens(IReadOnlyList<string> tokens)
    {
        var result = new HashSet<int>();
        foreach (var token in tokens)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var parts = token.Split('-', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end))
            {
                for (var p = start; p <= end; p++)
                {
                    result.Add(p);
                }
            }
            else if (int.TryParse(token, out var single))
            {
                result.Add(single);
            }
        }
        return result;
    }

    private static IpMatcher BuildIpMatcher(IReadOnlyList<string> included, IReadOnlyList<string> excluded)
    {
        var inc = included.Count > 0 ? new IpAddressRangeMatcher(included) : null;
        var exc = excluded.Count > 0 ? new IpAddressRangeMatcher(excluded) : null;
        return new IpMatcher(inc, exc);
    }

    private sealed class AggregationAccumulator
    {
        private readonly MostRecentlySeenName _serverHostName = new();
        private readonly MostRecentlySeenName _remoteHostName = new();

        public required bool IsOutbound { get; init; }
        public required bool IsListener { get; init; }
        public required string SourceIp { get; init; }
        public required string DestinationIp { get; init; }
        public required int DestinationPort { get; init; }
        public required string Protocol { get; init; }
        public required string ProcessName { get; init; }
        public required string ExecutablePath { get; init; }
        public required string UserName { get; init; }
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
        }

        private string RemoteHostNameOr(string remoteIp) => RemoteHostName.Length > 0 ? RemoteHostName : remoteIp;
    }

    // The first row of a flow is written before the DNS answer comes back, and a server can be renamed during an
    // audit: a rule takes each name from whichever row carries it. The most recently seen name wins and the ordinal
    // order settles a tie, so that the matrix does not depend on the order in which the audit files are read.
    private sealed class MostRecentlySeenName
    {
        private DateTimeOffset _seenUtc = DateTimeOffset.MinValue;

        public string Value { get; private set; } = string.Empty;

        public void Offer(string name, DateTimeOffset seenUtc)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            var isMoreRecent = seenUtc > _seenUtc;
            var settlesTie = seenUtc == _seenUtc && string.CompareOrdinal(name, Value) < 0;
            if (Value.Length == 0 || isMoreRecent || settlesTie)
            {
                Value = name;
                _seenUtc = seenUtc;
            }
        }
    }

    private sealed class PortMatcher(HashSet<int> included, HashSet<int> excluded)
    {
        public bool IsAllowed(int port)
        {
            if (excluded.Contains(port))
            {
                return false;
            }
            if (included.Count > 0 && !included.Contains(port))
            {
                return false;
            }
            return true;
        }
    }

    private sealed class IpMatcher(IpAddressRangeMatcher? included, IpAddressRangeMatcher? excluded)
    {
        public bool IsAllowed(IPAddress ip)
        {
            if (excluded is not null && excluded.IsMatch(ip))
            {
                return false;
            }
            if (included is not null && !included.IsMatch(ip))
            {
                return false;
            }
            return true;
        }
    }
}
