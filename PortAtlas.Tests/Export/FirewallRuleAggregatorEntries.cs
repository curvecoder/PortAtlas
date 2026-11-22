using System.Net;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;

namespace PortAtlas.Tests.Export;

/// <summary>
/// Audit entries shared by the <see cref="PortAtlas.Core.Export.FirewallRuleAggregator"/> tests.
/// </summary>
internal static class FirewallRuleAggregatorEntries
{
    // The SMB service of nas.home.arpa, bound to 192.168.1.20 unless another address is given.
    public static NetworkAuditEntry CreateListenerEntry(string boundAddress = "192.168.1.20") =>
        CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: boundAddress,
            localPort: 445,
            remoteIp: boundAddress == "::" ? "::" : "0.0.0.0",
            remotePort: 0,
            serverHost: "nas.home.arpa",
            processName: "smbd",
            state: "Listen");

    // One flow between the server nas.home.arpa (192.168.1.20) and the remote host 192.168.1.53, on the DNS port.
    public static NetworkAuditEntry CreateRemoteNamedEntry(
        SocketFlowDirection direction,
        string remoteHost,
        DateTimeOffset lastSeen,
        long observationCount = 10,
        string serverHost = "nas.home.arpa",
        string executablePath = "/usr/bin/dnsproxy",
        string userName = "root")
    {
        var isOutbound = direction == SocketFlowDirection.Outbound;
        return CreateSampleEntry(
            direction: direction,
            localIp: "192.168.1.20",
            localPort: isOutbound ? 51000 : 53,
            remoteIp: "192.168.1.53",
            remotePort: isOutbound ? 53 : 51000,
            serverHost: serverHost,
            remoteHost: remoteHost,
            processName: "dnsproxy",
            observationCount: observationCount,
            firstSeen: lastSeen.AddMinutes(-1),
            lastSeen: lastSeen,
            executablePath: executablePath,
            userName: userName);
    }

    public static NetworkAuditEntry CreateSampleEntry(
        SocketFlowDirection direction,
        string localIp,
        int localPort,
        string remoteIp,
        int remotePort,
        string serverHost = "SRV-TEST",
        string remoteHost = "",
        string processName = "testproc",
        long observationCount = 10,
        DateTimeOffset? firstSeen = null,
        DateTimeOffset? lastSeen = null,
        string state = "Established",
        SocketTransportProtocol protocol = SocketTransportProtocol.Tcp,
        string? executablePath = null,
        string userName = "root")
    {
        var now = DateTimeOffset.UtcNow;
        var socket = new ActiveSocketConnection(
            protocol,
            direction,
            new SocketEndpoint(IPAddress.Parse(localIp), localPort),
            new SocketEndpoint(IPAddress.Parse(remoteIp), remotePort),
            state,
            new ProcessMetadata(
                1234, processName, executablePath ?? $"/usr/bin/{processName}", $"{processName} --run", "", userName));

        return new NetworkAuditEntry(
            FirstSeenUtc: firstSeen ?? now.AddHours(-1),
            LastSeenUtc: lastSeen ?? now,
            ObservationCount: observationCount,
            Server: new ServerHostMetadata(serverHost, "Linux", localIp),
            Socket: socket,
            RemoteHostName: remoteHost);
    }
}
