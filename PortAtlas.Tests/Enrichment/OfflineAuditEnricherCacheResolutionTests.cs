using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Enrichment;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Enrichment;

public sealed class OfflineAuditEnricherCacheResolutionTests : IDisposable
{
    private const string AuditHeader = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "PortAtlas_CacheResolution_" + Guid.NewGuid().ToString("N"));

    public OfflineAuditEnricherCacheResolutionTests() => Directory.CreateDirectory(_tempDirectory);

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenForceResolveFindsNothing_KeepsTheResolvedNameInTheCache()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("10.0.0.5");
        var resolver = new ScriptedIpResolver();
        var options = Options(forceResolve: true);

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([auditPath], [Cache(("10.0.0.5", "SRV-KNOWN"))], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("10.0.0.5");
        SavedCache(options)["10.0.0.5"].HostName.Should().Be("SRV-KNOWN");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenForceResolveFindsAnotherName_ReplacesTheNameInTheCache()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("10.0.0.5");
        var resolver = new ScriptedIpResolver { ["10.0.0.5"] = Resolved("SRV-RENAMED") };
        var options = Options(forceResolve: true);

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([auditPath], [Cache(("10.0.0.5", "SRV-KNOWN"))], options);

        // Assert
        SavedCache(options)["10.0.0.5"].HostName.Should().Be("SRV-RENAMED");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenCacheEntryHasNoName_ResolvesItAgainWithoutForce()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("10.0.0.5");
        var resolver = new ScriptedIpResolver { ["10.0.0.5"] = Resolved("SRV-LATE") };
        var options = Options();

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([auditPath], [Cache(("10.0.0.5", ""))], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("10.0.0.5");
        SavedCache(options)["10.0.0.5"].HostName.Should().Be("SRV-LATE");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenCacheEntryHasNameAndNoForce_DoesNotResolveIt()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("10.0.0.5");
        var resolver = new ScriptedIpResolver();

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([auditPath], [Cache(("10.0.0.5", "SRV-KNOWN"))], Options());

        // Assert
        resolver.ResolvedAddresses.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichPathsAsync_WhenOutputCacheIsNotListedAsInput_MergesItBeforeWriting()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("10.0.0.5");
        var outputCache = Path.Combine(_tempDirectory, "master.json");
        await new PersistentDnsCacheStore(outputCache).SaveAsync(Cache(("10.0.0.9", "SRV-MASTER")));
        var otherCache = Path.Combine(_tempDirectory, "other.json");
        await new PersistentDnsCacheStore(otherCache).SaveAsync(Cache(("10.0.0.5", "SRV-OTHER")));
        var options = new OfflineAuditEnrichmentOptions { Isolated = true, OutputCachePath = outputCache };

        // Act
        await new OfflineAuditEnricher().EnrichPathsAsync([auditPath], [otherCache], options);

        // Assert
        var saved = new PersistentDnsCacheStore(outputCache).Load();
        saved.Keys.Should().BeEquivalentTo(["10.0.0.5", "10.0.0.9"]);
    }

    [Fact]
    public void Consolidate_WhenOneEntryHasNoName_KeepsTheNamedEntryWithoutConflict()
    {
        // Arrange
        var named = Cache(("10.0.0.5", "SRV-KNOWN"));
        var unnamed = Cache(("10.0.0.5", ""));

        // Act
        var report = new DnsCacheConsolidator().Consolidate([unnamed, named]);

        // Assert
        report.MergedEntries["10.0.0.5"].HostName.Should().Be("SRV-KNOWN");
        report.Conflicts.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoAuditFile_ResolvesOnlyTheEntriesWithoutName()
    {
        // Arrange
        var resolver = new ScriptedIpResolver { ["10.0.0.6"] = Resolved("SRV-LATE") };
        var options = Options();
        var cache = Cache(("10.0.0.5", "SRV-KNOWN"), ("10.0.0.6", ""));

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([], [cache], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("10.0.0.6");
        SavedCache(options).Values.Select(entry => entry.HostName).Should().BeEquivalentTo(["SRV-KNOWN", "SRV-LATE"]);
        report.ProcessedAuditFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoAuditFileAndForce_ResolvesEveryEntryAndKeepsNamesOnFailure()
    {
        // Arrange
        var resolver = new ScriptedIpResolver { ["10.0.0.5"] = Resolved("SRV-RENAMED") };
        var options = Options(forceResolve: true);
        var cache = Cache(("10.0.0.5", "SRV-KNOWN"), ("10.0.0.6", "SRV-OTHER"));

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([], [cache], options);

        // Assert
        resolver.ResolvedAddresses.Should().BeEquivalentTo(["10.0.0.5", "10.0.0.6"]);
        var saved = SavedCache(options);
        saved["10.0.0.5"].HostName.Should().Be("SRV-RENAMED");
        saved["10.0.0.6"].HostName.Should().Be("SRV-OTHER");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoAuditFileAndIsolated_ResolvesNothing()
    {
        // Arrange
        var resolver = new ScriptedIpResolver();
        var options = Options() with { Isolated = true };

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([], [Cache(("10.0.0.6", ""))], options);

        // Assert
        resolver.ResolvedAddresses.Should().BeEmpty();
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenCacheIsResolved_ReportsEachKindOfOutcome()
    {
        // Arrange
        var resolver = new ScriptedIpResolver
        {
            ["10.0.0.1"] = Resolved("NEW-NAME"),
            ["10.0.0.2"] = Resolved("CHANGED-NAME"),
            ["10.0.0.3"] = Resolved("SAME-NAME")
        };
        var cache = Cache(("10.0.0.1", ""), ("10.0.0.2", "OLD-NAME"), ("10.0.0.3", "SAME-NAME"), ("10.0.0.4", "KEPT-NAME"), ("10.0.0.5", ""));

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([], [cache], Options(forceResolve: true));

        // Assert
        report.Resolution.Should().Be(new CacheResolutionSummary(NewlyNamed: 1, Renamed: 1, Unchanged: 1, KeptAfterFailure: 1, StillUnresolved: 1));
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenForceResolveOnlyFindsRegistryOwner_KeepsTheReverseDnsName()
    {
        // Arrange
        var resolver = new ScriptedIpResolver { ["10.0.0.5"] = new DnsCacheEntry { HostName = "Example Org", Source = "RDAP" } };
        var options = Options(forceResolve: true);

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([], [Cache(("10.0.0.5", "srv.example.test"))], options);

        // Assert
        SavedCache(options)["10.0.0.5"].HostName.Should().Be("srv.example.test");
        report.Resolution!.KeptAfterFailure.Should().Be(1);
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoServerAnswersForAnUnknownAddress_LeavesItOutOfTheCache()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("192.168.1.30");
        var resolver = new ScriptedIpResolver { ["192.168.1.30"] = null };
        var options = Options();

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([auditPath], [], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("192.168.1.30");
        SavedCache(options).Should().NotContainKey("192.168.1.30", "an unanswered lookup is not a negative result");
        report.Resolution!.StillUnresolved.Should().Be(1);
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoServerAnswersForANamedAddress_KeepsItsEntry()
    {
        // Arrange
        var resolver = new ScriptedIpResolver { ["192.168.1.10"] = null };
        var options = Options(forceResolve: true);

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([], [Cache(("192.168.1.10", "nas.home.arpa"))], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("192.168.1.10");
        SavedCache(options)["192.168.1.10"].HostName.Should().Be("nas.home.arpa");
        report.Resolution!.KeptAfterFailure.Should().Be(1);
    }

    private async Task<string> WriteAuditAsync(string remoteIp)
    {
        var path = Path.Combine(_tempDirectory, $"audit_{Guid.NewGuid():N}.csv");
        var row = $"2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,{remoteIp},443,,1234,curl,/usr/bin/curl,curl,,root";
        await File.WriteAllLinesAsync(path, [AuditHeader, row]);
        return path;
    }

    private OfflineAuditEnrichmentOptions Options(bool forceResolve = false) => new()
    {
        ForceResolve = forceResolve,
        OutputCachePath = Path.Combine(_tempDirectory, $"out_{Guid.NewGuid():N}.json")
    };

    private static Dictionary<string, DnsCacheEntry> SavedCache(OfflineAuditEnrichmentOptions options) =>
        new PersistentDnsCacheStore(options.OutputCachePath).Load();

    private static Dictionary<string, DnsCacheEntry> Cache(params (string Ip, string HostName)[] entries) =>
        entries.ToDictionary(
            entry => entry.Ip,
            entry => new DnsCacheEntry { HostName = entry.HostName, Source = entry.HostName.Length > 0 ? "DNS" : "None" },
            StringComparer.OrdinalIgnoreCase);

    private static DnsCacheEntry Resolved(string hostName) => new() { HostName = hostName, Source = "DNS" };

    private sealed class ScriptedIpResolver : IOfflineIpResolver
    {
        private readonly Dictionary<string, DnsCacheEntry?> _answers = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _resolvedAddresses = [];

        public IReadOnlyList<string> ResolvedAddresses => _resolvedAddresses;

        // A null answer stands for a lookup that no DNS or RDAP server answered.
        public DnsCacheEntry? this[string ip]
        {
            set => _answers[ip] = value;
        }

        public Task<DnsCacheEntry?> ResolveSingleAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            var ip = address.ToString();
            _resolvedAddresses.Add(ip);
            var answer = _answers.TryGetValue(ip, out var known) ? known : new DnsCacheEntry { HostName = string.Empty, Source = "None" };
            return Task.FromResult(answer);
        }
    }
}
