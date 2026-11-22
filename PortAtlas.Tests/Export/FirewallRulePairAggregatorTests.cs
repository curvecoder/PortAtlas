using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallRulePairAggregatorTests
{
    [Fact]
    public void Aggregate_WhenRulesShareSourceAndDestination_MergesThemIntoOnePairPerDirection()
    {
        // Arrange
        var flows = Flows(
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "curl", 5, Monday.AddDays(1), Monday.AddDays(2)),
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 873, "rsync", 7, Monday, Monday.AddDays(1)),
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "firefox", 3, Monday.AddDays(1).AddHours(1), Monday.AddDays(3)),
            Outbound("192.168.1.50", "nas", "192.168.1.10", "desktop", 8080, "wget", 2));

        // Act
        var pairs = new FirewallRulePairAggregator().Aggregate(flows);

        // Assert
        pairs.Should().HaveCount(2);
        var desktopToNas = pairs[0];
        desktopToNas.PairId.Should().Be("PAIR-001");
        desktopToNas.SourceHost.Should().Be("desktop");
        desktopToNas.DestinationHost.Should().Be("nas");
        desktopToNas.FlowCount.Should().Be(2);
        desktopToNas.Ports.Should().Equal("443/TCP", "873/TCP");
        desktopToNas.ObservationCount.Should().Be(15);
        desktopToNas.FirstSeenUtc.Should().Be(Monday);
        desktopToNas.LastSeenUtc.Should().Be(Monday.AddDays(3));
        desktopToNas.SeenFrom.Should().Equal(FlowObservationSide.Source);
        pairs[1].PairId.Should().Be("PAIR-002");
        pairs[1].SourceHost.Should().Be("nas");
        pairs[1].Ports.Should().Equal("8080/TCP");
    }

    // Both logs saw the same HTTPS connection: adding the two counts and the two rules doubled the pair.
    [Fact]
    public void Aggregate_WhenBothEndsRecordAFlow_CountsTheFlowAndItsObservationsOnce()
    {
        // Arrange
        var flows = Flows(
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "curl", 10),
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443, "caddy", 8),
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 873, "rsync", 7));

        // Act
        var pair = new FirewallRulePairAggregator().Aggregate(flows).Should().ContainSingle().Subject;

        // Assert
        pair.FlowCount.Should().Be(2);
        pair.ObservationCount.Should().Be(17);
        pair.SeenFrom.Should().Equal(FlowObservationSide.Source, FlowObservationSide.Both);
    }

    [Fact]
    public void Aggregate_WhenTwoServersHoldTheSameLoopbackFlow_KeepsOnePairPerServer()
    {
        // Arrange
        var flows = Flows(
            Outbound("127.0.0.1", "nas", "127.0.0.1", "127.0.0.1", 5432, "gitea"),
            Outbound("127.0.0.1", "proxmox", "127.0.0.1", "127.0.0.1", 5432, "pvedaemon"));

        // Act
        var pairs = new FirewallRulePairAggregator().Aggregate(flows);

        // Assert
        pairs.Select(p => p.SourceHost).Should().Equal("nas", "proxmox");
    }

    [Fact]
    public void Aggregate_WhenDestinationsDifferInTheLastByte_NumbersThePairsInAddressOrder()
    {
        // Arrange
        var flows = Flows(
            Outbound("192.168.1.20", "proxmox", "192.168.1.10", "desktop", 22, "ssh"),
            Outbound("192.168.1.20", "proxmox", "192.168.1.9", "laptop", 22, "ssh"));

        // Act
        var pairs = new FirewallRulePairAggregator().Aggregate(flows);

        // Assert
        pairs.Select(p => (p.PairId, p.DestinationIp)).Should().Equal(("PAIR-001", "192.168.1.9"), ("PAIR-002", "192.168.1.10"));
    }

    private static IReadOnlyList<FirewallFlowItem> Flows(params FirewallRuleItem[] rules) =>
        new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty);
}
