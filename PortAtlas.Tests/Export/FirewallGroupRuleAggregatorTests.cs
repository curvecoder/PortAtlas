using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallGroupRuleAggregatorTests
{
    [Fact]
    public void Aggregate_WhenRulesLinkTwoGroupsOnOnePort_MergesThemIntoOneRow()
    {
        // Arrange
        var groups = AddressGroupDirectoryTests.Create(("lab", "192.168.10.0/24"), ("servers", "192.168.1.0/24"));
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.10.21", "pc-21", "192.168.1.50", "nas", 443, "firefox", 5, Monday.AddDays(1), Monday.AddDays(2)),
            Outbound("192.168.10.22", "pc-22", "192.168.1.51", "git", 443, "curl", 7, Monday, Monday.AddDays(1)),
            Inbound("192.168.10.22", "pc-22", "192.168.1.51", "git", 443, "caddy", 3, Monday.AddHours(1), Monday.AddDays(3)),
            Outbound("192.168.10.21", "pc-21", "192.168.1.50", "nas", 443, "curl", 2)
        ];

        // Act
        var rows = new FirewallGroupRuleAggregator().Aggregate(rules, groups);

        // Assert
        var row = rows.Should().ContainSingle().Subject;
        row.GroupRuleId.Should().Be("GROUP-001");
        row.SourceGroup.Should().Be("lab");
        row.DestinationGroup.Should().Be("servers");
        row.Protocol.Should().Be("TCP");
        row.DestinationPort.Should().Be(443);
        row.RuleCount.Should().Be(4);
        row.ObservationCount.Should().Be(17);
        row.FirstSeenUtc.Should().Be(Monday);
        row.LastSeenUtc.Should().Be(Monday.AddDays(3));
        row.Processes.Should().Equal("caddy", "curl", "firefox");
    }

    [Fact]
    public void Aggregate_WhenAnAddressIsInNoGroup_KeepsItsAddressAndSplitsByProtocolAndPort()
    {
        // Arrange
        var groups = AddressGroupDirectoryTests.Create(("lab", "192.168.10.0/24"));
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.10.21", "pc-21", "192.168.1.53", "pihole", 53, "dnsmasq", protocol: "UDP"),
            Outbound("192.168.10.21", "pc-21", "192.168.1.53", "pihole", 53, "dnsmasq", protocol: "TCP"),
            Outbound("192.168.10.21", "pc-21", "192.168.1.53", "pihole", 443, "curl"),
            Outbound("192.168.1.50", "nas", "192.168.10.21", "pc-21", 22, "ssh")
        ];

        // Act
        var rows = new FirewallGroupRuleAggregator().Aggregate(rules, groups);

        // Assert
        rows.Select(row => (row.GroupRuleId, row.SourceGroup, row.DestinationGroup, row.Protocol, row.DestinationPort)).Should().Equal(
            ("GROUP-001", "192.168.1.50", "lab", "TCP", 22),
            ("GROUP-002", "lab", "192.168.1.53", "TCP", 53),
            ("GROUP-003", "lab", "192.168.1.53", "UDP", 53),
            ("GROUP-004", "lab", "192.168.1.53", "TCP", 443));
    }

    [Fact]
    public void Aggregate_WhenARuleIsAListeningSocket_WritesAnyAsItsSource()
    {
        // Arrange
        var groups = AddressGroupDirectoryTests.Create(("everything", "0.0.0.0/0"), ("servers", "192.168.1.0/24"));
        var listener = Inbound("0.0.0.0", "Any", "192.168.1.50", "nas", 8080, "java");

        // Act
        var rows = new FirewallGroupRuleAggregator().Aggregate([listener], groups);

        // Assert
        var row = rows.Should().ContainSingle().Subject;
        row.SourceGroup.Should().Be("Any");
        row.DestinationGroup.Should().Be("servers");
    }

    [Fact]
    public void Aggregate_WhenAnAddressInNoGroupIsWrittenInTwoForms_MergesItsRulesUnderOneForm()
    {
        // Arrange
        var groups = AddressGroupDirectoryTests.Create(("lab", "192.168.10.0/24"));
        FirewallRuleItem[] rules =
        [
            Outbound("192.168.10.21", "pc-21", "::ffff:192.168.1.53", "pihole", 53, "dnsmasq", 2),
            Outbound("192.168.10.22", "pc-22", "192.168.1.53", "pihole", 53, "dnsmasq", 3),
            Outbound("192.168.10.21", "pc-21", "fe80::1%3", "router", 22, "ssh", 4),
            Outbound("192.168.10.22", "pc-22", "fe80::1", "router", 22, "ssh", 5)
        ];

        // Act
        var rows = new FirewallGroupRuleAggregator().Aggregate(rules, groups);

        // Assert
        rows.Select(row => (row.SourceGroup, row.DestinationGroup, row.DestinationPort, row.RuleCount)).Should().BeEquivalentTo(
            [("lab", "192.168.1.53", 53, 2), ("lab", "fe80::1", 22, 2)]);
    }

    [Fact]
    public void Aggregate_WhenAListenerSourceIsTheIpv4MappedUnspecifiedAddress_WritesAnyAsItsSource()
    {
        // Arrange
        var groups = AddressGroupDirectoryTests.Create(("everything", "0.0.0.0/0"), ("servers", "192.168.1.0/24"));
        var listener = Inbound("::ffff:0.0.0.0", "Any", "192.168.1.50", "nas", 8080, "java");

        // Act
        var rows = new FirewallGroupRuleAggregator().Aggregate([listener], groups);

        // Assert
        rows.Should().ContainSingle().Which.SourceGroup.Should().Be("Any");
    }
}
