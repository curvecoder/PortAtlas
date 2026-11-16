using AwesomeAssertions;
using PortAtlas.Cli;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// A folder of collected logs also holds what the commands wrote there: enriched copies, an NDJSON twin, an older
/// matrix. Each command must read every audit once, whatever the folder contains.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class AuditInputSelectionCliTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
    private const string LogRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,,1234,psql,/usr/bin/psql,psql -h db,,backup";
    private const string EnrichedRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,db.home.arpa,1234,psql,/usr/bin/psql,psql -h db,,backup";
    private const string NdjsonRow = "{\"firstSeenUtc\":\"2026-10-24T07:00:00+00:00\",\"lastSeenUtc\":\"2026-10-24T07:05:00+00:00\",\"observationCount\":10,\"serverName\":\"nas\",\"serverOsVersion\":\"Linux\",\"serverPrimaryIp\":\"192.168.1.20\",\"protocol\":\"Tcp\",\"direction\":\"Outbound\",\"tcpState\":\"Established\",\"localIp\":\"192.168.1.20\",\"localPort\":50000,\"remoteIp\":\"192.168.1.30\",\"remotePort\":5432,\"remoteHostName\":\"\",\"processId\":1234,\"processName\":\"psql\",\"executablePath\":\"/usr/bin/psql\",\"commandLine\":\"psql -h db\",\"targetExecutable\":\"\",\"userName\":\"backup\"}";
    private const string MatrixRow = "RULE-001,Outbound,192.168.1.40,matrix-only-host,192.168.1.30,db.home.arpa,Internal,TCP,5432,PostgreSQL,psql,/usr/bin/psql,backup,10,2026-10-24T07:00:00.0000000+00:00,2026-10-24T07:05:00.0000000+00:00";

    private readonly string _root;
    private readonly string _collected;
    private readonly string _emptyConfigPath;

    public AuditInputSelectionCliTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PortAtlas_InputSelection_" + Guid.NewGuid().ToString("N"));
        _collected = Path.Combine(_root, "collected");
        Directory.CreateDirectory(_collected);
        File.WriteAllLines(Path.Combine(_collected, "audit_nas.csv"), [Header, LogRow]);
        _emptyConfigPath = Path.Combine(_root, "empty-config.json");
        File.WriteAllText(_emptyConfigPath, "{}");
    }

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
    public async Task ExportRules_WhenInputDirectoryHoldsALogAndItsEnrichedCopy_ReadsTheEnrichedCopyOnly()
    {
        // Arrange
        WriteEnrichedCopy();
        var matrix = Path.Combine(_root, "rules.csv");

        // Act
        var output = await RunAsync("export", "rules", "-i", _collected, "-o", matrix);

        // Assert
        var rows = (await File.ReadAllLinesAsync(matrix)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should()
            .StartWith("RULE-001,Outbound,192.168.1.20,nas,192.168.1.30,db.home.arpa,Internal,TCP,5432,")
            .And.Contain(",backup,10,");
        output.Should().Contain(Path.Combine(_collected, "audit_nas.csv")).And.Contain("audit_nas_enriched.csv");
    }

    [Fact]
    public async Task ExportRules_WhenWildcardInputMatchesALogAndItsEnrichedCopy_ReadsTheEnrichedCopyOnly()
    {
        // Arrange
        WriteEnrichedCopy();
        var matrix = Path.Combine(_root, "rules-from-pattern.csv");

        // Act
        await RunAsync("export", "rules", "-i", Path.Combine(_collected, "*.csv"), "-o", matrix);

        // Assert
        (await File.ReadAllLinesAsync(matrix)).Skip(1).Should().ContainSingle().Which.Should().Contain(",backup,10,");
    }

    [Fact]
    public async Task ExportRules_WhenInputDirectoryHoldsCsvAndNdjsonOfTheSameAudit_ReadsTheCsvOnly()
    {
        // Arrange
        await File.WriteAllLinesAsync(Path.Combine(_collected, "audit_nas.ndjson"), [NdjsonRow]);
        var matrix = Path.Combine(_root, "rules-twin.csv");

        // Act
        var output = await RunAsync("export", "rules", "-i", _collected, "-o", matrix);

        // Assert
        (await File.ReadAllLinesAsync(matrix)).Skip(1).Should().ContainSingle().Which.Should().Contain(",backup,10,");
        output.Should().Contain(Path.Combine(_collected, "audit_nas.ndjson"));
    }

    [Fact]
    public async Task ExportRules_WhenInputDirectoryHoldsAnOlderMatrix_ReadsTheAuditLogOnly()
    {
        // Arrange
        WriteOlderMatrix();
        var matrix = Path.Combine(_root, "rules-next-to-matrix.csv");

        // Act
        var output = await RunAsync("export", "rules", "-i", _collected, "-o", matrix);

        // Assert
        var rows = (await File.ReadAllLinesAsync(matrix)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should().StartWith("RULE-001,Outbound,192.168.1.20,nas,192.168.1.30,");
        output.Should().Contain(Path.Combine(_collected, "firewall_rules.csv"));
    }

    // Naming a file is asking for it: the two copies of an audit are then read as two logs, as before.
    [Fact]
    public async Task ExportRules_WhenALogAndItsEnrichedCopyAreBothNamed_ReadsBoth()
    {
        // Arrange
        WriteEnrichedCopy();
        var matrix = Path.Combine(_root, "rules-both-named.csv");

        // Act
        await RunAsync(
            "export", "rules", "-o", matrix,
            "-i", Path.Combine(_collected, "audit_nas.csv"),
            "-i", Path.Combine(_collected, "audit_nas_enriched.csv"));

        // Assert
        (await File.ReadAllLinesAsync(matrix)).Skip(1).Should().ContainSingle().Which.Should().Contain(",backup,20,");
    }

    [Fact]
    public async Task ExportRules_WhenInputDirectoryHoldsNoAuditLog_ReturnsExitCodeOneNamingTheFilesLeftOut()
    {
        // Arrange
        var folder = Path.Combine(_root, "matrices");
        Directory.CreateDirectory(folder);
        await File.WriteAllLinesAsync(Path.Combine(folder, "firewall_rules.csv"), [new FirewallRuleCsvFormatter().BuildHeader(), MatrixRow]);
        var matrix = Path.Combine(_root, "rules-from-nothing.csv");
        var consoleOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["export", "rules", "-i", folder, "-o", matrix, "--config", _emptyConfigPath]);
        }
        finally
        {
            Console.SetOut(consoleOut);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedOut.ToString().Should().Contain(Path.Combine(folder, "firewall_rules.csv"));
        File.Exists(matrix).Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_WhenInputDirectoryHoldsAnEnrichedCopyAndAMatrix_EnrichesTheOriginalLogOnly()
    {
        // Arrange
        // The copy of an earlier run carries a name that the cache no longer gives: only a copy written again
        // from the original loses it.
        var enrichedCopy = Path.Combine(_collected, "audit_nas_enriched.csv");
        await File.WriteAllLinesAsync(enrichedCopy, [Header, EnrichedRow.Replace("db.home.arpa", "former-db.home.arpa", StringComparison.Ordinal)]);
        WriteOlderMatrix();
        var cache = Path.Combine(_root, "cache.json");
        await File.WriteAllTextAsync(cache, "{\"192.168.1.30\": {\"HostName\": \"db.home.arpa\", \"Source\": \"DNS\"}}");

        // Act
        var output = await RunAsync("resolve", "-i", _collected, "--cache", cache, "--output-cache", Path.Combine(_root, "master.json"), "--cache-only");

        // Assert
        Directory.GetFiles(_collected).Select(Path.GetFileName).Should().BeEquivalentTo(
            "audit_nas.csv", "audit_nas_enriched.csv", "firewall_rules.csv");
        (await File.ReadAllLinesAsync(enrichedCopy)).Should().Equal(Header, EnrichedRow);
        output.Should().Contain(enrichedCopy).And.Contain(Path.Combine(_collected, "firewall_rules.csv"));
    }

    // The CSV and the NDJSON of one audit are two logs to rewrite, not one audit read twice.
    [Fact]
    public async Task Resolve_WhenInputDirectoryHoldsCsvAndNdjsonOfTheSameAudit_EnrichesBoth()
    {
        // Arrange
        await File.WriteAllLinesAsync(Path.Combine(_collected, "audit_nas.ndjson"), [NdjsonRow]);
        var cache = Path.Combine(_root, "cache.json");
        await File.WriteAllTextAsync(cache, "{\"192.168.1.30\": {\"HostName\": \"db.home.arpa\", \"Source\": \"DNS\"}}");

        // Act
        await RunAsync("resolve", "-i", _collected, "--cache", cache, "--output-cache", Path.Combine(_root, "master.json"), "--cache-only");

        // Assert
        Directory.GetFiles(_collected).Select(Path.GetFileName).Should().BeEquivalentTo(
            "audit_nas.csv", "audit_nas_enriched.csv", "audit_nas.ndjson", "audit_nas_enriched.ndjson");
    }

    [Fact]
    public async Task ExportNdjson_WhenInputDirectoryHoldsALogItsEnrichedCopyAndAMatrix_WritesEachFlowOnce()
    {
        // Arrange
        WriteEnrichedCopy();
        WriteOlderMatrix();
        var ndjson = Path.Combine(_root, "audit.ndjson");

        // Act
        await RunAsync("export", "ndjson", "-i", _collected, "-o", ndjson);

        // Assert
        (await File.ReadAllLinesAsync(ndjson)).Should().ContainSingle().Which.Should().Contain("\"remoteHostName\":\"db.home.arpa\"");
    }

    [Fact]
    public async Task ExportNdjson_WhenAnNdjsonLogIsNamed_WritesItsFlowsAsTheyAre()
    {
        // Arrange
        var log = Path.Combine(_root, "audit_nas.ndjson");
        await File.WriteAllLinesAsync(log, [NdjsonRow]);
        var ndjson = Path.Combine(_root, "export.ndjson");

        // Act
        await RunAsync("export", "ndjson", "-i", log, "-o", ndjson);

        // Assert
        (await File.ReadAllLinesAsync(ndjson)).Should().Equal(NdjsonRow);
    }

    [Fact]
    public async Task ExportNdjson_WhenInputDirectoryHoldsOnlyAnNdjsonLog_WritesItsFlows()
    {
        // Arrange
        var directory = Directory.CreateDirectory(Path.Combine(_root, "ndjson-only")).FullName;
        await File.WriteAllLinesAsync(Path.Combine(directory, "audit_nas.ndjson"), [NdjsonRow]);
        var ndjson = Path.Combine(_root, "export.ndjson");

        // Act
        await RunAsync("export", "ndjson", "-i", directory, "-o", ndjson);

        // Assert
        (await File.ReadAllLinesAsync(ndjson)).Should().Equal(NdjsonRow);
    }

    [Fact]
    public async Task ExportNdjson_WhenTheOutputIsAlsoAnInputLog_ReturnsExitCodeOneAndKeepsTheLog()
    {
        // Arrange
        var log = Path.Combine(_root, "audit_nas.ndjson");
        await File.WriteAllLinesAsync(log, [NdjsonRow]);
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["export", "ndjson", "-i", log, "-o", log, "--config", _emptyConfigPath]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain($"The output '{log}' is also an audit log to read");
        (await File.ReadAllLinesAsync(log)).Should().Equal(NdjsonRow);
    }

    [Fact]
    public async Task ExportNdjson_WhenTheInputDirectoryHoldsTheOutputOfAnEarlierRun_SkipsItAndWritesEachFlowOnce()
    {
        // Arrange
        var ndjson = Path.Combine(_collected, "all.ndjson");
        await RunAsync("export", "ndjson", "-i", _collected, "-o", ndjson);

        // Act
        var output = await RunAsync("export", "ndjson", "-i", _collected, "-o", ndjson);

        // Assert
        output.Should().Contain($"Skipped {ndjson}: it is the output of this command.");
        (await File.ReadAllLinesAsync(ndjson)).Should().ContainSingle().Which.Should().Contain("\"remoteIp\":\"192.168.1.30\"");
    }

    [Fact]
    public async Task ExportNdjson_WhenInputDirectoryHoldsCsvAndNdjsonOfTheSameAudit_WritesEachFlowOnce()
    {
        // Arrange
        await File.WriteAllLinesAsync(Path.Combine(_collected, "audit_nas.ndjson"), [NdjsonRow]);
        var ndjson = Path.Combine(_root, "export.ndjson");

        // Act
        await RunAsync("export", "ndjson", "-i", _collected, "-o", ndjson);

        // Assert
        (await File.ReadAllLinesAsync(ndjson)).Should().ContainSingle().Which.Should().Contain("\"remoteIp\":\"192.168.1.30\"");
    }

    [Fact]
    public async Task Map_WhenInputDirectoryHoldsALogItsEnrichedCopyAndAMatrix_ReadsTheEnrichedCopyOnly()
    {
        // Arrange
        WriteEnrichedCopy();
        WriteOlderMatrix();
        var html = Path.Combine(_root, "map.html");

        // Act
        var output = await RunAsync("map", "-i", _collected, "--html", html);

        // Assert
        (await File.ReadAllTextAsync(html)).Should().Contain("db.home.arpa");
        output.Should().Contain("Reading 1 audit log(s)")
            .And.Contain(Path.Combine(_collected, "audit_nas.csv"))
            .And.Contain(Path.Combine(_collected, "firewall_rules.csv"));
    }

    [Fact]
    public async Task Map_WhenALogLineHoldsNoValidPort_NamesTheLineOnTheConsole()
    {
        // Arrange
        var log = Path.Combine(_collected, "audit_nas.csv");
        File.WriteAllLines(log, [Header, LogRow, LogRow.Replace(",50000,", ",abc,", StringComparison.Ordinal)]);
        var html = Path.Combine(_root, "map.html");

        // Act
        var output = await RunAsync("map", "-i", log, "--html", html);

        // Assert
        output.Should().Contain($"Skipped 1 line(s) of {log} without a valid local and remote port: line 3.");
    }

    private void WriteEnrichedCopy() =>
        File.WriteAllLines(Path.Combine(_collected, "audit_nas_enriched.csv"), [Header, EnrichedRow]);

    private void WriteOlderMatrix() =>
        File.WriteAllLines(Path.Combine(_collected, "firewall_rules.csv"), [new FirewallRuleCsvFormatter().BuildHeader(), MatrixRow]);

    private async Task<string> RunAsync(params string[] commandArguments)
    {
        var consoleOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);

        int exitCode;
        try
        {
            exitCode = await Program.Main([.. commandArguments, "--config", _emptyConfigPath]);
        }
        finally
        {
            Console.SetOut(consoleOut);
        }

        exitCode.Should().Be(0);
        return capturedOut.ToString();
    }
}
