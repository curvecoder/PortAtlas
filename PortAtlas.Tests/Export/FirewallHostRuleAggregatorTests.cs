using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallHostRuleAggregatorTests
{
    [Fact]
    public void Aggregate_WhenBothEndsRecordAFlow_GivesAnObservedRuleToEachEnd()
    {
        // Arrange
        var servers = Servers(("desktop", "192.168.1.10"), ("nas", "192.168.1.50"));
        var flows = Flows(
            servers,
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "curl", 10),
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "caddy", 8));

        // Act
        var rules = new FirewallHostRuleAggregator().Aggregate(flows, servers);

        // Assert
        rules.Should().HaveCount(2);
        var desktop = rules[0];
        desktop.HostRuleId.Should().Be("HOST-001");
        desktop.Host.Should().Be("desktop");
        desktop.HostIp.Should().Be("192.168.1.10");
        desktop.IsAudited.Should().BeTrue();
        desktop.Direction.Should().Be("Outbound");
        desktop.PeerIp.Should().Be("192.168.1.50");
        desktop.PeerHost.Should().Be("nas");
        desktop.PeerZone.Should().Be("Internal");
        desktop.Protocol.Should().Be("TCP");
        desktop.Port.Should().Be(443);
        desktop.LocalProcesses.Should().Equal("curl");
        desktop.Evidence.Should().Be(HostRuleEvidence.Observed);
        desktop.ObservationCount.Should().Be(10);
        desktop.FlowId.Should().Be("FLOW-001");
        var nas = rules[1];
        nas.HostRuleId.Should().Be("HOST-002");
        nas.Host.Should().Be("nas");
        nas.Direction.Should().Be("Inbound");
        nas.PeerIp.Should().Be("192.168.1.10");
        nas.Port.Should().Be(443);
        nas.LocalProcesses.Should().Equal("caddy");
        nas.Evidence.Should().Be(HostRuleEvidence.Observed);
        nas.ObservationCount.Should().Be(8);
    }

    [Fact]
    public void Aggregate_WhenOnlyTheSourceRecordsAFlow_InfersTheRuleOfTheDestinationFromTheSourceLog()
    {
        // Arrange
        var servers = Servers(("desktop", "192.168.1.10"));
        var flows = Flows(servers, Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas.home.arpa", 445, "System", 12));

        // Act
        var nas = new FirewallHostRuleAggregator().Aggregate(flows, servers).Single(r => r.HostIp == "192.168.1.50");

        // Assert
        nas.Host.Should().Be("nas.home.arpa");
        nas.IsAudited.Should().BeFalse();
        nas.Direction.Should().Be("Inbound");
        nas.Evidence.Should().Be(HostRuleEvidence.Inferred);
        nas.LocalProcesses.Should().BeEmpty();
        nas.ObservationCount.Should().Be(12);
    }

    // A public address carries no security group of ours: it only shows as the peer of an internal host.
    [Fact]
    public void Aggregate_WhenTheDestinationIsPublic_GivesARuleToTheSourceOnly()
    {
        // Arrange
        var flows = Flows(AuditedServerDirectory.Empty, Outbound("192.168.1.10", "desktop", "203.0.113.10", "mirror.example.org", 443));

        // Act
        var rule = new FirewallHostRuleAggregator().Aggregate(flows, AuditedServerDirectory.Empty).Should().ContainSingle().Subject;

        // Assert
        rule.HostIp.Should().Be("192.168.1.10");
        rule.PeerZone.Should().Be("Public");
    }

    // A loopback flow never leaves the server: no network security group sees it.
    [Fact]
    public void Aggregate_WhenTheFlowStaysOnTheLoopback_GivesNoRule()
    {
        // Arrange
        var flows = Flows(AuditedServerDirectory.Empty, Outbound("127.0.0.1", "nas", "127.0.0.1", "127.0.0.1", 5432, "gitea"));

        // Act
        var rules = new FirewallHostRuleAggregator().Aggregate(flows, AuditedServerDirectory.Empty);

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenAHostHasSeveralRules_SortsThemByHostThenInboundFirstThenPort()
    {
        // Arrange
        var flows = Flows(
            AuditedServerDirectory.Empty,
            Outbound("192.168.1.50", "nas", "192.168.1.2", "pihole", 53, "systemd-resolved"),
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 873, "rsync"),
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "caddy"));

        // Act
        var rules = new FirewallHostRuleAggregator().Aggregate(flows, AuditedServerDirectory.Empty);

        // Assert
        rules.Where(r => r.Host == "nas").Select(r => $"{r.HostRuleId} {r.Direction} {r.Port}").Should().Equal(
            "HOST-003 Inbound 443",
            "HOST-004 Inbound 873",
            "HOST-005 Outbound 53");
    }

    [Fact]
    public void Aggregate_WhenPeersDifferInTheLastByte_SortsThemInAddressOrder()
    {
        // Arrange
        var flows = Flows(
            AuditedServerDirectory.Empty,
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "caddy"),
            Inbound("192.168.1.9", "laptop", "192.168.1.50", "nas", 443, "caddy"));

        // Act
        var rules = new FirewallHostRuleAggregator().Aggregate(flows, AuditedServerDirectory.Empty);

        // Assert
        rules.Where(r => r.Host == "nas").Select(r => r.PeerIp).Should().Equal("192.168.1.9", "192.168.1.10");
    }

    private static IReadOnlyList<FirewallFlowItem> Flows(AuditedServerDirectory servers, params FirewallRuleItem[] rules) =>
        new FirewallFlowAggregator().Aggregate(rules, servers);
}
