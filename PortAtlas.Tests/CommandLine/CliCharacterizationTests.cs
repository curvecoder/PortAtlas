using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Cli;
using PortAtlas.Cli.Configuration;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// Pins the observable behaviour of each command (files produced from a known audit log) so that the
/// command-line parser can be replaced (ADR-0001) without changing what the commands compute.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class CliCharacterizationTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
    private const string OracleRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.20.1.5,1521,db-oracle.example.test,1234,tnslsnr,/usr/bin/tnslsnr,tnslsnr --run,,oracle";
    private const string PublicRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,2,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50001,203.0.113.10,443,,2222,git,/usr/bin/git,git fetch,,deploy";
    private const string ListenerRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,7,SRV-APP-01,Linux,10.0.0.1,Tcp,Listening,Listen,10.0.0.1,8080,0.0.0.0,0,,3333,java,/usr/bin/java,java -jar web.jar,web.jar,web";
    private const string SecondServerRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,4,SRV-APP-02,Linux,10.0.0.2,Tcp,Outbound,Established,10.0.0.2,50002,10.20.1.5,1521,db-oracle.example.test,4444,sqlplus,/usr/bin/sqlplus,sqlplus,,oracle";

    private readonly string _directory;
    private readonly string _auditPath;
    private readonly string _emptyConfigPath;

    public CliCharacterizationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_CliCharacterization_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _auditPath = Path.Combine(_directory, "audit_srv1.csv");
        File.WriteAllLines(_auditPath, [Header, OracleRow, PublicRow, ListenerRow]);
        // A command given a configuration file that does not exist stops; the commands under test run on the
        // default settings, which an empty document gives.
        _emptyConfigPath = Path.Combine(_directory, "empty-config.json");
        File.WriteAllText(_emptyConfigPath, "{}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the OS must not fail the test run; the folder lives in the temp directory.
        }
    }

    [Fact]
    public async Task ExportRules_WhenMinimumObservationsAndInternalZoneGiven_ExportsOnlyFrequentInternalRules()
    {
        // Arrange
        var matrix = Path.Combine(_directory, "rules.csv");

        // Act
        await RunAsync("export", "rules", "-o", matrix, "-i", _auditPath, "--min-observations", "5", "--zone", "internal");

        // Assert
        var rows = (await File.ReadAllLinesAsync(matrix)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should().StartWith("RULE-001,Outbound,10.0.0.1,SRV-APP-01,10.20.1.5,db-oracle.example.test,Internal,TCP,1521,Oracle-TNS,");
    }

    [Fact]
    public async Task ExportRules_WhenPairsOutputGiven_WritesThePairMatrixWithTheSameFilters()
    {
        // Arrange
        var matrix = Path.Combine(_directory, "rules_detailed.csv");
        var pairs = Path.Combine(_directory, "rules_pairs.csv");

        // Act
        await RunAsync("export", "rules", "-o", matrix, "-i", _auditPath, "--zone", "internal", "--pairs-output", pairs);

        // Assert
        File.Exists(matrix).Should().BeTrue();
        var rows = (await File.ReadAllLinesAsync(pairs)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should().StartWith("PAIR-001,10.0.0.1,SRV-APP-01,10.20.1.5,db-oracle.example.test,");
    }

    [Fact]
    public async Task ExportRules_WhenPublicZoneGiven_ExportsOnlyPublicDestinations()
    {
        // Arrange
        var matrix = Path.Combine(_directory, "rules_public.csv");

        // Act
        await RunAsync("export", "rules", "-o", matrix, "-i", _auditPath, "--zone", "public");

        // Assert
        var rows = (await File.ReadAllLinesAsync(matrix)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should().Contain(",203.0.113.10,");
    }

    [Fact]
    public async Task ExportRules_WhenListenersOnlyGiven_ExportsOneInboundRulePerListeningService()
    {
        // Arrange
        var matrix = Path.Combine(_directory, "rules_listeners.csv");

        // Act
        await RunAsync("export", "rules", "-o", matrix, "-i", _auditPath, "--listeners-only");

        // Assert
        (await File.ReadAllLinesAsync(matrix)).Skip(1).Should().ContainSingle()
            .Which.Should().StartWith("RULE-001,Inbound,0.0.0.0,Any,10.0.0.1,SRV-APP-01,Internal,TCP,8080,");
    }

    [Fact]
    public async Task ExportRules_WhenListenersOnlyGivenAndUdpSocketIsBound_ExportsTheUdpService()
    {
        // Arrange
        const string udpListenerRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,3,SRV-DNS-01,Windows,10.0.0.3,Udp,Listening,UDP,0.0.0.0,53,0.0.0.0,0,,5555,dns,C:\\Windows\\System32\\dns.exe,dns.exe,,SYSTEM";
        var audit = Path.Combine(_directory, "audit_dns.csv");
        await File.WriteAllLinesAsync(audit, [Header, udpListenerRow]);
        var matrix = Path.Combine(_directory, "rules_udp_listeners.csv");

        // Act
        await RunAsync("export", "rules", "-o", matrix, "-i", audit, "--listeners-only");

        // Assert
        (await File.ReadAllLinesAsync(matrix)).Skip(1).Should().ContainSingle()
            .Which.Should().StartWith("RULE-001,Inbound,0.0.0.0,Any,0.0.0.0,SRV-DNS-01,Internal,UDP,53,DNS,");
    }

    [Fact]
    public async Task ExportNdjson_WhenCsvAuditGiven_WritesOneJsonLinePerFlow()
    {
        // Arrange
        var ndjson = Path.Combine(_directory, "audit.ndjson");

        // Act
        await RunAsync("export", "ndjson", "-o", ndjson, "-i", _auditPath);

        // Assert
        var lines = await File.ReadAllLinesAsync(ndjson);
        lines.Should().HaveCount(3);
        lines.Should().AllSatisfy(line => JsonDocument.Parse(line).RootElement.ValueKind.Should().Be(JsonValueKind.Object));
    }

    [Fact]
    public async Task Map_WhenIncludedPortGiven_RendersOnlyMatchingFlows()
    {
        // Arrange
        var html = Path.Combine(_directory, "map.html");

        // Act
        await RunAsync("map", "-i", _auditPath, "--html", html, "--include-ports", "1521");

        // Assert
        var content = await File.ReadAllTextAsync(html);
        content.Should().Contain("db-oracle.example.test");
        content.Should().NotContain("203.0.113.10");
    }

    [Fact]
    public async Task Map_WhenWildcardInputGiven_ConsolidatesEveryMatchingAudit()
    {
        // Arrange
        await File.WriteAllLinesAsync(Path.Combine(_directory, "audit_srv2.csv"), [Header, SecondServerRow]);
        var html = Path.Combine(_directory, "fleet.html");

        // Act
        await RunAsync("map", "-i", Path.Combine(_directory, "audit_*.csv"), "--html", html);

        // Assert
        var content = await File.ReadAllTextAsync(html);
        content.Should().Contain("SRV-APP-01");
        content.Should().Contain("SRV-APP-02");
    }

    [Fact]
    public async Task MapDrawio_WhenOnlyDrawioOutputGiven_WritesDiagramWithoutHtmlMap()
    {
        // Arrange
        var drawio = Path.Combine(_directory, "map.drawio");

        // Act
        await RunAsync("map", "-i", _auditPath, "--drawio", drawio, "--drawio-theme", "light");

        // Assert
        File.Exists(drawio).Should().BeTrue();
        (await File.ReadAllTextAsync(drawio)).Should().Contain("<mxfile");
        Directory.GetFiles(_directory, "*.html").Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_WhenCacheOnlyGiven_EnrichesAuditFromCache()
    {
        // Arrange
        var cache = Path.Combine(_directory, "cache.json");
        await File.WriteAllTextAsync(cache, "{\"203.0.113.10\": {\"HostName\": \"git.example.test\", \"Source\": \"DNS\"}}");
        var masterCache = Path.Combine(_directory, "master_cache.json");

        // Act
        await RunAsync("resolve", "-i", _auditPath, "--cache", cache, "--output-cache", masterCache, "--cache-only");

        // Assert
        File.Exists(masterCache).Should().BeTrue();
        var enriched = await File.ReadAllLinesAsync(Path.Combine(_directory, "audit_srv1_enriched.csv"));
        enriched.Should().ContainSingle(line => line.Contains(",203.0.113.10,443,git.example.test,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resolve_WhenCacheGivenWithoutInput_MergesCachesAndLeavesAuditLogsAlone()
    {
        // Arrange
        var cache = Path.Combine(_directory, "other_cache.json");
        await File.WriteAllTextAsync(cache, "{\"203.0.113.10\": {\"HostName\": \"git.example.test\", \"Source\": \"DNS\"}}");
        var masterCache = Path.Combine(_directory, "known_master.json");
        await File.WriteAllTextAsync(masterCache, "{\"10.20.1.5\": {\"HostName\": \"db.example.test\", \"Source\": \"DNS\"}}");

        // Act
        await RunAsync("resolve", "--cache", cache, "--output-cache", masterCache, "--cache-only");

        // Assert
        Directory.GetFiles(_directory, "*_enriched.*").Should().BeEmpty();
        var saved = await File.ReadAllTextAsync(masterCache);
        saved.Should().Contain("git.example.test").And.Contain("db.example.test");
    }

    [Fact]
    public async Task Resolve_WhenNeitherInputNorCacheGivenAndNoAuditExists_ReturnsNonZeroExitCode()
    {
        // Act
        var exitCode = await Program.Main(["resolve", "--config", _emptyConfigPath, "--cache-only"]);

        // Assert
        exitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Resolve_WhenOutputCacheIsUnreadable_ReturnsExitCodeOneNamingTheFileAndLeavesItUnchanged()
    {
        // Arrange
        var masterCache = Path.Combine(_directory, "truncated_master.json");
        const string Truncated = "{\"192.168.1.20\": {\"HostName\": \"pihole.home.arpa\", ";
        await File.WriteAllTextAsync(masterCache, Truncated);
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["resolve", "-i", _auditPath, "--output-cache", masterCache, "--cache-only", "--config", _emptyConfigPath]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain($"'{masterCache}'");
        (await File.ReadAllTextAsync(masterCache)).Should().Be(Truncated);
        Directory.GetFiles(_directory, "*_enriched.*").Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigInit_WhenPathGiven_WritesDeserializableConfiguration()
    {
        // Arrange
        var target = Path.Combine(_directory, "generated.json");

        // Act
        await RunAsync("config", "init", target);

        // Assert
        var configuration = JsonSerializer.Deserialize<AppConfiguration>(await File.ReadAllTextAsync(target));
        configuration.Should().NotBeNull();
        configuration!.OutputCsvPath.Should().Be("netflow_audit.csv");
    }

    [Fact]
    public void AuditConfiguration_WhenNoRdapGiven_DisablesRdapButKeepsReverseDns()
    {
        // Act
        var configuration = AuditCommandConfiguration.Build(new AppConfiguration(), "audit", "--no-rdap");

        // Assert
        configuration.ResolveIpOwners.Should().BeFalse();
        configuration.ResolveRemoteHostNames.Should().BeTrue();
    }

    [Fact]
    public void AuditConfiguration_WhenScanAndOutputOptionsGiven_OverridesConfigurationValues()
    {
        // Act
        var configuration = AuditCommandConfiguration.Build(
            new AppConfiguration(),
            "audit", "--interval", "30", "--columns", "Protocol,RemoteIp", "--no-dns",
            "--exclude-remote-ips", "192.0.2.0/24,198.51.100.7", "--rewrite-interval", "25", "--html", "live.html");

        // Assert
        configuration.ScanIntervalSeconds.Should().Be(30);
        configuration.CsvColumns.Should().Equal("Protocol", "RemoteIp");
        configuration.ResolveRemoteHostNames.Should().BeFalse();
        configuration.ExcludedRemoteIps.Should().Equal("192.0.2.0/24", "198.51.100.7");
        configuration.LogRewriteIntervalMinutes.Should().Be(25);
        configuration.OutputHtmlMapPath.Should().Be("live.html");
    }

    [Fact]
    public async Task ExportRules_WhenConfigurationKeepsEphemeralMapPorts_StillExcludesEphemeralDestinations()
    {
        // Arrange
        const string ephemeralRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,3,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50003,10.20.1.9,50555,,5555,rpc,/usr/bin/rpc,rpc,,svc";
        var audit = Path.Combine(_directory, "ephemeral.csv");
        await File.WriteAllLinesAsync(audit, [Header, OracleRow, ephemeralRow]);
        var config = await WriteConfigurationAsync(new { MapExcludeEphemeralPorts = false });
        var matrix = Path.Combine(_directory, "rules_ephemeral.csv");

        // Act
        await RunWithConfigurationAsync(config, "export", "rules", "-o", matrix, "-i", audit);

        // Assert
        (await File.ReadAllLinesAsync(matrix)).Skip(1).Should().ContainSingle().Which.Should().Contain(",1521,");
    }

    [Fact]
    public async Task Map_WhenConfigurationDefinesDrawioPath_WritesHtmlAndDrawio()
    {
        // Arrange
        var html = Path.Combine(_directory, "configured.html");
        var drawio = Path.Combine(_directory, "configured.drawio");
        var config = await WriteConfigurationAsync(new { OutputHtmlMapPath = html, OutputDrawioPath = drawio });

        // Act
        await RunWithConfigurationAsync(config, "map", "-i", _auditPath);

        // Assert
        File.Exists(html).Should().BeTrue();
        File.Exists(drawio).Should().BeTrue();
    }

    [Fact]
    public async Task Map_WhenConfiguredAuditPathContainsTokens_ReadsTheExpandedFile()
    {
        // Arrange
        var tokenizedAudit = Path.Combine(_directory, "audit_{HostName}.csv");
        File.Copy(_auditPath, OutputFilePathResolver.Resolve(tokenizedAudit));
        var html = Path.Combine(_directory, "tokens.html");
        var config = await WriteConfigurationAsync(new { OutputCsvPath = tokenizedAudit });

        // Act
        await RunWithConfigurationAsync(config, "map", "--html", html);

        // Assert
        (await File.ReadAllTextAsync(html)).Should().Contain("db-oracle.example.test");
    }

    [Fact]
    public async Task Main_WhenConfigurationEnablesRemovedIsolatedMode_ReturnsNonZeroExitCode()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { IsolatedMode = true });

        // Act
        var exitCode = await Program.Main(["export", "ndjson", "-i", _auditPath, "-o", Path.Combine(_directory, "x.ndjson"), "--config", config]);

        // Assert
        exitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Main_WhenConfigurationFileIsMalformed_ReturnsNonZeroExitCode()
    {
        // Arrange
        var config = Path.Combine(_directory, "portatlas.json");
        await File.WriteAllTextAsync(config, """{ "ResolveIpOwners": false, """);
        var output = Path.Combine(_directory, "malformed.ndjson");
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["export", "ndjson", "-i", _auditPath, "-o", output, "--config", config]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain(config).And.Contain("not valid JSON");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Main_WhenConfigurationFileGivenByOptionDoesNotExist_ReturnsExitCodeOne()
    {
        // Arrange
        var config = Path.Combine(_directory, "mistyped.json");
        var output = Path.Combine(_directory, "mistyped.ndjson");
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["export", "ndjson", "-i", _auditPath, "-o", output, "--config", config]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain(config).And.Contain("does not exist");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Main_WhenConfigOptionIsGivenAnEmptyValue_ReturnsExitCodeOne()
    {
        // Arrange
        var output = Path.Combine(_directory, "empty-option.ndjson");
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["export", "ndjson", "-i", _auditPath, "-o", output, "--config", ""]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain("--config").And.Contain("is empty");
        File.Exists(output).Should().BeFalse();
    }

    // A filter value that cannot be read used to be dropped without a word: an unreadable inclusion then meant
    // "no filter" in the matrix, and an unreadable port ended the map command with a stack trace.
    [Theory]
    [InlineData("rules", "--include-ports", "http")]
    [InlineData("rules", "--include-ports", "70000")]
    [InlineData("rules", "--exclude-ports", "8080-8000")]
    [InlineData("rules", "--include-ips", "nas")]
    [InlineData("rules", "--exclude-ips", "192.168.1.0/33")]
    [InlineData("map", "--include-ports", "http")]
    [InlineData("map", "--exclude-ports", "8080-8000")]
    [InlineData("map", "--include-ips", "nas")]
    [InlineData("map", "--exclude-ips", "192.168.1.0/33")]
    public async Task Main_WhenFlowFilterValueIsUnreadable_ReturnsExitCodeOneNamingTheOptionAndTheValue(
        string command,
        string option,
        string value)
    {
        // Arrange
        var output = Path.Combine(_directory, "unreadable-filter.out");
        string[] commandArguments = command == "rules"
            ? ["export", "rules", "-i", _auditPath, "-o", output]
            : ["map", "-i", _auditPath, "--html", output];
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main([.. commandArguments, option, value, "--config", _emptyConfigPath]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain(option).And.Contain($"'{value}'");
        File.Exists(output).Should().BeFalse();
    }

    // These audits stop before the first scan: the log is read, and written once, before the audit loop starts.
    [Fact]
    public async Task Main_WhenTheAuditLogToResumeHasAnUnreadableValue_ReturnsExitCodeOneNamingTheLine()
    {
        // Arrange
        var log = Path.Combine(_directory, "unreadable_value_audit.csv");
        await File.WriteAllLinesAsync(log, [ResumableHeader, ResumableRow.Replace(",Tcp,", ",Sctp,", StringComparison.Ordinal)]);

        // Act
        var (exitCode, output) = await RunAuditAsync(log);

        // Assert
        exitCode.Should().Be(1);
        output.Should().Contain("Audit log error").And.Contain("line 2").And.Contain("'Sctp'");
    }

    [Fact]
    public async Task Main_WhenARewriteLeftTheCompleteLogInPending_StartsFromIt()
    {
        // Arrange
        var log = Path.Combine(_directory, "interrupted_audit.csv");
        await File.WriteAllLinesAsync($"{log}.pending", [ResumableHeader, ResumableRow.Replace(",Tcp,", ",Sctp,", StringComparison.Ordinal)]);

        // Act
        var (exitCode, output) = await RunAuditAsync(log);

        // Assert
        exitCode.Should().Be(1);
        output.Should().Contain("'Sctp'");
        File.Exists(log).Should().BeTrue();
        File.Exists($"{log}.pending").Should().BeFalse();
    }

    [Fact]
    public async Task Main_WhenTheAuditLogIsHeldOpenAtStart_ReturnsExitCodeOneNamingTheLog()
    {
        // Arrange
        var log = Path.Combine(_directory, "held_audit.csv");
        await File.WriteAllLinesAsync(log, [ResumableHeader, ResumableRow]);
        using var holder = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Act
        var (exitCode, output) = await RunAuditAsync(log);

        // Assert
        exitCode.Should().Be(1);
        output.Should().Contain("Audit log error").And.Contain(Path.GetFileName(log));
    }

    [Fact]
    public async Task Main_WhenTheColumnsLeaveOutTheFlowKey_ReturnsExitCodeOneNamingTheMissingColumns()
    {
        // Arrange
        var log = Path.Combine(_directory, "reduced_columns_audit.csv");

        // Act
        var (exitCode, output) = await RunAuditAsync(log, "--columns", "Protocol,RemoteIp,RemotePort");

        // Assert
        exitCode.Should().Be(1);
        output.Should().Contain("Column configuration error").And.Contain("FirstSeenUtc").And.Contain("LocalPort");
        File.Exists(log).Should().BeFalse();
    }

    [Fact]
    public async Task Main_WhenTheDnsCacheIsUnreadableAtAuditStart_ReturnsExitCodeOneNamingTheCacheAndWritesNothing()
    {
        // Arrange
        var log = Path.Combine(_directory, "cache_refused_audit.csv");
        var cache = Path.Combine(_directory, "truncated_dns_cache.json");
        const string Truncated = "{\"192.168.1.20\": {\"HostName\": \"pihole.home.arpa\", ";
        await File.WriteAllTextAsync(cache, Truncated);
        var config = await WriteConfigurationAsync(new { DnsCacheFilePath = cache });
        var consoleOut = Console.Out;
        var consoleError = Console.Error;
        using var capturedOut = new StringWriter();
        using var capturedError = new StringWriter();
        Console.SetOut(capturedOut);
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            var audit = Program.Main(["audit", "--output", log, "--no-tui", "--no-rdap", "--config", config]);
            var finished = await Task.WhenAny(audit, Task.Delay(TimeSpan.FromSeconds(20)));
            finished.Should().BeSameAs(audit, "the audit must stop before its first scan");
            exitCode = await audit;
        }
        finally
        {
            Console.SetOut(consoleOut);
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        // A stack frame line starts with "   at "; the JSON error quoted in the message may itself contain " at ".
        capturedError.ToString().Should().Contain($"'{cache}'").And.NotContain("   at ");
        (await File.ReadAllTextAsync(cache)).Should().Be(Truncated);
        File.Exists(log).Should().BeFalse();
    }

    private const string ResumableHeader =
        "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable";

    private const string ResumableRow =
        "2026-11-01T08:00:00.0000000Z,2026-11-02T08:00:00.0000000Z,3,proxmox,Tcp,Outbound,51000,192.168.1.30,8007,proxmox-backup-client,/usr/bin/proxmox-backup-client,";

    private async Task<(int ExitCode, string Output)> RunAuditAsync(string log, params string[] options)
    {
        var consoleOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        try
        {
            var audit = Program.Main(["audit", "--output", log, "--no-tui", "--no-dns", "--no-rdap", "--config", _emptyConfigPath, .. options]);
            var finished = await Task.WhenAny(audit, Task.Delay(TimeSpan.FromSeconds(20)));
            finished.Should().BeSameAs(audit, "the audit must stop before its first scan");
            return (await audit, capturedOut.ToString());
        }
        finally
        {
            Console.SetOut(consoleOut);
        }
    }

    // A command line spanning lines is one record; a quote never closed leaves the end of the log unreadable.
    [Theory]
    [InlineData("rules")]
    [InlineData("resolve")]
    public async Task Main_WhenAnAuditLogHasAQuoteNeverClosed_ReturnsExitCodeOneNamingTheLogAndTheLine(string command)
    {
        // Arrange
        var log = Path.Combine(_directory, "truncated_audit.csv");
        await File.WriteAllLinesAsync(log, [Header, OracleRow, PublicRow.Replace(",git fetch,", ",\"git fetch,", StringComparison.Ordinal)]);
        var output = command == "rules" ? Path.Combine(_directory, "truncated.out") : Path.Combine(_directory, "truncated_audit_enriched.csv");
        string[] commandArguments = command == "rules"
            ? ["export", "rules", "-i", log, "-o", output]
            : ["resolve", "-i", log];
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main([.. commandArguments, "--config", _emptyConfigPath]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain("truncated_audit.csv").And.Contain("line 3").And.NotContain(" at ");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Main_WhenFlowFilterKeyOfTheConfigurationIsUnreadable_ReturnsExitCodeOneNamingTheKey()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { MapExcludedIps = new[] { "192.168.1.0/24", "pihole" } });
        var matrix = Path.Combine(_directory, "unreadable-key.csv");
        var consoleError = Console.Error;
        using var capturedError = new StringWriter();
        Console.SetError(capturedError);

        // Act
        int exitCode;
        try
        {
            exitCode = await Program.Main(["export", "rules", "-i", _auditPath, "-o", matrix, "--config", config]);
        }
        finally
        {
            Console.SetError(consoleError);
        }

        // Assert
        exitCode.Should().Be(1);
        capturedError.ToString().Should().Contain("MapExcludedIps").And.Contain("'pihole'");
        File.Exists(matrix).Should().BeFalse();
    }

    // Same reading as the topology map, which masks the host bits of a subnet.
    [Fact]
    public async Task ExportRules_WhenIncludedSubnetIsWrittenWithHostBits_KeepsTheFlowsOfTheSubnet()
    {
        // Arrange
        var matrix = Path.Combine(_directory, "host-bits.csv");

        // Act
        await RunAsync("export", "rules", "-o", matrix, "-i", _auditPath, "--include-ips", "10.20.1.99/24");

        // Assert
        var rows = (await File.ReadAllLinesAsync(matrix)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should().Contain(",10.20.1.5,db-oracle.example.test,");
    }

    // A configuration list can hold several ports in one string, which the topology map already splits.
    [Fact]
    public async Task ExportRules_WhenConfigurationGivesSeveralPortsInOneString_KeepsEachOfThem()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { MapIncludedPorts = new[] { "1521; 8443" } });
        var matrix = Path.Combine(_directory, "ports-in-one-string.csv");

        // Act
        await RunWithConfigurationAsync(config, "export", "rules", "-o", matrix, "-i", _auditPath);

        // Assert
        var rows = (await File.ReadAllLinesAsync(matrix)).Skip(1).ToList();
        rows.Should().ContainSingle().Which.Should().Contain(",TCP,1521,");
    }

    private Task RunAsync(params string[] commandArguments) =>
        RunWithConfigurationAsync(_emptyConfigPath, commandArguments);

    private static async Task RunWithConfigurationAsync(string configPath, params string[] commandArguments)
    {
        var exitCode = await Program.Main([.. commandArguments, "--config", configPath]);
        exitCode.Should().Be(0);
    }

    private async Task<string> WriteConfigurationAsync(object values)
    {
        var path = Path.Combine(_directory, "portatlas.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(values));
        return path;
    }
}
