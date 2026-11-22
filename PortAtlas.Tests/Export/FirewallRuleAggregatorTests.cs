using AwesomeAssertions;
using PortAtlas.Core.Export;
using PortAtlas.Core.Models;
using SocketForge.Models;
using Xunit;
using static PortAtlas.Tests.Export.FirewallRuleAggregatorEntries;

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
        rule.RuleId.Should().Be("RULE-001");
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

    // A program updated or run under another account during the audit: the rule must not take the path or the
    // account of the first file read.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenRowsCarryDifferentExecutablesAndAccounts_KeepsTheMostRecentlySeenOnes(bool recentRowFirst)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var old = CreateRemoteNamedEntry(
            SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen,
            executablePath: "/opt/dnsproxy-0.9/dnsproxy", userName: "root");
        var recent = CreateRemoteNamedEntry(
            SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen.AddMinutes(5),
            executablePath: "/opt/dnsproxy-1.0/dnsproxy", userName: "dnsproxy");
        NetworkAuditEntry[] entries = recentRowFirst ? [recent, old] : [old, recent];

        // Act
        var rules = aggregator.Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        var rule = rules.Should().ContainSingle().Subject;
        rule.ExecutablePath.Should().Be("/opt/dnsproxy-1.0/dnsproxy");
        rule.UserName.Should().Be("dnsproxy");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenTheMostRecentRowHasNoExecutableOrAccount_KeepsTheOnesOfAnotherRow(bool recentRowFirst)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var seen = DateTimeOffset.UtcNow.AddHours(-2);
        var known = CreateRemoteNamedEntry(
            SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen,
            executablePath: "/opt/dnsproxy/dnsproxy", userName: "dnsproxy");
        var unknown = CreateRemoteNamedEntry(
            SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: seen.AddMinutes(5),
            executablePath: "", userName: "  ");
        NetworkAuditEntry[] entries = recentRowFirst ? [unknown, known] : [known, unknown];

        // Act
        var rules = aggregator.Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        var rule = rules.Should().ContainSingle().Subject;
        rule.ExecutablePath.Should().Be("/opt/dnsproxy/dnsproxy");
        rule.UserName.Should().Be("dnsproxy");
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
    public void Aggregate_WhenDestinationsDifferInTheLastByte_NumbersThemInAddressOrder()
    {
        // Arrange
        NetworkAuditEntry[] entries =
        [
            CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51000, "192.168.1.10", 443),
            CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51001, "192.168.1.2", 443)
        ];

        // Act
        var rules = new FirewallRuleAggregator().Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        rules.Select(rule => (rule.RuleId, rule.DestinationIp)).Should().Equal(("RULE-001", "192.168.1.2"), ("RULE-002", "192.168.1.10"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenRulesDifferOnlyByProtocol_NumbersThemInProtocolOrderWhateverTheInputOrder(bool reversed)
    {
        // Arrange
        var tcp = CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51000, "192.168.1.2", 53, protocol: SocketTransportProtocol.Tcp);
        var udp = CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51000, "192.168.1.2", 53, protocol: SocketTransportProtocol.Udp);
        NetworkAuditEntry[] entries = reversed ? [udp, tcp] : [tcp, udp];

        // Act
        var rules = new FirewallRuleAggregator().Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        rules.Select(rule => (rule.RuleId, rule.Protocol)).Should().Equal(("RULE-001", "TCP"), ("RULE-002", "UDP"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenRulesDifferOnlyByProcess_NumbersThemInOrdinalProcessOrderWhateverTheInputOrder(bool reversed)
    {
        // Arrange
        var nginx = CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51000, "192.168.1.50", 443, processName: "nginx");
        var caddy = CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51000, "192.168.1.50", 443, processName: "caddy");
        var upperCaddy = CreateSampleEntry(SocketFlowDirection.Outbound, "192.168.1.20", 51000, "192.168.1.50", 443, processName: "Caddy");
        NetworkAuditEntry[] entries = reversed ? [upperCaddy, nginx, caddy] : [caddy, nginx, upperCaddy];

        // Act
        var rules = new FirewallRuleAggregator().Aggregate(entries, new FirewallRuleExportOptions());

        // Assert
        rules.Select(rule => (rule.RuleId, rule.ProcessName))
            .Should().Equal(("RULE-001", "Caddy"), ("RULE-002", "caddy"), ("RULE-003", "nginx"));
    }

    [Fact]
    public void Aggregate_WhenTwoServersConnectOverLoopback_KeepsOneRulePerServer()
    {
        // Arrange
        var nas = CreateSampleEntry(SocketFlowDirection.Outbound, "127.0.0.1", 51000, "127.0.0.1", 5432, serverHost: "nas.home.arpa", processName: "psql");
        var pihole = CreateSampleEntry(SocketFlowDirection.Outbound, "127.0.0.1", 51000, "127.0.0.1", 5432, serverHost: "pihole.home.arpa", processName: "psql");

        // Act
        var rules = new FirewallRuleAggregator().Aggregate([nas, pihole], new FirewallRuleExportOptions());

        // Assert
        rules.Select(rule => rule.SourceHost).Should().BeEquivalentTo(["nas.home.arpa", "pihole.home.arpa"]);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Aggregate_WhenListenersOnlyAndTwoServersListenOnTheUnspecifiedAddress_KeepsOneRulePerServer(string boundAddress)
    {
        // Arrange
        var remoteAddress = boundAddress == "::" ? "::" : "0.0.0.0";
        var nas = CreateSampleEntry(SocketFlowDirection.Listening, boundAddress, 22, remoteAddress, 0, serverHost: "nas.home.arpa", processName: "sshd", state: "Listen");
        var pihole = CreateSampleEntry(SocketFlowDirection.Listening, boundAddress, 22, remoteAddress, 0, serverHost: "pihole.home.arpa", processName: "sshd", state: "Listen");

        // Act
        var rules = new FirewallRuleAggregator().Aggregate([nas, pihole], new FirewallRuleExportOptions { ListenersOnly = true });

        // Assert
        rules.Select(rule => rule.DestinationHost).Should().BeEquivalentTo(["nas.home.arpa", "pihole.home.arpa"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aggregate_WhenRulesDifferOnlyByServer_NumbersThemInServerOrderWhateverTheInputOrder(bool reversed)
    {
        // Arrange
        var pihole = CreateSampleEntry(SocketFlowDirection.Listening, "0.0.0.0", 22, "0.0.0.0", 0, serverHost: "pihole.home.arpa", processName: "sshd", state: "Listen");
        var nas = CreateSampleEntry(SocketFlowDirection.Listening, "0.0.0.0", 22, "0.0.0.0", 0, serverHost: "nas.home.arpa", processName: "sshd", state: "Listen");
        NetworkAuditEntry[] entries = reversed ? [nas, pihole] : [pihole, nas];

        // Act
        var rules = new FirewallRuleAggregator().Aggregate(entries, new FirewallRuleExportOptions { ListenersOnly = true });

        // Assert
        rules.Select(rule => (rule.RuleId, rule.DestinationHost))
            .Should().Equal(("RULE-001", "nas.home.arpa"), ("RULE-002", "pihole.home.arpa"));
    }
}
