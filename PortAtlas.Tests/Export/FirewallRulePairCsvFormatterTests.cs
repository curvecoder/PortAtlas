using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class FirewallRulePairCsvFormatterTests
{
    [Fact]
    public void BuildHeader_WhenInvoked_ReturnsPairMatrixHeader()
    {
        // Arrange
        var formatter = new FirewallRulePairCsvFormatter();

        // Act
        var header = formatter.BuildHeader(',');

        // Assert
        header.Should().Be("PairId,SourceIp,SourceHost,DestinationIp,DestinationHost,FlowCount,Ports,ObservationCount,FirstSeenUtc,LastSeenUtc,SeenFrom");
    }

    [Fact]
    public void FormatRow_WhenPairHasSeveralPorts_QuotesThePortList()
    {
        // Arrange
        var formatter = new FirewallRulePairCsvFormatter();
        var pair = new FirewallRulePairItem(
            "PAIR-001",
            "192.168.1.10",
            "desktop",
            "192.168.1.50",
            "nas",
            3,
            ["443/TCP", "873/TCP"],
            15,
            DateTimeOffset.Parse("2026-10-01T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-10-04T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [FlowObservationSide.Source, FlowObservationSide.Both]);

        // Act
        var row = formatter.FormatRow(pair, ',');

        // Assert
        row.Should().Be("PAIR-001,192.168.1.10,desktop,192.168.1.50,nas,3,\"443/TCP, 873/TCP\",15,2026-10-01T08:00:00.0000000+00:00,2026-10-04T08:00:00.0000000+00:00,\"Source, Both\"");
    }

    // A host name comes from DNS or RDAP, both under a third party's control; the export is opened in a spreadsheet.
    [Fact]
    public void FormatRow_WhenHostNameStartsLikeAFormula_PrefixesItWithAnApostrophe()
    {
        // Arrange
        var formatter = new FirewallRulePairCsvFormatter();
        var pair = new FirewallRulePairItem(
            "PAIR-001",
            "192.168.1.10",
            "desktop",
            "192.168.1.50",
            "@SUM(1+1)",
            1,
            ["443/TCP"],
            1,
            DateTimeOffset.Parse("2026-10-01T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-10-04T08:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
            [FlowObservationSide.Source]);

        // Act
        var row = formatter.FormatRow(pair, ',');

        // Assert
        row.Should().StartWith("PAIR-001,192.168.1.10,desktop,192.168.1.50,'@SUM(1+1),1,");
    }
}
