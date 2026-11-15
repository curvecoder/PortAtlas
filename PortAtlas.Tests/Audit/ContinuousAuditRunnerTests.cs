using AwesomeAssertions;
using PortAtlas.Cli.Audit;
using PortAtlas.Cli.Configuration;
using Xunit;

namespace PortAtlas.Tests.Audit;

[Collection(ConsoleOutputCollection.Name)]
public sealed class ContinuousAuditRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PortAtlas_AuditRunner_" + Guid.NewGuid().ToString("N"));

    public ContinuousAuditRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the OS must not fail the test run; the folder lives in the temp directory.
        }
    }

    [Fact]
    public async Task RunAsync_WhenCancelledDuringTheStartupRewrite_DisposesTheResolverAndSavesTheDnsCache()
    {
        // Arrange
        var cachePath = Path.Combine(_root, "dns_cache.json");
        var config = CreateConfiguration(cachePath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act
        var act = () => ContinuousAuditRunner.RunAsync(config, Path.Combine(_root, "config.json"), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(cachePath).Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_WhenTheStartupRewriteFails_DisposesTheResolverAndReturnsOne()
    {
        // Arrange
        var cachePath = Path.Combine(_root, "dns_cache.json");
        var csvPath = Path.Combine(_root, "audit.csv");
        Directory.CreateDirectory(csvPath + ".pending");
        var config = CreateConfiguration(cachePath, csvPath);

        // Act
        var exitCode = await ContinuousAuditRunner.RunAsync(config, Path.Combine(_root, "config.json"));

        // Assert
        exitCode.Should().Be(1);
        File.Exists(cachePath).Should().BeTrue();
    }

    private AppConfiguration CreateConfiguration(string cachePath, string? csvPath = null) => new()
    {
        OutputCsvPath = csvPath ?? Path.Combine(_root, "audit.csv"),
        DnsCacheFilePath = cachePath,
        ResolveRemoteHostNames = true,
        ResolveIpOwners = false,
        EnableDashboard = false
    };
}
