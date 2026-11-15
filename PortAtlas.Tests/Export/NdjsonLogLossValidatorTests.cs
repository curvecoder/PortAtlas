using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class NdjsonLogLossValidatorTests : IDisposable
{
    private const string Record = "{\"firstSeenUtc\":\"2026-11-01T08:00:00+00:00\",\"serverName\":\"proxmox\",\"localPort\":51000,\"remoteIp\":\"192.168.1.30\",\"remotePort\":8007}";

    private readonly string _directory = Directory.CreateTempSubdirectory("portatlas-ndjson-loss-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task EnsureNoFlowIsLostAsync_WhenNdjsonHoldsARecordAndNoFlowWasResumed_ThrowsNamingBothLogs()
    {
        // Arrange
        var csv = Path.Combine(_directory, "audit.csv");
        var ndjson = Path.Combine(_directory, "audit.ndjson");
        await File.WriteAllLinesAsync(ndjson, [Record]);

        // Act
        var act = () => NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(csv, 0, ndjson);

        // Assert
        (await act.Should().ThrowAsync<InvalidDataException>())
            .WithMessage($"*'{ndjson}'*'{csv}'*erase*");
    }

    [Fact]
    public async Task EnsureNoFlowIsLostAsync_WhenTheCsvLogResumedFlows_DoesNotThrow()
    {
        // Arrange
        var ndjson = Path.Combine(_directory, "audit.ndjson");
        await File.WriteAllLinesAsync(ndjson, [Record]);

        // Act
        var act = () => NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(Path.Combine(_directory, "audit.csv"), 2, ndjson);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public Task EnsureNoFlowIsLostAsync_WhenNoNdjsonLogIsConfigured_DoesNotThrow(string? ndjson)
    {
        // Act
        var act = () => NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(Path.Combine(_directory, "audit.csv"), 0, ndjson);

        // Assert
        return act.Should().NotThrowAsync();
    }

    [Fact]
    public Task EnsureNoFlowIsLostAsync_WhenTheNdjsonLogIsAbsent_DoesNotThrow()
    {
        // Act
        var act = () => NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(
            Path.Combine(_directory, "audit.csv"), 0, Path.Combine(_directory, "absent.ndjson"));

        // Assert
        return act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n\r\n")]
    public async Task EnsureNoFlowIsLostAsync_WhenTheNdjsonLogHoldsNoRecord_DoesNotThrow(string content)
    {
        // Arrange
        var ndjson = Path.Combine(_directory, "empty.ndjson");
        await File.WriteAllTextAsync(ndjson, content);

        // Act
        var act = () => NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(Path.Combine(_directory, "audit.csv"), 0, ndjson);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureNoFlowIsLostAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var ndjson = Path.Combine(_directory, "audit.ndjson");
        await File.WriteAllLinesAsync(ndjson, [Record]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => NdjsonLogLossValidator.EnsureNoFlowIsLostAsync(Path.Combine(_directory, "audit.csv"), 0, ndjson, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
