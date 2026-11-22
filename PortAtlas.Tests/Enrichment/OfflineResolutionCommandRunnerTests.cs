using System.Net;
using AwesomeAssertions;
using PortAtlas.Cli.Enrichment;
using PortAtlas.Core.Enrichment;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Enrichment;

public sealed class OfflineResolutionCommandRunnerTests : IDisposable
{
    private readonly string _tempDirectory;

    public OfflineResolutionCommandRunnerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PortAtlas_CmdTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch (IOException)
            {
                // Tolerated in test cleanup
            }
        }
    }

    [Fact]
    public async Task RunAsync_WhenInvokedWithValidFiles_EnrichesAuditAndProducesMasterCache()
    {
        // Arrange
        var auditPath = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.0.0.99,443,,1234,curl,/usr/bin/curl,curl https://10.0.0.99,,root";
        await File.WriteAllLinesAsync(auditPath, [header, row]);

        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        var cacheJson = "{\"10.0.0.99\": {\"HostName\": \"SRV-CONSOLIDATED-99\", \"Source\": \"DNS\"}}";
        await File.WriteAllTextAsync(cachePath, cacheJson);

        var outputCachePath = Path.Combine(_tempDirectory, "master_cache.json");

        var options = new OfflineAuditEnrichmentOptions { Isolated = true, OutputCachePath = outputCachePath };
        var runner = new OfflineResolutionCommandRunner();

        // Act
        var exitCode = await runner.RunAsync([auditPath], [cachePath], options);

        // Assert
        exitCode.Should().Be(0);
        File.Exists(outputCachePath).Should().BeTrue();

        var enrichedFile = Path.Combine(_tempDirectory, "audit_enriched.csv");
        File.Exists(enrichedFile).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(enrichedFile);
        lines[1].Should().Contain(",SRV-CONSOLIDATED-99,");
    }

    [Fact]
    public async Task RunAsync_WhenInterruptedDuringTheLookups_SavesTheCacheAndReturns130()
    {
        // Arrange
        var cachePath = Path.Combine(_tempDirectory, "cache.json");
        await File.WriteAllTextAsync(cachePath, """{ "192.168.1.10": "", "192.168.1.11": "" }""");
        var outputCachePath = Path.Combine(_tempDirectory, "master_cache.json");
        var options = new OfflineAuditEnrichmentOptions { OutputCachePath = outputCachePath };
        using var interruption = new CancellationTokenSource();
        var runner = new OfflineResolutionCommandRunner(new OfflineAuditEnricher(offlineResolver: new InterruptedIpResolver(interruption)));

        // Act
        var act = () => runner.RunAsync([], [cachePath], options, interruption.Token);

        // Assert
        (await act.Should().NotThrowAsync()).Which.Should().Be(130);
        new PersistentDnsCacheStore(outputCachePath).Load().Values.Should().ContainSingle(entry => entry.HostName == "nas.home.arpa");
    }

    // Answers the first lookup, then stops the run as a Ctrl+C would during the second one.
    private sealed class InterruptedIpResolver(CancellationTokenSource interruption) : IOfflineIpResolver
    {
        private int _calls;

        public async Task<DnsCacheEntry?> ResolveSingleAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            if (++_calls > 1)
            {
                await interruption.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return new DnsCacheEntry { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "nas.home.arpa", Source = "DNS" };
        }
    }
}
