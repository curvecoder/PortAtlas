using System.ComponentModel;
using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Deduplication;
using PortAtlas.Core.Engine;
using PortAtlas.Core.Export;
using PortAtlas.Core.Filtering;
using PortAtlas.Core.Models;
using PortAtlas.Core.Providers;
using ProcessForge.Filtering;
using ProcessForge.Models;
using SocketForge.Contracts;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Engine;

public sealed class FlowAuditEngineTests : IAsyncLifetime
{
    private readonly string _testDir = Path.Combine(Path.GetTempPath(), "PortAtlas_Engine_" + Guid.NewGuid().ToString("N"));

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_testDir);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenNewFlowAppears_WritesToCsvAndReturnsCount()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "output.csv");
        var csvFormatter = new AuditCsvFormatter(new AuditCsvOptions());
        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter);

        var hostProvider = new LocalServerHostProvider();
        var deduplication = new ConnectionDeduplicationRegister();
        var exclusion = new ProcessExclusionFilter(new ProcessExclusionOptions { ExcludedProcessIds = [4] });

        var mockScanner = new FakeSocketScanner([
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("127.0.0.1"), 55000),
                new SocketEndpoint(IPAddress.Parse("93.184.216.34"), 80),
                "Established",
                new ProcessMetadata(100, "curl", "C:\\curl.exe", "curl http://example.com", "", "User"))
        ]);

        var engine = new FlowAuditEngine(
            mockScanner,
            exclusion,
            deduplication,
            hostProvider,
            logRewriter,
            TimeSpan.FromSeconds(1));

        // Act
        var count = await engine.ExecuteScanCycleAsync();

        // Assert
        count.Should().Be(1);
        deduplication.UniqueCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenFlowIsExcluded_DoesNotRecord()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "excluded.csv");
        var csvFormatter = new AuditCsvFormatter(new AuditCsvOptions());
        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter);

        var hostProvider = new LocalServerHostProvider();
        var deduplication = new ConnectionDeduplicationRegister();
        var exclusion = new ProcessExclusionFilter(new ProcessExclusionOptions { ExcludedProcessIds = [4] });

        var mockScanner = new FakeSocketScanner([
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Listening,
                new SocketEndpoint(IPAddress.Any, 445),
                new SocketEndpoint(IPAddress.Any, 0),
                "Listen",
                new ProcessMetadata(4, "System", "", "", "", "SYSTEM"))
        ]);

        var engine = new FlowAuditEngine(
            mockScanner,
            exclusion,
            deduplication,
            hostProvider,
            logRewriter,
            TimeSpan.FromSeconds(1));

        // Act
        var count = await engine.ExecuteScanCycleAsync();

        // Assert
        count.Should().Be(0);
        deduplication.UniqueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenNetworkFlowFilterExcludesPort_OmitsFromCsvAndCountsExcluded()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "network_filtered.csv");
        var csvFormatter = new AuditCsvFormatter(new AuditCsvOptions());
        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter);

        var hostProvider = new LocalServerHostProvider();
        var deduplication = new ConnectionDeduplicationRegister();
        var exclusion = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var networkFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions
        {
            ExcludedRemotePorts = new HashSet<int> { 443 }
        });

        var mockScanner = new FakeSocketScanner([
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("192.168.1.50"), 49152),
                new SocketEndpoint(IPAddress.Parse("93.184.216.34"), 443),
                "Established",
                new ProcessMetadata(200, "browser", "C:\\browser.exe", "", "", "User")),
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("192.168.1.50"), 49153),
                new SocketEndpoint(IPAddress.Parse("93.184.216.34"), 80),
                "Established",
                new ProcessMetadata(200, "browser", "C:\\browser.exe", "", "", "User"))
        ]);

        var engine = new FlowAuditEngine(
            mockScanner,
            exclusion,
            networkFilter,
            deduplication,
            hostProvider,
            logRewriter,
            TimeSpan.FromSeconds(1));

        FlowAuditCycleResult? recordedResult = null;
        engine.ScanCycleCompleted += (_, e) => recordedResult = e.Result;

        // Act
        var count = await engine.ExecuteScanCycleAsync();

        // Assert
        count.Should().Be(1);
        deduplication.UniqueCount.Should().Be(1);
        recordedResult.Should().NotBeNull();
        recordedResult!.TotalSocketsScanned.Should().Be(2);
        recordedResult.ExcludedSocketsCount.Should().Be(1);
        recordedResult.NewlyDiscoveredCount.Should().Be(1);
        recordedResult.TotalUniqueFlowsTracked.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenLoopbackExcludedByDefault_DoesNotRecordLoopbackFlow()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "loopback_filtered.csv");
        var csvFormatter = new AuditCsvFormatter(new AuditCsvOptions());
        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter);

        var hostProvider = new LocalServerHostProvider();
        var deduplication = new ConnectionDeduplicationRegister();
        var exclusion = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var networkFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions
        {
            ExcludeLoopbackFlows = true
        });

        var mockScanner = new FakeSocketScanner([
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Loopback, 5000),
                new SocketEndpoint(IPAddress.Loopback, 8080),
                "Established",
                new ProcessMetadata(300, "dotnet", "dotnet.exe", "", "", "User"))
        ]);

        var engine = new FlowAuditEngine(
            mockScanner,
            exclusion,
            networkFilter,
            deduplication,
            hostProvider,
            logRewriter,
            TimeSpan.FromSeconds(1));

        // Act
        var count = await engine.ExecuteScanCycleAsync();

        // Assert
        count.Should().Be(0);
        deduplication.UniqueCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenHostNameResolverInjected_ResolvesAndStoresRemoteHostNameForNewFlow()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "dns_output.csv");
        var options = new AuditCsvOptions
        {
            SelectedColumns = [AuditCsvColumn.RemoteIp, AuditCsvColumn.RemotePort, AuditCsvColumn.RemoteHostName]
        };
        var csvFormatter = new AuditCsvFormatter(options);
        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter);

        var hostProvider = new LocalServerHostProvider();
        var deduplication = new ConnectionDeduplicationRegister();
        var exclusion = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var networkFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions { ExcludeLoopbackFlows = false });

        var remoteIp = IPAddress.Parse("140.82.121.4");
        var mockScanner = new FakeSocketScanner([
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("192.168.1.10"), 50000),
                new SocketEndpoint(remoteIp, 443),
                "Established",
                new ProcessMetadata(1234, "git", "git.exe", "git fetch", "", "User"))
        ]);

        var fakeResolver = new FakeHostNameResolver("github.com");

        var engine = new FlowAuditEngine(
            mockScanner,
            exclusion,
            networkFilter,
            deduplication,
            hostProvider,
            logRewriter,
            fakeResolver,
            TimeSpan.FromSeconds(1),
            FlowAuditEngine.DefaultLogRewriteInterval);

        FlowAuditCycleResult? cycleResult = null;
        engine.ScanCycleCompleted += (_, e) => cycleResult = e.Result;

        // Act
        var count = await engine.ExecuteScanCycleAsync();

        // Assert
        count.Should().Be(1);
        cycleResult.Should().NotBeNull();
        cycleResult!.NewlyDiscoveredEntries.Should().HaveCount(1);
        cycleResult.NewlyDiscoveredEntries[0].RemoteHostName.Should().Be("github.com");

        await engine.RewriteLogAsync();
        var lines = await File.ReadAllLinesAsync(testCsv);
        lines.Length.Should().Be(2); // Header + 1 entry
        lines[1].Should().Be("140.82.121.4,443,github.com");
    }

    [Fact]
    public async Task RewriteLogAsync_WhenAnNdjsonLogIsConfigured_WritesBothCsvAndNdjsonLines()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "dual_output.csv");
        var testNdjson = Path.Combine(_testDir, "dual_output.ndjson");
        var csvFormatter = new AuditCsvFormatter(new AuditCsvOptions());
        var ndjsonFormatter = new AuditNdjsonFormatter();
        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter, testNdjson, ndjsonFormatter);

        var hostProvider = new LocalServerHostProvider();
        var deduplication = new ConnectionDeduplicationRegister();
        var exclusion = new ProcessExclusionFilter(new ProcessExclusionOptions());

        var mockScanner = new FakeSocketScanner([
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("10.0.0.1"), 55000),
                new SocketEndpoint(IPAddress.Parse("10.0.0.2"), 443),
                "Established",
                new ProcessMetadata(200, "app", "C:\\app.exe", "app", "", "User"))
        ]);

        var engine = new FlowAuditEngine(
            mockScanner,
            exclusion,
            new NetworkFlowFilter(new NetworkFlowFilterOptions()),
            deduplication,
            hostProvider,
            logRewriter,
            new PortAtlas.Core.Resolution.NoOpHostNameResolver(),
            TimeSpan.FromSeconds(1),
            FlowAuditEngine.DefaultLogRewriteInterval);

        // Act
        var count = await engine.ExecuteScanCycleAsync();

        // Assert
        count.Should().Be(1);
        await engine.RewriteLogAsync();

        File.Exists(testCsv).Should().BeTrue();
        File.Exists(testNdjson).Should().BeTrue();

        var ndjsonLines = await File.ReadAllLinesAsync(testNdjson);
        ndjsonLines.Should().HaveCount(1);

        var parser = new AuditNdjsonParser();
        var entry = parser.ParseLine(ndjsonLines[0]);
        entry.Should().NotBeNull();
        entry!.Socket.RemoteEndpoint.Port.Should().Be(443);
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenTheLogAlreadyHoldsReloadedFlows_TracksEachFlowOnce()
    {
        // Arrange
        var testCsv = Path.Combine(_testDir, "reloaded.csv");
        var csvFormatter = new AuditCsvFormatter(new AuditCsvOptions());
        var hostProvider = new LocalServerHostProvider();
        var host = hostProvider.GetCurrentHostMetadata();
        var seenUtc = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var process = new ProcessMetadata(300, "pveproxy", "/usr/bin/pveproxy", "pveproxy", "", "www-data");
        NetworkAuditEntry[] loggedFlows =
        [
            new(seenUtc, seenUtc, 1, host, new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("192.168.1.20"), 51000),
                new SocketEndpoint(IPAddress.Parse("192.168.1.2"), 53),
                "Established",
                process)),
            new(seenUtc, seenUtc, 1, host, new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("192.168.1.20"), 51001),
                new SocketEndpoint(IPAddress.Parse("192.168.1.30"), 8007),
                "Established",
                process))
        ];

        await using (var seedWriter = new FileForge.IO.PersistentDelimitedFileWriter(testCsv, csvFormatter.BuildHeaderLine()))
        {
            await seedWriter.InitializeAsync();
            foreach (var flow in loggedFlows)
            {
                await seedWriter.AppendLineAsync(csvFormatter.FormatEntry(flow));
            }
        }

        // The audit command reloads the parsed history into the register before starting the engine.
        var deduplication = new ConnectionDeduplicationRegister();
        deduplication.Hydrate(loggedFlows);

        var logRewriter = new AuditLogRewriter(testCsv, csvFormatter);
        var engine = new FlowAuditEngine(
            new FakeSocketScanner([]),
            new ProcessExclusionFilter(new ProcessExclusionOptions()),
            deduplication,
            hostProvider,
            logRewriter,
            TimeSpan.FromSeconds(1));

        using var cts = new CancellationTokenSource();
        var trackedFlows = -1;
        engine.ScanCycleCompleted += (_, e) =>
        {
            trackedFlows = e.Result.TotalUniqueFlowsTracked;
            _ = cts.CancelAsync();
        };

        // Act
        await engine.RunContinuousAuditAsync(cts.Token);

        // Assert
        trackedFlows.Should().Be(2);
        deduplication.GetAllEntries().Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenTheRewritePeriodHasElapsed_WritesTheLatestCountAndLastSeenDateOfEachFlow()
    {
        // Arrange
        var (engine, clock, logPath) = CreateEngineWithRewritePeriod(new FakeSocketScanner([NasBackupFlow()]), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        await engine.ExecuteScanCycleAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await engine.ExecuteScanCycleAsync();
        clock.Advance(TimeSpan.FromMinutes(4));

        // Act
        await engine.ExecuteScanCycleAsync();

        // Assert
        (await ReadLogAsync(logPath)).Should().Equal(
            "LastSeenUtc,ObservationCount,RemoteIp,RemoteHostName",
            "2026-11-14T08:05:00.0000000+00:00,3,192.168.1.30,");
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenTheRewritePeriodHasNotElapsed_LeavesTheLogAsItIs()
    {
        // Arrange
        var (engine, clock, logPath) = CreateEngineWithRewritePeriod(new FakeSocketScanner([NasBackupFlow()]), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        await engine.RewriteLogAsync();
        clock.Advance(TimeSpan.FromMinutes(4));

        // Act
        await engine.ExecuteScanCycleAsync();

        // Assert
        (await ReadLogAsync(logPath)).Should().Equal("LastSeenUtc,ObservationCount,RemoteIp,RemoteHostName");
    }

    [Fact]
    public async Task RewriteLogAsync_WhenANameIsResolvedAfterTheFirstSighting_WritesTheName()
    {
        // Arrange
        var resolver = new LateHostNameResolver();
        var (engine, _, logPath) = CreateEngineWithRewritePeriod(new FakeSocketScanner([NasBackupFlow()]), resolver);
        await engine.ExecuteScanCycleAsync();
        resolver.Resolve(IPAddress.Parse("192.168.1.30"), "pbs.home.arpa");

        // Act
        await engine.RewriteLogAsync();

        // Assert
        (await ReadLogAsync(logPath)).Should().Equal(
            "LastSeenUtc,ObservationCount,RemoteIp,RemoteHostName",
            "2026-11-14T08:00:00.0000000+00:00,1,192.168.1.30,pbs.home.arpa");
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenStopped_WritesTheFlowsObservedSinceTheLastRewrite()
    {
        // Arrange
        var (engine, _, logPath) = CreateEngineWithRewritePeriod(new FakeSocketScanner([NasBackupFlow()]), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        using var cts = new CancellationTokenSource();
        engine.ScanCycleCompleted += (_, _) => _ = cts.CancelAsync();

        // Act
        await engine.RunContinuousAuditAsync(cts.Token);

        // Assert
        (await ReadLogAsync(logPath)).Should().Equal(
            "LastSeenUtc,ObservationCount,RemoteIp,RemoteHostName",
            "2026-11-14T08:00:00.0000000+00:00,1,192.168.1.30,");
    }

    [Fact]
    public async Task ExecuteScanCycleAsync_WhenTheLogIsHeldOpenAtTheRewrite_KeepsAuditingAndWritesAtTheNextPeriod()
    {
        // Arrange
        var (engine, clock, logPath) = CreateEngineWithRewritePeriod(new FakeSocketScanner([NasBackupFlow()]), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        await engine.RewriteLogAsync();
        clock.Advance(TimeSpan.FromMinutes(5));
        using (new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await engine.ExecuteScanCycleAsync();
        }

        clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        await engine.ExecuteScanCycleAsync();

        // Assert
        (await ReadLogAsync(logPath)).Should().Equal(
            "LastSeenUtc,ObservationCount,RemoteIp,RemoteHostName",
            "2026-11-14T08:10:00.0000000+00:00,2,192.168.1.30,");
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenAScanFailsWhileTheLogIsHeldOpen_ReportsTheScanFailure()
    {
        // Arrange
        var (engine, _, logPath) = CreateEngineWithRewritePeriod(new FailingSocketScanner(), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        await engine.RewriteLogAsync();
        using var holder = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        // Act
        var act = () => engine.RunContinuousAuditAsync();

        // Assert
        (await act.Should().ThrowAsync<ConsecutiveScanFailuresException>())
            .WithInnerException<InvalidOperationException>().WithMessage("The socket table cannot be read.");
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenAScanFailsOnce_ReportsTheFailureAndRecordsTheNextCycle()
    {
        // Arrange
        var scanner = new FailingThenWorkingSocketScanner(1, new Win32Exception(5), [NasBackupFlow()]);
        var (engine, _, _) = CreateEngineWithRewritePeriod(scanner, new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        var failures = new List<FlowAuditCycleFailedEventArgs>();
        engine.ScanCycleFailed += (_, e) => failures.Add(e);
        using var cts = new CancellationTokenSource();
        var trackedFlows = 0;
        engine.ScanCycleCompleted += (_, e) =>
        {
            trackedFlows = e.Result.TotalUniqueFlowsTracked;
            _ = cts.CancelAsync();
        };

        // Act
        var act = () => engine.RunContinuousAuditAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
        trackedFlows.Should().Be(1);
        failures.Should().ContainSingle();
        failures[0].Error.Should().BeOfType<Win32Exception>();
        failures[0].ConsecutiveFailures.Should().Be(1);
        failures[0].ConsecutiveFailureLimit.Should().Be(FlowAuditEngine.ConsecutiveScanFailureLimit);
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenEveryScanFails_StopsAtTheLimitAfterReportingEachEarlierFailure()
    {
        // Arrange
        var (engine, _, logPath) = CreateEngineWithRewritePeriod(new FailingSocketScanner(), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        var reportedCounts = new List<int>();
        engine.ScanCycleFailed += (_, e) => reportedCounts.Add(e.ConsecutiveFailures);

        // Act
        var act = () => engine.RunContinuousAuditAsync();

        // Assert
        (await act.Should().ThrowAsync<ConsecutiveScanFailuresException>())
            .WithMessage($"The audit stopped after {FlowAuditEngine.ConsecutiveScanFailureLimit} failed scans in a row: The socket table cannot be read.")
            .WithInnerException<InvalidOperationException>();
        reportedCounts.Should().Equal(Enumerable.Range(1, FlowAuditEngine.ConsecutiveScanFailureLimit - 1));
        (await ReadLogAsync(logPath)).Should().Equal("LastSeenUtc,ObservationCount,RemoteIp,RemoteHostName");
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenScansFailBetweenSuccessfulCycles_CountsOnlyTheFailuresInARow()
    {
        // Arrange
        var scanner = new AlternatingSocketScanner([NasBackupFlow()]);
        var (engine, _, _) = CreateEngineWithRewritePeriod(scanner, new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        var reportedCounts = new List<int>();
        engine.ScanCycleFailed += (_, e) => reportedCounts.Add(e.ConsecutiveFailures);
        using var cts = new CancellationTokenSource();
        engine.ScanCycleCompleted += (_, _) =>
        {
            if (scanner.CallCount >= 2 * FlowAuditEngine.ConsecutiveScanFailureLimit)
            {
                _ = cts.CancelAsync();
            }
        };

        // Act
        var act = () => engine.RunContinuousAuditAsync(cts.Token);

        // Assert
        await act.Should().NotThrowAsync();
        reportedCounts.Should().OnlyContain(count => count == 1).And.HaveCount(FlowAuditEngine.ConsecutiveScanFailureLimit);
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenTheCycleFailsAfterTheScan_StopsAtOnceWithoutCountingAScanFailure()
    {
        // Arrange
        var (engine, _, _) = CreateEngineWithRewritePeriod(new FakeSocketScanner([NasBackupFlow()]), new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        var failures = 0;
        engine.ScanCycleFailed += (_, _) => failures++;
        engine.ScanCycleCompleted += (_, _) => throw new IOException("The map file is held by another program.");

        // Act
        var act = () => engine.RunContinuousAuditAsync();

        // Assert
        await act.Should().ThrowAsync<IOException>().WithMessage("The map file is held by another program.");
        failures.Should().Be(0);
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenStoppedDuringAFailingScan_StopsAsCancelledWithoutReportingAScanFailure()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var scanner = new CancellingThenFailingSocketScanner(cts);
        var (engine, _, _) = CreateEngineWithRewritePeriod(scanner, new PortAtlas.Core.Resolution.NoOpHostNameResolver());
        var failures = 0;
        engine.ScanCycleFailed += (_, _) => failures++;

        // Act
        var act = () => engine.RunContinuousAuditAsync(cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        failures.Should().Be(0);
    }

    [Fact]
    public async Task RunContinuousAuditAsync_WhenAScanFailsWithAnUnexpectedError_StopsAtOnce()
    {
        // Arrange
        var scanner = new FailingThenWorkingSocketScanner(1, new NotSupportedException("Unsupported platform."), [NasBackupFlow()]);
        var (engine, _, _) = CreateEngineWithRewritePeriod(scanner, new PortAtlas.Core.Resolution.NoOpHostNameResolver());

        // Act
        var act = () => engine.RunContinuousAuditAsync();

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("Unsupported platform.");
        scanner.CallCount.Should().Be(1);
    }

    private (FlowAuditEngine Engine, ManualTimeProvider Clock, string LogPath) CreateEngineWithRewritePeriod(
        ISocketScanner scanner,
        PortAtlas.Core.Resolution.IHostNameResolver resolver)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero));
        var logPath = Path.Combine(_testDir, "rewritten.csv");
        var formatter = new AuditCsvFormatter(new AuditCsvOptions
        {
            SelectedColumns = [AuditCsvColumn.LastSeenUtc, AuditCsvColumn.ObservationCount, AuditCsvColumn.RemoteIp, AuditCsvColumn.RemoteHostName]
        });
        var engine = new FlowAuditEngine(
            scanner,
            new ProcessExclusionFilter(new ProcessExclusionOptions()),
            new NetworkFlowFilter(new NetworkFlowFilterOptions()),
            new ConnectionDeduplicationRegister(),
            new LocalServerHostProvider(),
            new AuditLogRewriter(logPath, formatter),
            resolver,
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMinutes(5),
            clock);
        return (engine, clock, logPath);
    }

    private static async Task<string[]> ReadLogAsync(string logPath) =>
        File.Exists(logPath) ? await File.ReadAllLinesAsync(logPath) : [];

    private static ActiveSocketConnection NasBackupFlow() => new(
        SocketTransportProtocol.Tcp,
        SocketFlowDirection.Outbound,
        new SocketEndpoint(IPAddress.Parse("192.168.1.20"), 51000),
        new SocketEndpoint(IPAddress.Parse("192.168.1.30"), 8007),
        "Established",
        new ProcessMetadata(300, "proxmox-backup-client", "/usr/bin/proxmox-backup-client", "proxmox-backup-client backup", "", "backup"));

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delay) => _now += delay;
    }

    private sealed class FailingSocketScanner : ISocketScanner
    {
        public Task<IReadOnlyList<ActiveSocketConnection>> ScanSocketsAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("The socket table cannot be read.");
    }

    private sealed class FailingThenWorkingSocketScanner(int failingCalls, Exception error, IReadOnlyList<ActiveSocketConnection> connections) : ISocketScanner
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ActiveSocketConnection>> ScanSocketsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return CallCount <= failingCalls ? Task.FromException<IReadOnlyList<ActiveSocketConnection>>(error) : Task.FromResult(connections);
        }
    }

    private sealed class CancellingThenFailingSocketScanner(CancellationTokenSource stop) : ISocketScanner
    {
        public async Task<IReadOnlyList<ActiveSocketConnection>> ScanSocketsAsync(CancellationToken cancellationToken = default)
        {
            await stop.CancelAsync();
            throw new ObjectDisposedException("SocketScanner");
        }
    }

    private sealed class AlternatingSocketScanner(IReadOnlyList<ActiveSocketConnection> connections) : ISocketScanner
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<ActiveSocketConnection>> ScanSocketsAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            return CallCount % 2 == 1
                ? Task.FromException<IReadOnlyList<ActiveSocketConnection>>(new InvalidOperationException("The socket table changed size eight times."))
                : Task.FromResult(connections);
        }
    }

    private sealed class LateHostNameResolver : PortAtlas.Core.Resolution.IHostNameResolver
    {
        public event EventHandler<PortAtlas.Core.Resolution.HostNameResolvedEventArgs>? HostNameResolved;

        public bool TryGetCachedHostName(IPAddress address, out string hostName)
        {
            hostName = string.Empty;
            return false;
        }

        public void QueueResolution(IPAddress address)
        {
            // The name arrives later, when the test calls Resolve.
        }

        public Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public void Resolve(IPAddress address, string hostName)
            => HostNameResolved?.Invoke(this, new PortAtlas.Core.Resolution.HostNameResolvedEventArgs(address, hostName));
    }

    private sealed class FakeHostNameResolver(string resolvedHost) : PortAtlas.Core.Resolution.IHostNameResolver
    {
        public event EventHandler<PortAtlas.Core.Resolution.HostNameResolvedEventArgs>? HostNameResolved;

        public bool TryGetCachedHostName(IPAddress address, out string hostName)
        {
            hostName = resolvedHost;
            return true;
        }

        public void QueueResolution(IPAddress address)
        {
            HostNameResolved?.Invoke(this, new PortAtlas.Core.Resolution.HostNameResolvedEventArgs(address, resolvedHost));
        }

        public Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(resolvedHost);
        }
    }

    private sealed class FakeSocketScanner(IReadOnlyList<ActiveSocketConnection> connections) : ISocketScanner
    {
        public Task<IReadOnlyList<ActiveSocketConnection>> ScanSocketsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(connections);
        }
    }
}
