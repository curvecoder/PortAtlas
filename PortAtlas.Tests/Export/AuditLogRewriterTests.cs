using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Export;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditLogRewriterTests : IDisposable
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("portatlas-rewriter-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task RewriteAsync_WhenFlowsAreGiven_WritesTheHeaderAndOneLinePerFlowInFirstSeenOrder()
    {
        // Arrange
        var csvPath = Path.Combine(_directory, "audit.csv");
        var formatter = new AuditCsvFormatter(new AuditCsvOptions
        {
            SelectedColumns = [AuditCsvColumn.FirstSeenUtc, AuditCsvColumn.LastSeenUtc, AuditCsvColumn.ObservationCount, AuditCsvColumn.RemoteIp]
        });
        var later = Flow("192.168.1.20", firstSeen: Monday.AddHours(2), lastSeen: Monday.AddHours(3), observations: 4);
        var earlier = Flow("192.168.1.10", firstSeen: Monday, lastSeen: Monday.AddDays(1), observations: 12);

        // Act
        await new AuditLogRewriter(csvPath, formatter).RewriteAsync([later, earlier]);

        // Assert
        (await File.ReadAllLinesAsync(csvPath)).Should().Equal(
            "FirstSeenUtc,LastSeenUtc,ObservationCount,RemoteIp",
            "2026-11-09T08:00:00.0000000+00:00,2026-11-10T08:00:00.0000000+00:00,12,192.168.1.10",
            "2026-11-09T10:00:00.0000000+00:00,2026-11-09T11:00:00.0000000+00:00,4,192.168.1.20");
    }

    [Fact]
    public async Task RewriteAsync_WhenTheLogAlreadyHoldsFlows_ReplacesThemWithTheGivenOnes()
    {
        // Arrange
        var csvPath = Path.Combine(_directory, "audit.csv");
        var formatter = new AuditCsvFormatter(new AuditCsvOptions { SelectedColumns = [AuditCsvColumn.ObservationCount, AuditCsvColumn.RemoteIp] });
        var rewriter = new AuditLogRewriter(csvPath, formatter);
        await rewriter.RewriteAsync([Flow("192.168.1.10", Monday, Monday, observations: 1)]);

        // Act
        await rewriter.RewriteAsync([Flow("192.168.1.10", Monday, Monday.AddMinutes(5), observations: 2)]);

        // Assert
        (await File.ReadAllLinesAsync(csvPath)).Should().Equal("ObservationCount,RemoteIp", "2,192.168.1.10");
    }

    [Fact]
    public async Task RewriteAsync_WhenACommandLineSpansLines_WritesItOnOneLine()
    {
        // Arrange
        var csvPath = Path.Combine(_directory, "audit.csv");
        var formatter = new AuditCsvFormatter(new AuditCsvOptions { SelectedColumns = [AuditCsvColumn.RemoteIp, AuditCsvColumn.CommandLine] });
        var flow = Flow("192.168.1.30", Monday, Monday, observations: 1, commandLine: "java -jar backup.jar\r\n--target pbs\n--retry\r3");

        // Act
        await new AuditLogRewriter(csvPath, formatter).RewriteAsync([flow]);

        // Assert
        (await File.ReadAllLinesAsync(csvPath)).Should().Equal("RemoteIp,CommandLine", "192.168.1.30,java -jar backup.jar --target pbs --retry 3");
    }

    [Fact]
    public async Task RewriteAsync_WhenAnNdjsonLogIsConfigured_WritesTheSameFlowsToIt()
    {
        // Arrange
        var csvPath = Path.Combine(_directory, "audit.csv");
        var ndjsonPath = Path.Combine(_directory, "audit.ndjson");
        var rewriter = new AuditLogRewriter(csvPath, new AuditCsvFormatter(new AuditCsvOptions()), ndjsonPath, new AuditNdjsonFormatter());

        // Act
        await rewriter.RewriteAsync([Flow("192.168.1.20", Monday.AddHours(1), Monday.AddHours(1), 1), Flow("192.168.1.10", Monday, Monday, 7)]);

        // Assert
        File.Exists(ndjsonPath).Should().BeTrue();
        var parser = new AuditNdjsonParser();
        var flows = (await File.ReadAllLinesAsync(ndjsonPath)).Select(parser.ParseLine).ToList();
        flows.Select(flow => (flow?.Socket.RemoteEndpoint.Address.ToString(), flow?.ObservationCount))
            .Should().Equal(("192.168.1.10", 7L), ("192.168.1.20", 1L));
    }

    private static NetworkAuditEntry Flow(string remoteIp, DateTimeOffset firstSeen, DateTimeOffset lastSeen, long observations, string commandLine = "rsync")
    {
        var socket = new ActiveSocketConnection(
            SocketTransportProtocol.Tcp,
            SocketFlowDirection.Outbound,
            new SocketEndpoint(IPAddress.Parse("192.168.1.5"), 51000),
            new SocketEndpoint(IPAddress.Parse(remoteIp), 873),
            "Established",
            new ProcessMetadata(42, "rsync", "/usr/bin/rsync", commandLine, "", "backup"));
        return new NetworkAuditEntry(firstSeen, lastSeen, observations, new ServerHostMetadata("nas", "Linux 6.8.0", "192.168.1.5"), socket);
    }
}
