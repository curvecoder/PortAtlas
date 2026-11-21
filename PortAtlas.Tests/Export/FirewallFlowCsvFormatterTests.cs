using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;
using static PortAtlas.Tests.Export.FirewallMatrixSamples;

namespace PortAtlas.Tests.Export;

public sealed class FirewallFlowCsvFormatterTests
{
    [Fact]
    public void BuildHeader_WhenInvoked_ReturnsFlowMatrixHeader()
    {
        // Act
        var header = new FirewallFlowCsvFormatter().BuildHeader(';');

        // Assert
        header.Should().Be("FlowId;SourceIp;SourceHost;DestinationIp;DestinationHost;DestinationZone;Protocol;DestinationPort;ServiceName;SourceProcesses;DestinationProcesses;SeenFrom;SourceObservationCount;DestinationObservationCount;FirstSeenUtc;LastSeenUtc");
    }

    [Fact]
    public void FormatRow_WhenTheSourceRunsSeveralProcesses_QuotesTheProcessList()
    {
        // Arrange
        var flow = Flow() with { SourceProcesses = ["WinSCP", "ssh"], DestinationProcesses = ["sshd"] };

        // Act
        var row = new FirewallFlowCsvFormatter().FormatRow(flow, ',');

        // Assert
        row.Should().Be("FLOW-001,192.168.1.10,desktop,192.168.1.50,nas,Internal,TCP,22,SSH,\"WinSCP, ssh\",sshd,Both,10,8,2026-10-05T08:00:00.0000000+00:00,2026-10-05T09:00:00.0000000+00:00");
    }

    // A host or process name can be chosen by a third party; the matrix is opened in a spreadsheet.
    [Fact]
    public void FormatRow_WhenAHostNameStartsLikeAFormula_PrefixesItWithAnApostrophe()
    {
        // Arrange
        var flow = Flow() with { DestinationHost = "=HYPERLINK(1)" };

        // Act
        var row = new FirewallFlowCsvFormatter().FormatRow(flow, ',');

        // Assert
        row.Should().StartWith("FLOW-001,192.168.1.10,desktop,192.168.1.50,'=HYPERLINK(1),Internal,");
    }

    private static FirewallFlowItem Flow() => new()
    {
        FlowId = "FLOW-001",
        SourceIp = "192.168.1.10",
        SourceHost = "desktop",
        DestinationIp = "192.168.1.50",
        DestinationHost = "nas",
        DestinationZone = "Internal",
        Protocol = "TCP",
        DestinationPort = 22,
        ServiceName = "SSH",
        SourceProcesses = ["ssh"],
        DestinationProcesses = ["sshd"],
        SeenFrom = FlowObservationSide.Both,
        SourceObservationCount = 10,
        DestinationObservationCount = 8,
        FirstSeenUtc = Monday,
        LastSeenUtc = Monday.AddHours(1)
    };
}
