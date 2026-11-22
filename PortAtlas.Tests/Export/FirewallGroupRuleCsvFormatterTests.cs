using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallGroupRuleCsvFormatterTests
{
    [Fact]
    public void BuildHeader_WhenDelimiterIsSemicolon_ListsTheColumnsInOrder()
    {
        // Act
        var header = new FirewallGroupRuleCsvFormatter().BuildHeader(';');

        // Assert
        header.Should().Be("GroupRuleId;SourceGroup;DestinationGroup;Protocol;DestinationPort;RuleCount;ObservationCount;FirstSeenUtc;LastSeenUtc;Processes");
    }

    [Fact]
    public void FormatRow_WhenAGroupNameStartsLikeAFormula_NeutralizesItAndJoinsTheProcesses()
    {
        // Arrange
        var row = new FirewallGroupRuleItem(
            "GROUP-001", "=lab", "servers", "TCP", 443, 3, 17, Monday, Monday.AddDays(3), ["caddy", "curl"]);

        // Act
        var line = new FirewallGroupRuleCsvFormatter().FormatRow(row, ';');

        // Assert
        line.Should().Be("GROUP-001;'=lab;servers;TCP;443;3;17;2026-10-05T08:00:00.0000000+00:00;2026-10-08T08:00:00.0000000+00:00;caddy, curl");
    }
}
