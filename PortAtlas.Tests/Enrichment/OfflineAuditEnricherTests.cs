using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Core.Enrichment;
using PortAtlas.Core.Export;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Enrichment;

public sealed class OfflineAuditEnricherTests : IDisposable
{
    private readonly string _tempDirectory;

    public OfflineAuditEnricherTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PortAtlas_EnricherTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task EnrichFilesAsync_WhenHostNameIsEmpty_PopulatesFromCache()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.0.0.5,443,,1234,curl,/usr/bin/curl,curl https://10.0.0.5,,root";
        await File.WriteAllLinesAsync(csvPath, [header, row]);

        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["10.0.0.5"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "SRV-TARGET-05", Source = "DNS" }
        };

        var enricher = new OfflineAuditEnricher();
        var options = new OfflineAuditEnrichmentOptions
        {
            InPlace = false,
            ForceResolve = false,
            Isolated = true
        };

        // Act
        var report = await enricher.EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        report.TotalRecordsScanned.Should().Be(1);
        report.TotalRecordsUpdated.Should().Be(1);
        report.GeneratedAuditFiles.Should().HaveCount(1);

        var enrichedFile = report.GeneratedAuditFiles[0];
        File.Exists(enrichedFile).Should().BeTrue();
        enrichedFile.Should().EndWith("audit_enriched.csv");

        var lines = await File.ReadAllLinesAsync(enrichedFile);
        lines.Should().HaveCount(2);
        lines[1].Should().Contain(",SRV-TARGET-05,");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenHostNameAlreadyPresentAndNoForce_PreservesExistingName()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit_existing.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.0.0.5,443,PRE-EXISTING-NAME,1234,curl,/usr/bin/curl,curl https://10.0.0.5,,root";
        await File.WriteAllLinesAsync(csvPath, [header, row]);

        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["10.0.0.5"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "CACHE-NAME", Source = "DNS" }
        };

        var enricher = new OfflineAuditEnricher();
        var options = new OfflineAuditEnrichmentOptions
        {
            InPlace = false,
            ForceResolve = false,
            Isolated = true
        };

        // Act
        var report = await enricher.EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        report.TotalRecordsUpdated.Should().Be(0);
        var enrichedFile = report.GeneratedAuditFiles[0];
        var lines = await File.ReadAllLinesAsync(enrichedFile);
        lines[1].Should().Contain(",PRE-EXISTING-NAME,");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenForceResolveEnabled_OverwritesExistingName()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit_force.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.0.0.5,443,PRE-EXISTING-NAME,1234,curl,/usr/bin/curl,curl https://10.0.0.5,,root";
        await File.WriteAllLinesAsync(csvPath, [header, row]);

        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["10.0.0.5"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "OVERWRITTEN-NAME", Source = "DNS" }
        };

        var enricher = new OfflineAuditEnricher();
        var options = new OfflineAuditEnrichmentOptions
        {
            InPlace = false,
            ForceResolve = true,
            Isolated = true
        };

        // Act
        var report = await enricher.EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        report.TotalRecordsUpdated.Should().Be(1);
        var enrichedFile = report.GeneratedAuditFiles[0];
        var lines = await File.ReadAllLinesAsync(enrichedFile);
        lines[1].Should().Contain(",OVERWRITTEN-NAME,");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenInPlaceIsTrue_OverwritesOriginalFileAtomically()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit_inplace.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.0.0.5,443,,1234,curl,/usr/bin/curl,curl https://10.0.0.5,,root";
        await File.WriteAllLinesAsync(csvPath, [header, row]);

        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["10.0.0.5"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "INPLACE-NAME", Source = "DNS" }
        };

        var enricher = new OfflineAuditEnricher();
        var options = new OfflineAuditEnrichmentOptions
        {
            InPlace = true,
            ForceResolve = false,
            Isolated = true
        };

        // Act
        var report = await enricher.EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        report.GeneratedAuditFiles.Should().ContainSingle().Which.Should().Be(csvPath);
        var lines = await File.ReadAllLinesAsync(csvPath);
        lines[1].Should().Contain(",INPLACE-NAME,");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenACommandLineSpansLines_NamesTheFlowAndKeepsTheRecordWhole()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit_multiline.csv");
        var csvContent =
            "FirstSeenUtc,ServerName,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessName,CommandLine\r\n" +
            "2026-11-01T08:00:00Z,nas,51000,192.168.1.30,8007,,java,\"java -jar backup.jar\r\n--target 192.168.1.30\"\r\n" +
            "2026-11-01T08:01:00Z,nas,51001,192.168.1.2,53,,dnsmasq,dnsmasq\r\n";
        await File.WriteAllTextAsync(csvPath, csvContent);

        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["192.168.1.30"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "pbs.home.arpa", Source = "DNS" }
        };
        var options = new OfflineAuditEnrichmentOptions { InPlace = true, ForceResolve = false, Isolated = true };

        // Act
        var report = await new OfflineAuditEnricher().EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        report.TotalRecordsScanned.Should().Be(2);
        var entries = await new AuditCsvHistoryParser().ParseHistoryAsync(csvPath);
        entries.Select(entry => (entry.RemoteHostName, entry.Socket.Process.CommandLine)).Should().Equal(
            ("pbs.home.arpa", "java -jar backup.jar\r\n--target 192.168.1.30"),
            (string.Empty, "dnsmasq"));
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNoFlowNeedsAName_RewritesTheLogByteForByte()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit_unchanged.csv");
        var csvContent =
            "FirstSeenUtc,ServerName,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessName,CommandLine\r\n" +
            "2026-11-01T08:00:00Z,nas,51000,192.168.1.30,8007,pbs.home.arpa,java,\"java -jar backup.jar\r\n--target \"\"pbs\"\", now\"\r\n" +
            "2026-11-01T08:01:00Z,nas,51001,192.168.1.2,53,,dnsmasq,dnsmasq\r\n";
        await File.WriteAllTextAsync(csvPath, csvContent);
        var options = new OfflineAuditEnrichmentOptions { InPlace = true, ForceResolve = false, Isolated = true };

        // Act
        var report = await new OfflineAuditEnricher().EnrichFilesAsync([csvPath], [new Dictionary<string, DnsCacheEntry>()], options);

        // Assert
        report.TotalRecordsUpdated.Should().Be(0);
        (await File.ReadAllTextAsync(csvPath)).Should().Be(csvContent);
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenTheLogEnrichedInPlaceIsASymbolicLink_WritesThroughTheLink()
    {
        // Arrange
        var linked = Path.Combine(Directory.CreateDirectory(Path.Combine(_tempDirectory, "linked")).FullName, "audit.csv");
        await File.WriteAllTextAsync(linked, "FirstSeenUtc,RemoteIp,RemoteHostName\r\n2026-11-01T08:00:00Z,192.168.1.30,\r\n");
        var csvPath = Path.Combine(_tempDirectory, "audit.csv");
        File.CreateSymbolicLink(csvPath, linked);
        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["192.168.1.30"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "pbs.home.arpa", Source = "DNS" }
        };
        var options = new OfflineAuditEnrichmentOptions { InPlace = true, ForceResolve = false, Isolated = true };

        // Act
        await new OfflineAuditEnricher().EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        (await File.ReadAllTextAsync(linked)).Should().Contain("pbs.home.arpa");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EnrichFilesAsync_WhenTheCsvLogStartsWithAByteOrderMark_KeepsItInTheRewrittenLog(bool inPlace)
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "audit_bom.csv");
        await File.WriteAllTextAsync(csvPath, "FirstSeenUtc,RemoteIp,RemoteHostName\r\n2026-11-01T08:00:00Z,192.168.1.30,\r\n", Encoding.UTF8);
        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["192.168.1.30"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "pbs.home.arpa", Source = "DNS" }
        };
        var options = new OfflineAuditEnrichmentOptions { InPlace = inPlace, Isolated = true };

        // Act
        var report = await new OfflineAuditEnricher().EnrichFilesAsync([csvPath], [cache], options);

        // Assert
        var written = await File.ReadAllBytesAsync(report.GeneratedAuditFiles.Should().ContainSingle().Subject);
        written.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        Encoding.UTF8.GetString(written).Should().Contain("pbs.home.arpa");
    }

    [Theory]
    [InlineData("audit_plain.csv", "FirstSeenUtc,RemoteIp,RemoteHostName\r\n2026-11-01T08:00:00Z,192.168.1.30,\r\n")]
    [InlineData("audit_plain.ndjson", "{\"remoteIp\":\"192.168.1.30\",\"remoteHostName\":\"\"}\r\n")]
    public async Task EnrichFilesAsync_WhenTheLogHasNoByteOrderMark_WritesNone(string fileName, string content)
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, fileName);
        await File.WriteAllTextAsync(path, content);
        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["192.168.1.30"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "pbs.home.arpa", Source = "DNS" }
        };
        var options = new OfflineAuditEnrichmentOptions { InPlace = true, Isolated = true };

        // Act
        await new OfflineAuditEnricher().EnrichFilesAsync([path], [cache], options);

        // Assert
        var written = await File.ReadAllBytesAsync(path);
        written[0].Should().Be((byte)content[0]);
        Encoding.UTF8.GetString(written).Should().Contain("pbs.home.arpa");
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenTheLogIsShorterThanAByteOrderMark_WritesNone()
    {
        // Arrange
        var path = Path.Combine(_tempDirectory, "audit_short.csv");
        await File.WriteAllBytesAsync(path, [0xEF, 0xBB]);
        var options = new OfflineAuditEnrichmentOptions { InPlace = true, Isolated = true };

        // Act
        await new OfflineAuditEnricher().EnrichFilesAsync([path], [], options);

        // Assert
        (await File.ReadAllBytesAsync(path)).Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public async Task EnrichFilesAsync_WhenNdjsonFileProvided_EnrichesRemoteHostName()
    {
        // Arrange
        var ndjsonPath = Path.Combine(_tempDirectory, "audit.ndjson");
        var jsonRecord = "{\"remoteIp\":\"10.0.0.8\",\"remoteHostName\":\"\",\"localPort\":50000,\"remotePort\":443}";
        await File.WriteAllLinesAsync(ndjsonPath, [jsonRecord]);

        var cache = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["10.0.0.8"] = new() { ResolvedAtUtc = DateTimeOffset.UtcNow, HostName = "NDJSON-SERVER", Source = "DNS" }
        };

        var enricher = new OfflineAuditEnricher();
        var options = new OfflineAuditEnrichmentOptions
        {
            InPlace = false,
            ForceResolve = false,
            Isolated = true
        };

        // Act
        var report = await enricher.EnrichFilesAsync([ndjsonPath], [cache], options);

        // Assert
        report.TotalRecordsUpdated.Should().Be(1);
        var enrichedFile = report.GeneratedAuditFiles[0];
        enrichedFile.Should().EndWith("audit_enriched.ndjson");

        var lines = await File.ReadAllLinesAsync(enrichedFile);
        lines.Should().ContainSingle();
        using var doc = JsonDocument.Parse(lines[0]);
        doc.RootElement.GetProperty("remoteHostName").GetString().Should().Be("NDJSON-SERVER");
    }
}
