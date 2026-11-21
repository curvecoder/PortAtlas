using System.Globalization;
using System.Net;
using PortAtlas.Core.Export;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;

namespace PortAtlas.Tests.Export;

/// <summary>
/// Homelab rules and audit entries shared by the flow, pair and host rule matrix tests.
/// </summary>
internal static class FirewallMatrixSamples
{
    public static readonly DateTimeOffset Monday = DateTimeOffset.Parse("2026-10-05T08:00:00Z", CultureInfo.InvariantCulture);

    public static FirewallRuleItem Outbound(
        string sourceIp, string sourceHost, string destinationIp, string destinationHost, int port,
        string process = "curl", long observations = 10, DateTimeOffset? firstSeen = null, DateTimeOffset? lastSeen = null,
        string protocol = "TCP", string? serviceName = null) => new()
        {
            RuleId = "RULE-000",
            Direction = "Outbound",
            SourceIp = sourceIp,
            SourceHost = sourceHost,
            DestinationIp = destinationIp,
            DestinationHost = destinationHost,
            DestinationZone = NetworkZoneOf(destinationIp),
            Protocol = protocol,
            DestinationPort = port,
            ServiceName = serviceName ?? process,
            ProcessName = process,
            ObservationCount = observations,
            FirstSeenUtc = firstSeen ?? Monday,
            LastSeenUtc = lastSeen ?? Monday.AddHours(1),
            AuditedServer = sourceHost
        };

    public static FirewallRuleItem Inbound(
        string sourceIp, string sourceHost, string destinationIp, string destinationHost, int port,
        string process = "caddy", long observations = 8, DateTimeOffset? firstSeen = null, DateTimeOffset? lastSeen = null,
        string protocol = "TCP", string? serviceName = null) => new()
        {
            RuleId = "RULE-000",
            Direction = "Inbound",
            SourceIp = sourceIp,
            SourceHost = sourceHost,
            DestinationIp = destinationIp,
            DestinationHost = destinationHost,
            DestinationZone = NetworkZoneOf(destinationIp),
            Protocol = protocol,
            DestinationPort = port,
            ServiceName = serviceName ?? process,
            ProcessName = process,
            ObservationCount = observations,
            FirstSeenUtc = firstSeen ?? Monday,
            LastSeenUtc = lastSeen ?? Monday.AddHours(1),
            AuditedServer = destinationHost
        };

    public static NetworkAuditEntry Entry(string serverName, string localIp, DateTimeOffset? lastSeen = null) =>
        new(
            FirstSeenUtc: Monday,
            LastSeenUtc: lastSeen ?? Monday.AddHours(1),
            ObservationCount: 1,
            Server: new ServerHostMetadata(serverName, "Linux", localIp),
            Socket: new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse(localIp), 50000),
                new SocketEndpoint(IPAddress.Parse("192.168.1.2"), 53),
                "Established",
                new ProcessMetadata(1, "systemd-resolved", "/usr/lib/systemd/systemd-resolved", "systemd-resolved", "", "root")));

    public static AuditedServerDirectory Servers(params (string Name, string Ip)[] servers) =>
        AuditedServerDirectory.FromEntries(servers.Select(server => Entry(server.Name, server.Ip)));

    private static string NetworkZoneOf(string ip) =>
        ip.StartsWith("192.168.", StringComparison.Ordinal) || ip.StartsWith("127.", StringComparison.Ordinal) ? "Internal" : "Public";
}
