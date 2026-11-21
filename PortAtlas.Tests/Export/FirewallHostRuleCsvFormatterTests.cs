using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallHostRuleCsvFormatterTests
{
    [Fact]
    public void BuildHeader_WhenInvoked_ReturnsHostRuleMatrixHeader()
    {
        // Act
        var header = new FirewallHostRuleCsvFormatter().BuildHeader(',');

        // Assert
        header.Should().Be("HostRuleId,Host,HostIp,Audited,Direction,PeerIp,PeerHost,PeerZone,Protocol,Port,ServiceName,LocalProcesses,Evidence,ObservationCount,FirstSeenUtc,LastSeenUtc,FlowId");
    }

    [Fact]
    public void FormatRow_WhenTheHostHasNoLog_WritesNoAndTheEvidence()
    {
        // Arrange
        var rule = Rule() with { IsAudited = false, LocalProcesses = [], Evidence = HostRuleEvidence.Inferred };

        // Act
        var row = new FirewallHostRuleCsvFormatter().FormatRow(rule, ',');

        // Assert
        row.Should().Be("HOST-001,nas,192.168.1.50,No,Inbound,192.168.1.10,desktop,Internal,TCP,443,HTTPS,,Inferred,8,2026-10-05T08:00:00.0000000+00:00,2026-10-05T09:00:00.0000000+00:00,FLOW-001");
    }

    [Fact]
    public void FormatRow_WhenTheHostRunsSeveralProcesses_QuotesTheProcessList()
    {
        // Arrange
        var rule = Rule() with { LocalProcesses = ["caddy", "nginx"] };

        // Act
        var row = new FirewallHostRuleCsvFormatter().FormatRow(rule, ',');

        // Assert
        row.Should().Contain(",Yes,Inbound,").And.Contain(",HTTPS,\"caddy, nginx\",Observed,8,");
    }

    // A peer name comes from DNS or RDAP, under a third party's control; the matrix is opened in a spreadsheet.
    [Fact]
    public void FormatRow_WhenThePeerNameStartsLikeAFormula_PrefixesItWithAnApostrophe()
    {
        // Arrange
        var rule = Rule() with { PeerHost = "+cmd" };

        // Act
        var row = new FirewallHostRuleCsvFormatter().FormatRow(rule, ',');

        // Assert
        row.Should().Contain(",192.168.1.10,'+cmd,Internal,");
    }

    private static FirewallHostRuleItem Rule() => new()
    {
        HostRuleId = "HOST-001",
        Host = "nas",
        HostIp = "192.168.1.50",
        IsAudited = true,
        Direction = "Inbound",
        PeerIp = "192.168.1.10",
        PeerHost = "desktop",
        PeerZone = "Internal",
        Protocol = "TCP",
        Port = 443,
        ServiceName = "HTTPS",
        LocalProcesses = ["caddy"],
        Evidence = HostRuleEvidence.Observed,
        ObservationCount = 8,
        FirstSeenUtc = Monday,
        LastSeenUtc = Monday.AddHours(1),
        FlowId = "FLOW-001"
    };
}
