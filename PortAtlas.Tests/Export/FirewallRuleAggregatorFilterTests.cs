using AwesomeAssertions;
using PortAtlas.Core.Export;
using SocketForge.Models;
using TopologyForge.Filtering;
using TopologyForge.Models;
using Xunit;
using static PortAtlas.Tests.Export.FirewallRuleAggregatorEntries;

namespace PortAtlas.Tests.Export;

/// <summary>
/// Ephemeral port, threshold, zone, host, port and address filters applied by <see cref="FirewallRuleAggregator"/>.
/// </summary>
public sealed class FirewallRuleAggregatorFilterTests
{
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

    // The flow of CreateRemoteNamedEntry goes to the DNS port.
    [Theory]
    [InlineData("53", 1)]
    [InlineData("50-60", 1)]
    [InlineData("443", 0)]
    [InlineData("54-60", 0)]
    public void Aggregate_WhenIncludedPortsAreGiven_KeepsOnlyTheRulesTowardsThem(string includedPorts, int expectedRules)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { IncludedPorts = [NetworkPortRange.Parse(includedPorts)] });

        // Assert
        rules.Should().HaveCount(expectedRules);
    }

    [Theory]
    [InlineData("53", "53")]
    [InlineData("50-60", "53")]
    [InlineData("50-60", "50-55")]
    public void Aggregate_WhenPortIsBothIncludedAndExcluded_RemovesTheRule(string includedPorts, string excludedPorts)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);
        var options = new FirewallRuleExportOptions
        {
            IncludedPorts = [NetworkPortRange.Parse(includedPorts)],
            ExcludedPorts = [NetworkPortRange.Parse(excludedPorts)]
        };

        // Act
        var rules = aggregator.Aggregate([entry], options);

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenExcludedRangeDoesNotHoldTheIncludedPort_KeepsTheRule()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(SocketFlowDirection.Outbound, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);
        var options = new FirewallRuleExportOptions
        {
            IncludedPorts = [NetworkPortRange.Parse("50-60")],
            ExcludedPorts = [NetworkPortRange.Parse("54-60")]
        };

        // Act
        var rules = aggregator.Aggregate([entry], options);

        // Assert
        rules.Should().ContainSingle();
    }

    // Same rule as the topology map: an address filter is tested against both ends of the flow, whatever its direction.
    [Theory]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.53")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.48/29")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.53")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.48/29")]
    public void Aggregate_WhenExcludedAddressMatchesOneEndOfTheFlow_RemovesTheRule(SocketFlowDirection direction, string excluded)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { ExcludedIps = [IpAddressRange.Parse(excluded)] });

        // Assert
        rules.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.53")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.48/29")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.20")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.53")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.48/29")]
    public void Aggregate_WhenIncludedAddressMatchesOneEndOfTheFlow_KeepsTheRule(SocketFlowDirection direction, string included)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { IncludedIps = [IpAddressRange.Parse(included)] });

        // Assert
        rules.Should().ContainSingle();
    }

    [Theory]
    [InlineData(SocketFlowDirection.Outbound)]
    [InlineData(SocketFlowDirection.Inbound)]
    public void Aggregate_WhenIncludedAddressMatchesNoEndOfTheFlow_RemovesTheRule(SocketFlowDirection direction)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate([entry], new FirewallRuleExportOptions { IncludedIps = [IpAddressRange.Parse("192.168.1.2")] });

        // Assert
        rules.Should().BeEmpty();
    }

    [Theory]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.20", "192.168.1.53")]
    [InlineData(SocketFlowDirection.Outbound, "192.168.1.53", "192.168.1.20")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.20", "192.168.1.53")]
    [InlineData(SocketFlowDirection.Inbound, "192.168.1.53", "192.168.1.20")]
    public void Aggregate_WhenOneEndIsIncludedAndTheOtherExcluded_RemovesTheRule(
        SocketFlowDirection direction,
        string included,
        string excluded)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateRemoteNamedEntry(direction, remoteHost: "pihole.home.arpa", lastSeen: DateTimeOffset.UtcNow);

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { IncludedIps = [IpAddressRange.Parse(included)], ExcludedIps = [IpAddressRange.Parse(excluded)] });

        // Assert
        rules.Should().BeEmpty();
    }

    // A listening socket has no remote end: its remote address is the unset 0.0.0.0, which no address filter may read
    // as a host. Only the address it is bound to is tested.
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("0.0.0.0/8")]
    public void Aggregate_WhenListenersOnlyAndExcludedAddressMatchesTheUnsetRemoteAddress_KeepsTheListener(string excluded)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateListenerEntry();

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, ExcludedIps = [IpAddressRange.Parse(excluded)] });

        // Assert
        rules.Should().ContainSingle();
    }

    [Fact]
    public void Aggregate_WhenListenersOnlyAndIncludedAddressMatchesOnlyTheUnsetRemoteAddress_RemovesTheListener()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateListenerEntry();

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, IncludedIps = [IpAddressRange.Parse("0.0.0.0")] });

        // Assert
        rules.Should().BeEmpty();
    }

    [Fact]
    public void Aggregate_WhenListenersOnlyAndIncludedAddressMatchesTheBoundAddress_KeepsTheListener()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateListenerEntry();

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, IncludedIps = [IpAddressRange.Parse("192.168.1.0/24")] });

        // Assert
        rules.Should().ContainSingle();
    }

    [Fact]
    public void Aggregate_WhenListenersOnlyAndExcludedAddressMatchesTheBoundAddress_RemovesTheListener()
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateListenerEntry();

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, ExcludedIps = [IpAddressRange.Parse("192.168.1.0/24")] });

        // Assert
        rules.Should().BeEmpty();
    }

    // The matrix tests the address the rule shows, not the primary address of the server as the topology map does:
    // a listener bound to every interface is not in the subnet of its server.
    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void Aggregate_WhenListenerIsBoundToEveryInterfaceAndIncludedSubnetIsTheOneOfTheServer_RemovesTheListener(string boundAddress)
    {
        // Arrange
        var aggregator = new FirewallRuleAggregator();
        var entry = CreateListenerEntry(boundAddress);

        // Act
        var rules = aggregator.Aggregate(
            [entry],
            new FirewallRuleExportOptions { ListenersOnly = true, IncludedIps = [IpAddressRange.Parse("192.168.1.0/24")] });

        // Assert
        rules.Should().BeEmpty();
    }
}
