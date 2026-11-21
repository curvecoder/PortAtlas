using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallFlowAggregatorTests
{
    [Fact]
    public void Aggregate_WhenBothEndsRecordTheFlow_MergesThemIntoOneFlowSeenFromBoth()
    {
        // Arrange
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas.home.arpa", 443, "curl", 10, Monday, Monday.AddHours(3)),
            Inbound("192.168.1.10", "desktop.home.arpa", "192.168.1.50", "nas", 443, "caddy", 8, Monday.AddMinutes(-5), Monday.AddHours(2))
        ];

        // Act
        var flows = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty);

        // Assert
        var flow = flows.Should().ContainSingle().Subject;
        flow.FlowId.Should().Be("FLOW-001");
        flow.SourceIp.Should().Be("192.168.1.10");
        flow.SourceHost.Should().Be("desktop");
        flow.DestinationIp.Should().Be("192.168.1.50");
        flow.DestinationHost.Should().Be("nas");
        flow.DestinationZone.Should().Be("Internal");
        flow.Protocol.Should().Be("TCP");
        flow.DestinationPort.Should().Be(443);
        flow.SourceProcesses.Should().Equal("curl");
        flow.DestinationProcesses.Should().Equal("caddy");
        flow.SeenFrom.Should().Be(FlowObservationSide.Both);
        flow.SourceObservationCount.Should().Be(10);
        flow.DestinationObservationCount.Should().Be(8);
        flow.ObservationCount.Should().Be(10);
        flow.FirstSeenUtc.Should().Be(Monday.AddMinutes(-5));
        flow.LastSeenUtc.Should().Be(Monday.AddHours(3));
    }

    [Fact]
    public void Aggregate_WhenOnlyTheSourceRecordsTheFlow_ReportsTheSourceAndNoDestinationProcess()
    {
        // Arrange
        FirewallRuleItem[] rules = [Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas.home.arpa", 443)];

        // Act
        var flow = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty).Should().ContainSingle().Subject;

        // Assert
        flow.SeenFrom.Should().Be(FlowObservationSide.Source);
        flow.SourceProcesses.Should().Equal("curl");
        flow.DestinationProcesses.Should().BeEmpty();
        flow.DestinationObservationCount.Should().Be(0);
        flow.DestinationHost.Should().Be("nas.home.arpa");
    }

    [Fact]
    public void Aggregate_WhenOnlyTheDestinationRecordsTheFlow_ReportsTheDestinationAndNoSourceProcess()
    {
        // Arrange
        FirewallRuleItem[] rules = [Inbound("192.168.1.10", "desktop.home.arpa", "192.168.1.50", "nas", 443)];

        // Act
        var flow = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty).Should().ContainSingle().Subject;

        // Assert
        flow.SeenFrom.Should().Be(FlowObservationSide.Destination);
        flow.SourceProcesses.Should().BeEmpty();
        flow.DestinationProcesses.Should().Equal("caddy");
        flow.SourceObservationCount.Should().Be(0);
        flow.SourceHost.Should().Be("desktop.home.arpa");
    }

    [Fact]
    public void Aggregate_WhenSeveralProcessesOpenTheSameFlow_ListsThemSortedInOneFlowAndAddsTheirObservations()
    {
        // Arrange
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas.home.arpa", 22, "ssh", 4),
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas.home.arpa", 22, "WinSCP", 6)
        ];

        // Act
        var flow = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty).Should().ContainSingle().Subject;

        // Assert
        flow.SourceProcesses.Should().Equal("WinSCP", "ssh");
        flow.SourceObservationCount.Should().Be(10);
    }

    [Fact]
    public void Aggregate_WhenAnAddressBelongsToAnAuditedServer_NamesItAfterTheServerWhicheverLogSawTheFlow()
    {
        // Arrange
        FirewallRuleItem[] rules = [Inbound("192.168.1.20", "proxmox.home.arpa", "192.168.1.50", "nas", 445)];
        var servers = Servers(("proxmox", "192.168.1.20"), ("nas", "192.168.1.50"));

        // Act
        var flow = new FirewallFlowAggregator().Aggregate(rules, servers).Should().ContainSingle().Subject;

        // Assert
        flow.SourceHost.Should().Be("proxmox");
        flow.DestinationHost.Should().Be("nas");
    }

    [Fact]
    public void Aggregate_WhenTwoServersHoldTheSameLoopbackFlow_KeepsOneFlowPerServer()
    {
        // Arrange
        FirewallRuleItem[] rules =
        [
            Outbound("127.0.0.1", "nas", "127.0.0.1", "127.0.0.1", 5432, "gitea"),
            Inbound("127.0.0.1", "127.0.0.1", "127.0.0.1", "nas", 5432, "postgres"),
            Outbound("127.0.0.1", "proxmox", "127.0.0.1", "127.0.0.1", 5432, "pvedaemon")
        ];

        // Act
        var flows = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty);

        // Assert
        flows.Should().HaveCount(2);
        flows.Should().ContainSingle(f => f.LocalServer == "nas")
            .Which.Should().Match<FirewallFlowItem>(f => f.SeenFrom == FlowObservationSide.Both && f.DestinationHost == "nas");
        flows.Should().ContainSingle(f => f.LocalServer == "proxmox")
            .Which.SourceProcesses.Should().Equal("pvedaemon");
    }

    [Fact]
    public void Aggregate_WhenTheEndsNameDifferentServices_KeepsTheServiceNamedByTheDestination()
    {
        // Arrange
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 9000, "mc", serviceName: "mc"),
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 9000, "minio", serviceName: "minio")
        ];

        // Act
        var flow = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty).Should().ContainSingle().Subject;

        // Assert
        flow.ServiceName.Should().Be("minio");
    }

    [Fact]
    public void Aggregate_WhenTheDestinationNamesSeveralServices_UsesThePortAndProtocol()
    {
        // Arrange
        FirewallRuleItem[] rules =
        [
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 9000, "minio", serviceName: "minio"),
            Inbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 9000, "garage", serviceName: "garage")
        ];

        // Act
        var flow = new FirewallFlowAggregator().Aggregate(rules, AuditedServerDirectory.Empty).Should().ContainSingle().Subject;

        // Assert
        flow.ServiceName.Should().Be("9000/TCP");
        flow.DestinationProcesses.Should().Equal("garage", "minio");
    }

    [Fact]
    public void Aggregate_WhenTheRulesComeInAnotherOrder_NumbersTheFlowsTheSameWay()
    {
        // Arrange
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.1.10", "desktop", "192.168.1.50", "nas", 443),
            Outbound("192.168.1.10", "desktop", "192.168.1.2", "pihole", 53, "systemd-resolved", protocol: "UDP"),
            Outbound("192.168.1.10", "desktop", "192.168.1.2", "pihole", 53, "systemd-resolved"),
            Inbound("192.168.1.20", "proxmox", "192.168.1.50", "nas", 443)
        ];
        var aggregator = new FirewallFlowAggregator();

        // Act
        var given = aggregator.Aggregate(rules, AuditedServerDirectory.Empty);
        var reversed = aggregator.Aggregate([.. rules.Reverse()], AuditedServerDirectory.Empty);

        // Assert
        given.Select(f => $"{f.FlowId} {f.DestinationPort}/{f.Protocol} {f.SourceIp}").Should().Equal(
            "FLOW-001 53/TCP 192.168.1.10",
            "FLOW-002 53/UDP 192.168.1.10",
            "FLOW-003 443/TCP 192.168.1.10",
            "FLOW-004 443/TCP 192.168.1.20");
        reversed.Should().BeEquivalentTo(given, options => options.WithStrictOrdering());
    }
}
