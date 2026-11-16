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
    public async Task EnrichPathsAsync_WhenOutputCacheIsNotListedAsInput_CountsOnlyTheListedCachesAsSources()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("192.168.1.10");
        var outputCache = Path.Combine(_tempDirectory, "master.json");
        await new PersistentDnsCacheStore(outputCache).SaveAsync(Cache(("192.168.1.20", "pihole.home.arpa"), ("192.168.1.30", "pve.home.arpa")));
        var otherCache = Path.Combine(_tempDirectory, "other.json");
        await new PersistentDnsCacheStore(otherCache).SaveAsync(Cache(("192.168.1.10", "nas.home.arpa")));
        var options = new OfflineAuditEnrichmentOptions { Isolated = true, OutputCachePath = outputCache };

        // Act
        var report = await new OfflineAuditEnricher().EnrichPathsAsync([auditPath], [otherCache], options);

        // Assert
        report.CacheReport.TotalCachesProcessed.Should().Be(1);
        report.CacheReport.TotalEntriesLoaded.Should().Be(1);
        report.CacheReport.MergedEntries.Keys.Should().BeEquivalentTo(["192.168.1.10", "192.168.1.20", "192.168.1.30"]);
    }

    [Fact]
    public async Task EnrichPathsAsync_WhenOutputCacheAndListedCacheDisagreeOnATie_KeepsTheOutputCacheName()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("192.168.1.10");
        var resolvedAt = new DateTimeOffset(2026, 10, 24, 7, 0, 0, TimeSpan.Zero);
        var outputCache = Path.Combine(_tempDirectory, "master.json");
        await new PersistentDnsCacheStore(outputCache).SaveAsync(new Dictionary<string, DnsCacheEntry>
        {
            ["192.168.1.10"] = new() { HostName = "nas.home.arpa", Source = "DNS", ResolvedAtUtc = resolvedAt }
        });
        var otherCache = Path.Combine(_tempDirectory, "other.json");
        await new PersistentDnsCacheStore(otherCache).SaveAsync(new Dictionary<string, DnsCacheEntry>
        {
            ["192.168.1.10"] = new() { HostName = "backup.home.arpa", Source = "DNS", ResolvedAtUtc = resolvedAt }
        });
        var options = new OfflineAuditEnrichmentOptions { Isolated = true, OutputCachePath = outputCache };

        // Act
        var report = await new OfflineAuditEnricher().EnrichPathsAsync([auditPath], [otherCache], options);

        // Assert
        report.CacheReport.MergedEntries["192.168.1.10"].HostName.Should().Be("nas.home.arpa");
        report.CacheReport.Conflicts.Should().ContainSingle(conflict => conflict.ConflictingHostName == "backup.home.arpa");
    }

    [Theory]
    [InlineData("{\"192.168.1.20\": {\"HostName\": \"pihole.home.arpa\", ", false)]
    [InlineData("{\"192.168.1.20\": {\"HostName\": \"pihole.home.arpa\", ", true)]
    [InlineData("[\"192.168.1.20\"]", false)]
    [InlineData("[\"192.168.1.20\"]", true)]
    public async Task EnrichPathsAsync_WhenOutputCacheIsUnreadable_ThrowsWithoutResolvingOrWriting(string content, bool listedAsInput)
    {
        // Arrange
        var auditPath = await WriteAuditAsync("192.168.1.10");
        var outputCache = Path.Combine(_tempDirectory, "master.json");
        await File.WriteAllTextAsync(outputCache, content);
        var resolver = new ScriptedIpResolver { ["192.168.1.10"] = Resolved("nas.home.arpa") };
        var options = new OfflineAuditEnrichmentOptions { OutputCachePath = outputCache };
        string[] caches = listedAsInput ? [outputCache] : [];

        // Act
        var act = () => new OfflineAuditEnricher(offlineResolver: resolver).EnrichPathsAsync([auditPath], caches, options);

        // Assert
        (await act.Should().ThrowAsync<DnsCacheUnreadableException>()).WithMessage($"*'{outputCache}'*");
        resolver.ResolvedAddresses.Should().BeEmpty();
        (await File.ReadAllTextAsync(outputCache)).Should().Be(content);
        Directory.GetFiles(_tempDirectory, "*_enriched*").Should().BeEmpty();
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
    public async Task EnrichFilesAsync_WhenNdjsonLinesLackTheHostNameOrHoldOtherJsonTypes_ResolvesTheAddressesWithoutAName()
    {
        // Arrange
        var auditPath = Path.Combine(_tempDirectory, $"audit_{Guid.NewGuid():N}.ndjson");
        await File.WriteAllLinesAsync(auditPath,
        [
            """{"remoteIp":"192.168.1.40","remotePort":443}""",
            """{"remoteIp":"192.168.1.41","remoteHostName":7}""",
            """[ "192.168.1.42" ]""",
            """{"remoteIp":"192.168.1.43","remoteHostName":null}"""
        ]);
        var resolver = new ScriptedIpResolver();
        var enricher = new OfflineAuditEnricher(offlineResolver: resolver);

        // Act
        var act = () => enricher.EnrichFilesAsync([auditPath], [], Options());

        // Assert
        await act.Should().NotThrowAsync();
        resolver.ResolvedAddresses.Should().BeEquivalentTo(["192.168.1.40", "192.168.1.43"]);
    }

    [Theory]
    [InlineData(';')]
    [InlineData('\t')]
    [InlineData('|')]
    public async Task EnrichFilesAsync_WhenTheCsvUsesAnotherSeparator_ResolvesAndNamesTheRemoteAddress(char separator)
    {
        // Arrange
        var auditPath = Path.Combine(_tempDirectory, $"audit_{Guid.NewGuid():N}.csv");
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,192.168.1.2,Tcp,Outbound,Established,192.168.1.2,50000,192.168.1.50,443,,1234,curl,/usr/bin/curl,curl,,root";
        await File.WriteAllLinesAsync(auditPath, [AuditHeader.Replace(',', separator), row.Replace(',', separator)]);
        var resolver = new ScriptedIpResolver { ["192.168.1.50"] = Resolved("proxmox.home.arpa") };
        var options = Options();

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([auditPath], [], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("192.168.1.50");
        report.TotalRecordsUpdated.Should().Be(1);
        var enrichedLines = await File.ReadAllLinesAsync(report.GeneratedAuditFiles.Should().ContainSingle().Which);
        enrichedLines[1].Split(separator)[13].Should().Be("proxmox.home.arpa");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenCancelledDuringTheLookups_SavesTheAddressesResolvedSoFar()
    {
        // Arrange
        using var interruption = new CancellationTokenSource();
        var enricher = new OfflineAuditEnricher(offlineResolver: new InterruptedIpResolver(interruption));
        var options = Options();
        var cache = Cache(("192.168.1.10", ""), ("192.168.1.11", ""));

        // Act
        var act = () => enricher.EnrichFilesAsync([], [cache], options, interruption.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        SavedCache(options).Values.Should().ContainSingle(entry => entry.HostName == "nas.home.arpa");
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

    [Fact]
    public async Task EnrichFilesAsync_WhenAddressIsInAnExcludedNetwork_DoesNotResolveIt()
    {
        // Arrange
        var excludedAudit = await WriteAuditAsync("169.254.10.1");
        var resolvedAudit = await WriteAuditAsync("10.0.0.5");
        var resolver = new ScriptedIpResolver();
        var options = Options() with { DnsExcludedNetworks = ["169.254.0.0/16"] };

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([excludedAudit, resolvedAudit], [], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("10.0.0.5");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoAuditFileAndAddressIsInAnExcludedNetwork_DoesNotResolveIt()
    {
        // Arrange
        var resolver = new ScriptedIpResolver();
        var options = Options() with { DnsExcludedNetworks = ["224.0.0.0/4"] };

        // Act
        await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([], [Cache(("224.0.0.251", ""), ("192.168.1.10", ""))], options);

        // Assert
        resolver.ResolvedAddresses.Should().Equal("192.168.1.10");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnrichFilesAsync_WhenAddressHasAnAlias_WritesTheAliasWithoutResolvingOrCachingIt(bool isolated)
    {
        // Arrange
        var auditPath = await WriteAuditAsync("192.168.1.20");
        var resolver = new ScriptedIpResolver();
        var options = Options() with
        {
            Isolated = isolated,
            CustomHostAliases = new Dictionary<string, string> { ["192.168.1.20"] = "nas.home.arpa" }
        };

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([auditPath], [], options);

        // Assert
        resolver.ResolvedAddresses.Should().BeEmpty();
        var lines = await File.ReadAllLinesAsync(report.GeneratedAuditFiles.Should().ContainSingle().Subject);
        lines[1].Should().Contain(",192.168.1.20,443,nas.home.arpa,");
        SavedCache(options).Should().NotContainKey("192.168.1.20");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenAliasedAddressIsAlreadyNamedAndForceResolve_ReplacesTheNameWithTheAlias()
    {
        // Arrange
        var auditPath = await WriteAuditAsync("192.168.1.20");
        var resolver = new ScriptedIpResolver();
        var options = Options(forceResolve: true) with
        {
            CustomHostAliases = new Dictionary<string, string> { ["192.168.1.20"] = "nas.home.arpa" }
        };

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver)
            .EnrichFilesAsync([auditPath], [Cache(("192.168.1.20", "old-nas.home.arpa"))], options);

        // Assert
        resolver.ResolvedAddresses.Should().BeEmpty();
        var lines = await File.ReadAllLinesAsync(report.GeneratedAuditFiles.Should().ContainSingle().Subject);
        lines[1].Should().Contain(",nas.home.arpa,");
        SavedCache(options)["192.168.1.20"].HostName.Should().Be("old-nas.home.arpa");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenTheCsvLogHasNoHeader_ResolvesAndNamesTheFirstFlowLikeTheOthers()
    {
        // Arrange
        var auditPath = Path.Combine(_tempDirectory, $"audit_{Guid.NewGuid():N}.csv");
        await File.WriteAllLinesAsync(auditPath,
        [
            "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,,1234,psql,/usr/bin/psql,psql,,backup",
            "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50001,192.168.1.40,443,,1234,curl,/usr/bin/curl,curl,,backup"
        ]);
        var resolver = new ScriptedIpResolver
        {
            ["192.168.1.30"] = Resolved("db.home.arpa"),
            ["192.168.1.40"] = Resolved("proxmox.home.arpa")
        };
        var options = Options();

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([auditPath], [], options);

        // Assert
        resolver.ResolvedAddresses.Should().BeEquivalentTo(["192.168.1.30", "192.168.1.40"]);
        report.TotalRecordsScanned.Should().Be(2);
        report.TotalRecordsUpdated.Should().Be(2);
        var lines = await File.ReadAllLinesAsync(report.GeneratedAuditFiles.Should().ContainSingle().Subject);
        lines.Should().HaveCount(2);
        lines[0].Should().Contain(",192.168.1.30,5432,db.home.arpa,");
        lines[1].Should().Contain(",192.168.1.40,443,proxmox.home.arpa,");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenTheCsvLogHasAReducedHeaderWithoutTheNameColumns_RewritesItUnchanged()
    {
        // Arrange
        var auditPath = Path.Combine(_tempDirectory, $"audit_{Guid.NewGuid():N}.csv");
        string[] content =
        [
            "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemotePort,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName,SourceAudit,Note",
            "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,,1234,psql,/usr/bin/psql,psql,,backup"
        ];
        await File.WriteAllLinesAsync(auditPath, content);
        var resolver = new ScriptedIpResolver { ["192.168.1.30"] = Resolved("db.home.arpa") };
        var options = Options();

        // Act
        var report = await new OfflineAuditEnricher(offlineResolver: resolver).EnrichFilesAsync([auditPath], [], options);

        // Assert
        resolver.ResolvedAddresses.Should().BeEmpty();
        report.TotalRecordsUpdated.Should().Be(0);
        (await File.ReadAllLinesAsync(report.GeneratedAuditFiles.Should().ContainSingle().Subject)).Should().Equal(content);
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

            return Resolved("nas.home.arpa");
        }
    }

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
