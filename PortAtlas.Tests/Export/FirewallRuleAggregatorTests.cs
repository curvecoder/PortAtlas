using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Export;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class FirewallRuleAggregatorTests
{
    [Fact]
    public void Aggregate_WhenOutboundFlow_NormalizesSourceAndDestinationCorrectly()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 52140,
            remoteIp: "10.20.1.5",
            remotePort: 1521,
            serverHost: "SRV-APP-01",
            remoteHost: "db-oracle.prod",
            processName: "tnslsnr",
            observationCount: 150);

        var options = new FirewallRuleExportOptions();

        // Act
        var rules = aggregator.Aggregate([entry], options);

        // Assert
        rules.Should().HaveCount(1);
        var rule = rules[0];
        rule.RuleId.Should().Be("FLUX-001");
        rule.Direction.Should().Be("Outbound");
        rule.SourceIp.Should().Be("10.0.0.10");
        rule.SourceHost.Should().Be("SRV-APP-01");
        rule.DestinationIp.Should().Be("10.20.1.5");
        rule.DestinationHost.Should().Be("db-oracle.prod");
        rule.DestinationPort.Should().Be(1521);
        rule.DestinationZone.Should().Be("Internal");
        rule.ServiceName.Should().Be("Oracle-TNS");
        rule.ProcessName.Should().Be("tnslsnr");
        rule.ObservationCount.Should().Be(150);
    }

    [Fact]
    public void Aggregate_WhenListenersOnlyAndSocketListens_ReturnsInboundRuleFromAnySource()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: "10.0.0.10",
            localPort: 8443,
            remoteIp: "0.0.0.0",
            remotePort: 0,
            serverHost: "SRV-WEB-01",
            processName: "nginx",
            state: "Listen");

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ListenersOnly = true });

        // Assert
        var rule = rules.Should().ContainSingle().Subject;
        rule.Direction.Should().Be("Inbound");
        rule.SourceIp.Should().Be("0.0.0.0");
        rule.SourceHost.Should().Be("Any");
        rule.DestinationIp.Should().Be("10.0.0.10");
        rule.DestinationHost.Should().Be("SRV-WEB-01");
        rule.DestinationPort.Should().Be(8443);
        rule.DestinationZone.Should().Be("Internal");
        rule.ProcessName.Should().Be("nginx");
    }

    [Fact]
    public void Aggregate_WhenListenerIsBoundToAllInterfaces_ClassifiesDestinationAsInternal()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: "::",
            localPort: 22,
            remoteIp: "::",
            remotePort: 0,
            serverHost: "SRV-SSH-01",
            processName: "sshd",
            state: "Listen");

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ListenersOnly = true });

        // Assert
        var rule = rules.Should().ContainSingle().Subject;
        rule.DestinationZone.Should().Be("Internal");
        rule.DestinationHost.Should().Be("SRV-SSH-01");
    }

    // A bound UDP socket has no TCP state: the Windows scanner reports "UDP", /proc/net/udp reports state 07 ("Close").
    [Theory]
    [InlineData("UDP")]
    [InlineData("Close")]
    public void Aggregate_WhenListenersOnlyAndUdpSocketIsBound_ReturnsInboundUdpRuleFromAnySource(string state)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: "0.0.0.0",
            localPort: 53,
            remoteIp: "0.0.0.0",
            remotePort: 0,
            serverHost: "SRV-DNS-01",
            processName: "dnsmasq",
            state: state,
            protocol: SocketTransportProtocol.Udp);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ListenersOnly = true });

        // Assert
        var rule = rules.Should().ContainSingle().Subject;
        rule.Direction.Should().Be("Inbound");
        rule.Protocol.Should().Be("UDP");
        rule.SourceHost.Should().Be("Any");
        rule.DestinationHost.Should().Be("SRV-DNS-01");
        rule.DestinationPort.Should().Be(53);
        rule.ServiceName.Should().Be("DNS");
    }

    // The state text alone never makes a listener: a log row whose direction cannot be read is loaded as outbound
    // and keeps its "Listen" state, and would otherwise become a rule towards 0.0.0.0:0.
    [Theory]
    [InlineData(SocketFlowDirection.Inbound, "Established")]
    [InlineData(SocketFlowDirection.Outbound, "Established")]
    [InlineData(SocketFlowDirection.Inbound, "Listen")]
    [InlineData(SocketFlowDirection.Outbound, "Listen")]
    public void Aggregate_WhenListenersOnlyAndSocketIsNotListening_IgnoresTheFlow(SocketFlowDirection direction, string state)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: direction,
            localIp: "10.0.0.10",
            localPort: 443,
            remoteIp: "10.20.1.5",
            remotePort: 8443,
            state: state);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ListenersOnly = true });

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenListenersOnlyIsNotSet_IgnoresListeningSockets()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: "10.0.0.10",
            localPort: 8443,
            remoteIp: "0.0.0.0",
            remotePort: 0,
            state: "Listen");

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions());

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenInboundFlow_InvertsSourceAndDestinationToMatchRealTraffic()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Inbound,
            localIp: "10.0.0.10",
            localPort: 443,
            remoteIp: "192.168.1.100",
            remotePort: 60123,
            serverHost: "SRV-WEB-01",
            remoteHost: "client.local",
            processName: "nginx",
            observationCount: 50);

        var options = new FirewallRuleExportOptions();

        // Act
        var rules = aggregator.Aggregate([entry], options);

        // Assert
        rules.Should().HaveCount(1);
        var rule = rules[0];
        rule.Direction.Should().Be("Inbound");
        rule.SourceIp.Should().Be("192.168.1.100");
        rule.SourceHost.Should().Be("client.local");
        rule.DestinationIp.Should().Be("10.0.0.10");
        rule.DestinationHost.Should().Be("SRV-WEB-01");
        rule.DestinationPort.Should().Be(443);
        rule.ServiceName.Should().Be("HTTPS");
    }

    [Fact]
    public void Aggregate_WhenMultipleEntriesShareSameTuple_ConsolidatesObservationsAndDates()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var time1 = DateTimeOffset.UtcNow.AddHours(-3);
        var time2 = DateTimeOffset.UtcNow.AddHours(-1);

        var entry1 = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 50001,
            remoteIp: "10.20.1.5",
            remotePort: 443,
            firstSeen: time1,
            lastSeen: time1.AddMinutes(10),
            observationCount: 20);

        var entry2 = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 50002,
            remoteIp: "10.20.1.5",
            remotePort: 443,
            firstSeen: time2,
            lastSeen: time2.AddMinutes(5),
            observationCount: 30);

        var options = new FirewallRuleExportOptions();

        // Act
        var rules = aggregator.Aggregate([entry1, entry2], options);

        // Assert
        rules.Should().HaveCount(1);
        var rule = rules[0];
        rule.ObservationCount.Should().Be(50);
        rule.FirstSeenUtc.Should().Be(time1);
        rule.LastSeenUtc.Should().Be(time2.AddMinutes(5));
    }

    [Fact]
    public void Aggregate_WhenDestinationPortIsEphemeralAndExcludeEphemeralEnabled_FiltersRuleOut()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 45000,
            remoteIp: "10.20.1.5",
            remotePort: 55000); // Ephemeral destination port

        var options = new FirewallRuleExportOptions { ExcludeEphemeralPorts = true };

        // Act
        var rules = aggregator.Aggregate([entry], options);

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenObservationCountBelowThreshold_FiltersRuleOut()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 50000,
            remoteIp: "10.20.1.5",
            remotePort: 443,
            observationCount: 3);

        var options = new FirewallRuleExportOptions { MinObservations = 5 };

        // Act
        var rules = aggregator.Aggregate([entry], options);

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenInternalOnlyFilterActiveAndTargetIsPublic_FiltersRuleOut()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var internalEntry = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 50000,
            remoteIp: "10.20.1.5",
            remotePort: 443);

        var publicEntry = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "10.0.0.10",
            localPort: 50001,
            remoteIp: "8.8.8.8",
            remotePort: 53);

        var options = new FirewallRuleExportOptions { DestinationZoneFilter = "internal" };

        // Act
        var rules = aggregator.Aggregate([internalEntry, publicEntry], options);

        // Assert
        rules.Should().HaveCount(1);
        rules[0].DestinationIp.Should().Be("10.20.1.5");
    }

    // The first row of a flow is written before the DNS answer comes back: only a later row carries the name.
    [Theory]
    [InlineData(SocketFlowDirection.Outbound)]
    [InlineData(SocketFlowDirection.Inbound)]
    public void Aggregate_WhenOnlyALaterRowCarriesTheRemoteName_NamesTheRemoteEndOfTheRule(SocketFlowDirection direction)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var unnamed = CreateRemoteNamedEntry(direction, remoteHost: "", lastSeen: seen);
        var named = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: seen.AddMinutes(5));

        // Act
        var rules = aggregator.Aggregate([unnamed, named], new FirewallRuleExportOptions());

        // Assert
        var rule = rules.Should().ContainSingle().Subject;
        var remoteHost = direction == SocketFlowDirection.Outbound ? rule.DestinationHost : rule.SourceHost;
        remoteHost.Should().Be("pihole.home.arpa");
    }

    // A server renamed during the audit, or read from two logs: the rule must not take the name of the first file read.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenRowsCarryDifferentServerNames_KeepsTheMostRecentlySeenName(bool recentRowFirst)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var old = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen, serverHost: "old-nas.home.arpa");
        var recent = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen.AddMinutes(5), serverHost: "nas.home.arpa");
        NetworkAuditEntry[] entries = recentRowFirst ? [recent, old] : [old, recent];

        // Act
        var rules = aggregator.Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        rules.Should().ContainSingle().Which.SourceHost.Should().Be("nas.home.arpa");
    }

    [Fact]
    public void Aggregate_WhenExcludedHostPatternMatchesTheAddressOfAnUnnamedRemoteHost_RemovesTheRule()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ExcludedHosts = ["192.168.1.53"] });

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenExcludedHostPatternMatchesOneRuleOnly_KeepsTheOtherRule()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var toPihole = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);
        var toProxmox = CreateSampleEntry(
            direction: SocketFlowDirection.Outbound,
            localIp: "192.168.1.20",
            localPort: 51001,
            remoteIp: "192.168.1.10",
            remotePort: 8006,
            serverHost: "nas.home.arpa",
            remoteHost: "proxmox.home.arpa");

        // Act
        var rules = aggregator.Aggregate([toPihole, toProxmox], new FirewallRuleExportOptions { ExcludedHosts = ["pihole*"] });

        // Assert
        rules.Should().ContainSingle().Which.DestinationHost.Should().Be("proxmox.home.arpa");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Aggregate_WhenALaterRowHasNoRemoteName_KeepsTheNameAlreadyRead(string missingName)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var named = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen);
        var unnamed = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: missingName, lastSeen: seen.AddMinutes(5));

        // Act
        var rules = aggregator.Aggregate([named, unnamed], new FirewallRuleExportOptions());

        // Assert
        rules.Should().ContainSingle().Which.DestinationHost.Should().Be("pihole.home.arpa");
    }

    // The matrix must not depend on the order in which the audit files are read.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenRowsCarryDifferentRemoteNames_KeepsTheMostRecentlySeenName(bool recentRowFirst)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var old = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "old-name.home.arpa", lastSeen: seen);
        var recent = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "new-name.home.arpa", lastSeen: seen.AddMinutes(5));
        NetworkAuditEntry[] entries = recentRowFirst ? [recent, old] : [old, recent];

        // Act
        var rules = aggregator.Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        rules.Should().ContainSingle().Which.DestinationHost.Should().Be("new-name.home.arpa");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenTwoRemoteNamesWereSeenAtTheSameInstant_KeepsTheFirstInOrdinalOrder(bool reversed)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var first = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "adguard.home.arpa", lastSeen: seen);
        var second = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen);
        NetworkAuditEntry[] entries = reversed ? [second, first] : [first, second];

        // Act
        var rules = aggregator.Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        rules.Should().ContainSingle().Which.DestinationHost.Should().Be("adguard.home.arpa");
    }

    [Fact]
    public void Aggregate_WhenExcludedHostNameIsCarriedByOneRowOnly_RemovesTheWholeRule()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var unnamed = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "", lastSeen: seen);
        var named = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen.AddMinutes(5));

        // Act
        var rules = aggregator.Aggregate([unnamed, named], new FirewallRuleExportOptions { ExcludedHosts = ["pihole*"] });

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenIncludedHostNameIsCarriedByOneRowOnly_CountsEveryRowOfTheRule()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var unnamed = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "", lastSeen: seen, observationCount: 10);
        var named = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen.AddMinutes(5), observationCount: 30);

        // Act
        var rules = aggregator.Aggregate([unnamed, named], new FirewallRuleExportOptions { IncludedHosts = ["pihole*"] });

        // Assert
        rules.Should().ContainSingle().Which.ObservationCount.Should().Be(40);
    }

    // Same rule as the topology map: one end of the flow is enough, by its name or by its address.
    [Theory]
    [InlineData(SocketFlowDirection.Outbound, "nas*")]
    [InlineData(SocketFlowDirection.Outbound, "pihole*")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.53")]
    [InlineData(SocketFlowDirection.Inbound, "nas*")]
    [InlineData(SocketFlowDirection.Inbound, "pihole*")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.53")]
    public void Aggregate_WhenIncludedHostPatternMatchesOneEndOfTheFlow_KeepsTheRule(SocketFlowDirection direction, string pattern)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { IncludedHosts = [pattern] });

        // Assert
        rules.Should().ContainSingle();
    }

    [Theory]
    [InlineData(SocketFlowDirection.Outbound, "nas*")]
    [InlineData(SocketFlowDirection.Outbound, "pihole*")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.53")]
    [InlineData(SocketFlowDirection.Inbound, "nas*")]
    [InlineData(SocketFlowDirection.Inbound, "pihole*")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.53")]
    public void Aggregate_WhenExcludedHostPatternMatchesOneEndOfTheFlow_RemovesTheRule(SocketFlowDirection direction, string pattern)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ExcludedHosts = [pattern] });

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenIncludedHostPatternMatchesNoEndOfTheFlow_RemovesTheRule()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { IncludedHosts = ["proxmox*"] });

        // Assert
        rules.Should().BeEmpty();
    }

    // The source of a listener rule is the placeholder "Any": it must not be what the patterns are tested against.
    [Fact]
    public void Aggregate_WhenListenersOnlyAndIncludedHostPatternMatchesTheServer_KeepsTheListener()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: "192.168.1.20",
            localPort: 445,
            remoteIp: "0.0.0.0",
            remotePort: 0,
            serverHost: "nas.home.arpa",
            processName: "smbd",
            state: "Listen");

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, IncludedHosts = ["nas*"] });

        // Assert
        rules.Should().ContainSingle().Which.SourceHost.Should().Be("Any");
    }

    [Fact]
    public void Aggregate_WhenListenersOnlyAndExcludedHostPatternMatchesTheAnySourceLabel_KeepsTheListener()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateSampleEntry(
            direction: SocketFlowDirection.Listening,
            localIp: "192.168.1.20",
            localPort: 445,
            remoteIp: "0.0.0.0",
            remotePort: 0,
            serverHost: "nas.home.arpa",
            processName: "smbd",
            state: "Listen");

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, ExcludedHosts = ["Any"] });

        // Assert
        rules.Should().ContainSingle();
    }

    // One flow between the server nas.home.arpa (192.168.1.20) and the remote host 192.168.1.53, on the DNS port.
    private static NetworkAuditEntry CreateRemoteNamedEntry(
        SocketFlowDirection direction,
        string remoteHost,
        DateTimeOffset lastSeen,
        long observationCount = 10,
        string serverHost = "nas.home.arpa")
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
            lastSeen: lastSeen);
    }

    private static NetworkAuditEntry CreateSampleEntry(
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
        SocketTransportProtocol protocol = SocketTransportProtocol.Tcp)
    {
        var now = DateTimeOffset.UtcNow;
        var socket = new ActiveSocketConnection(
            protocol,
            direction,
            new SocketEndpoint(IPAddress.Parse(localIp), localPort),
            new SocketEndpoint(IPAddress.Parse(remoteIp), remotePort),
            state,
            new ProcessMetadata(1234, processName, $"/usr/bin/{processName}", $"{processName} --run", "", "root"));

        return new NetworkAuditEntry(
            FirstSeenUtc: firstSeen ?? now.AddHours(-1),
            LastSeenUtc: lastSeen ?? now,
            ObservationCount: observationCount,
            Server: new ServerHostMetadata(serverHost, "Linux", localIp),
            Socket: socket,
            RemoteHostName: remoteHost);
    }
}
