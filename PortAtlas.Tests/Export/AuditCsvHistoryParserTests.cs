using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Export;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditCsvHistoryParserTests : IDisposable
{
    private readonly string _tempDirectory;

    public AuditCsvHistoryParserTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "netflow_history_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenFileNotFound_ReturnsEmptyList()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var nonExistentPath = Path.Combine(_tempDirectory, "does_not_exist.csv");

        // Act
        var entries = await parser.ParseHistoryAsync(nonExistentPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenEmptyFile_ReturnsEmptyList()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var emptyPath = Path.Combine(_tempDirectory, "empty.csv");
        await File.WriteAllTextAsync(emptyPath, string.Empty);

        // Act
        var entries = await parser.ParseHistoryAsync(emptyPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenHeaderOnly_ReturnsEmptyList()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var headerOnlyPath = Path.Combine(_tempDirectory, "header_only.csv");
        await File.WriteAllTextAsync(headerOnlyPath, "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,Protocol,LocalPort,RemoteIp,RemotePort,ProcessName\n");

        // Act
        var entries = await parser.ParseHistoryAsync(headerOnlyPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenMaxRowsIsZero_ReturnsEmptyList()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "valid.csv");
        var csvContent = """
            FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName
            2026-10-24T07:00:00.0000000Z,2026-10-24T07:05:00.0000000Z,10,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,1.1.1.1,443,one.one.one.one,1234,curl,/usr/bin/curl,curl https://1.1.1.1,,appuser
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: 0, delimiter: ',');

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenValidCsvWithStandardHeader_ParsesFieldsAccurately()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "two_rows.csv");
        var csvContent = """
            FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName
            2026-10-24T07:00:00.0000000Z,2026-10-24T07:05:00.0000000Z,15,SRV-01,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,54321,172.217.119.4,443,google.com,4321,chrome,C:\Chrome\chrome.exe,chrome.exe https://google.com,,user1
            2026-10-24T07:01:00.0000000Z,2026-10-24T07:06:00.0000000Z,5,SRV-01,Windows,192.168.1.10,Tcp,Listening,Listen,0.0.0.0,8080,0.0.0.0,0,,9999,java,C:\Java\java.exe,java -jar app.jar,app.jar,appservice
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().HaveCount(2);

        var first = entries[0];
        first.Server.HostName.Should().Be("SRV-01");
        first.Server.OperatingSystemDescription.Should().Be("Windows");
        first.Server.PrimaryIpAddress.Should().Be("192.168.1.10");
        first.ObservationCount.Should().Be(15);
        first.Socket.Protocol.Should().Be(SocketTransportProtocol.Tcp);
        first.Socket.Direction.Should().Be(SocketFlowDirection.Outbound);
        first.Socket.LocalEndpoint.Port.Should().Be(54321);
        first.Socket.RemoteEndpoint.Address.Should().Be(IPAddress.Parse("172.217.119.4"));
        first.Socket.RemoteEndpoint.Port.Should().Be(443);
        first.RemoteHostName.Should().Be("google.com");
        first.Socket.Process.ProcessName.Should().Be("chrome");

        var second = entries[1];
        second.Socket.Direction.Should().Be(SocketFlowDirection.Listening);
        second.Socket.LocalEndpoint.Port.Should().Be(8080);
        second.Socket.Process.TargetExecutable.Should().Be("app.jar");
        second.Socket.Process.UserName.Should().Be("appservice");
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenMaxRowsSpecified_ReturnsOnlyMostRecentNEntries()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "three_rows.csv");
        var csvContent = """
            FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName
            2026-10-24T07:00:00.0000000Z,2026-10-24T07:00:00.0000000Z,1,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,1001,1.1.1.1,80,,1,p1,,,,,
            2026-10-24T07:01:00.0000000Z,2026-10-24T07:01:00.0000000Z,1,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,1002,1.1.1.2,80,,2,p2,,,,,
            2026-10-24T07:02:00.0000000Z,2026-10-24T07:02:00.0000000Z,1,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,1003,1.1.1.3,80,,3,p3,,,,,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act (request last 2 rows)
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: 2, delimiter: ',');

        // Assert
        entries.Should().HaveCount(2);
        entries[0].Socket.Process.ProcessName.Should().Be("p2");
        entries[1].Socket.Process.ProcessName.Should().Be("p3");
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenHeaderMissing_UsesStandardPositionalMapping()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "no_header.csv");
        // No header line, straight 20 columns data
        var csvContent = "2026-10-24T07:00:00.0000000Z,2026-10-24T07:05:00.0000000Z,3,SRV-HEADLESS,Linux,10.0.0.5,Tcp,Outbound,Established,10.0.0.5,45000,8.8.8.8,53,dns.google,555,dig,/usr/bin/dig,dig @8.8.8.8,,root\n";
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().HaveCount(1);
        entries[0].Server.HostName.Should().Be("SRV-HEADLESS");
        entries[0].Socket.RemoteEndpoint.Address.Should().Be(IPAddress.Parse("8.8.8.8"));
        entries[0].Socket.RemoteEndpoint.Port.Should().Be(53);
        entries[0].RemoteHostName.Should().Be("dns.google");
        entries[0].Socket.Process.ProcessName.Should().Be("dig");
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenValuesContainEscapedQuotesAndDelimiters_ParsesAccurately()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "escaped.csv");
        var csvContent = """
            FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName
            2026-10-24T07:00:00.0000000Z,2026-10-24T07:05:00.0000000Z,1,SRV-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.0.0.2,443,,1234,runner,"/opt/app/my runner.sh","runner --arg ""quoted,value"" --flag",app.sh,"DOMAIN\User, Special"
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Socket.Process.ExecutablePath.Should().Be("/opt/app/my runner.sh");
        entry.Socket.Process.CommandLine.Should().Be("runner --arg \"quoted,value\" --flag");
        entry.Socket.Process.UserName.Should().Be("DOMAIN\\User, Special");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenFlowKeyColumnsAreMissing_ThrowsNamingTheMissingColumns()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "reduced_columns.csv");
        var csvContent = """
            FirstSeenUtc,LastSeenUtc,ServerName,LocalPort,RemoteIp,ProcessName,ExecutablePath,TargetExecutable
            2026-11-01T08:00:00.0000000Z,2026-11-01T08:05:00.0000000Z,proxmox,51000,192.168.1.30,proxmox-backup-client,/usr/bin/proxmox-backup-client,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var act = () => parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*reduced_columns.csv*Protocol, Direction, RemotePort*");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenTheFirstSeenColumnIsMissing_ThrowsNamingIt()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "no_first_seen.csv");
        var csvContent = """
            LastSeenUtc,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable
            2026-11-01T08:05:00.0000000Z,proxmox,Tcp,Outbound,51000,192.168.1.30,8007,proxmox-backup-client,/usr/bin/proxmox-backup-client,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var act = () => parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*FirstSeenUtc*");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenTheProcessNameColumnIsMissing_ThrowsNamingIt()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "no_process_name.csv");
        var csvContent = """
            FirstSeenUtc,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ExecutablePath,TargetExecutable
            2026-11-01T08:00:00.0000000Z,nas,Tcp,Outbound,51000,192.168.1.2,53,,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var act = () => parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*no ProcessName column*");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenAFlowWithoutExecutablePathIsReadBack_MatchesTheScannedFlowOfTheSameProcess()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "unpathed_flow.csv");
        var csvContent = """
            FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable
            2026-11-01T08:00:00.0000000Z,2026-11-01T08:05:00.0000000Z,3,nas,Tcp,Outbound,51000,192.168.1.2,53,lsass,,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);
        var scannedSocket = new ActiveSocketConnection(
            SocketTransportProtocol.Tcp,
            SocketFlowDirection.Outbound,
            new SocketEndpoint(IPAddress.Parse("192.168.1.10"), 51234),
            new SocketEndpoint(IPAddress.Parse("192.168.1.2"), 53),
            "Established",
            new ProcessMetadata(812, "lsass", string.Empty, string.Empty, string.Empty, string.Empty));
        var otherProcessSocket = scannedSocket with
        {
            Process = new ProcessMetadata(940, "svchost", string.Empty, string.Empty, string.Empty, string.Empty)
        };

        // Act
        var entries = await parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        var readBackKey = entries.Should().ContainSingle().Which.ComputeAuditKey();
        readBackKey.Should().Be(scannedSocket.ComputeLogicalKey());
        readBackKey.Should().NotBe(otherProcessSocket.ComputeLogicalKey());
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenAFirstSeenDateIsUnreadable_ThrowsNamingTheLine()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "unreadable_date.csv");
        var csvContent = """
            FirstSeenUtc,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable
            2026-11-01T08:00:00.0000000Z,proxmox,Tcp,Outbound,51000,192.168.1.30,8007,proxmox-backup-client,/usr/bin/proxmox-backup-client,
            yesterday,proxmox,Udp,Outbound,51001,192.168.1.2,53,pveproxy,/usr/bin/pveproxy,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var act = () => parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*line 3*yesterday*");
    }

    [Theory]
    [InlineData("Protocol", "Sctp")]
    [InlineData("Protocol", "7")]
    [InlineData("Direction", "Sideways")]
    [InlineData("LocalIp", "nas")]
    [InlineData("LocalPort", "")]
    [InlineData("LocalPort", "70000")]
    [InlineData("RemoteIp", "pbs")]
    [InlineData("RemotePort", "-1")]
    [InlineData("LastSeenUtc", "tomorrow")]
    [InlineData("ObservationCount", "many")]
    [InlineData("ObservationCount", "0")]
    public async Task ParseLogForResumeAsync_WhenAValueIsUnreadable_ThrowsNamingTheLineAndTheColumn(string column, string value)
    {
        // Arrange
        string[] columns = ["FirstSeenUtc", "LastSeenUtc", "ObservationCount", "ServerName", "Protocol", "Direction", "LocalIp", "LocalPort", "RemoteIp", "RemotePort", "ProcessName", "ExecutablePath", "TargetExecutable"];
        string[] readable = ["2026-11-01T08:00:00.0000000Z", "2026-11-02T08:00:00.0000000Z", "3", "proxmox", "Tcp", "Outbound", "192.168.1.20", "51000", "192.168.1.30", "8007", "proxmox-backup-client", "/usr/bin/proxmox-backup-client", ""];
        var unreadable = (string[])readable.Clone();
        unreadable[Array.IndexOf(columns, column)] = value;
        var csvPath = Path.Combine(_tempDirectory, "unreadable_value.csv");
        await File.WriteAllLinesAsync(csvPath, [string.Join(',', columns), string.Join(',', readable), string.Join(',', unreadable)]);

        // Act
        var act = () => new AuditCsvHistoryParser().ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage($"*line 3*{column}*'{value}'*");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenOptionalValuesAreBlank_ReturnsTheFlow()
    {
        // Arrange
        var csvPath = Path.Combine(_tempDirectory, "blank_optional_values.csv");
        await File.WriteAllLinesAsync(csvPath,
        [
            "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,Protocol,Direction,LocalIp,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable",
            "2026-11-01T08:00:00.0000000Z,,,proxmox,Tcp,Outbound,,51000,192.168.1.30,8007,proxmox-backup-client,/usr/bin/proxmox-backup-client,"
        ]);

        // Act
        var entries = await new AuditCsvHistoryParser().ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().ContainSingle().Which.ObservationCount.Should().Be(1);
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenReducedColumnsKeepTheFlowKey_ReturnsTheFlows()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "key_columns.csv");
        var csvContent = """
            FirstSeenUtc,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable
            2026-11-01T08:00:00.0000000Z,proxmox,Udp,Outbound,51001,192.168.1.2,53,pveproxy,/usr/bin/pveproxy,
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        var entry = entries.Should().ContainSingle().Subject;
        entry.Socket.Protocol.Should().Be(SocketTransportProtocol.Udp);
        entry.FirstSeenUtc.Should().Be(new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenTheLogDoesNotExist_ReturnsEmptyList()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "not_written_yet.csv");

        // Act
        var entries = await parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenFlowKeyColumnsAreMissing_StillReadsTheLog()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "reduced_for_export.csv");
        var csvContent = """
            FirstSeenUtc,ServerName,RemoteIp,ProcessName
            2026-11-01T08:00:00.0000000Z,proxmox,192.168.1.30,proxmox-backup-client
            """;
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().ContainSingle().Which.Socket.RemoteEndpoint.Address.Should().Be(IPAddress.Parse("192.168.1.30"));
    }

    [Fact]
    public async Task ParseHistoryAsync_WhenACommandLineSpansLines_ReadsEachFlowWhole()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "multiline_command.csv");
        var csvContent =
            "FirstSeenUtc,ServerName,LocalPort,RemoteIp,RemotePort,ProcessName,CommandLine\r\n" +
            "2026-11-01T08:00:00.0000000Z,nas,51000,192.168.1.30,8007,java,\"java -jar backup.jar\r\n--target pbs.home.arpa\"\r\n" +
            "2026-11-01T08:01:00.0000000Z,nas,51001,192.168.1.2,53,dnsmasq,dnsmasq\r\n";
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseHistoryAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Select(entry => entry.Socket.Process.CommandLine).Should().Equal("java -jar backup.jar\r\n--target pbs.home.arpa", "dnsmasq");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenAnUnreadableDateFollowsAMultilineRecord_NamesTheLineTheRecordStartsOn()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "multiline_then_bad_date.csv");
        var csvContent =
            "FirstSeenUtc,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable,CommandLine\r\n" +
            "2026-11-01T08:00:00.0000000Z,nas,Tcp,Outbound,51000,192.168.1.30,8007,java,/usr/bin/java,,\"java -jar backup.jar\r\n--target pbs.home.arpa\"\r\n" +
            "yesterday,nas,Udp,Outbound,51001,192.168.1.2,53,dnsmasq,/usr/sbin/dnsmasq,,dnsmasq\r\n";
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var act = () => parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*line 4*yesterday*");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenAQuotedFieldIsNeverClosed_ThrowsNamingTheLogAndTheLine()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "unclosed_quote.csv");
        var csvContent =
            "FirstSeenUtc,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort,ProcessName,ExecutablePath,TargetExecutable,CommandLine\r\n" +
            "2026-11-01T08:00:00.0000000Z,nas,Udp,Outbound,51001,192.168.1.2,53,dnsmasq,/usr/sbin/dnsmasq,,dnsmasq\r\n" +
            "2026-11-01T08:01:00.0000000Z,nas,Tcp,Outbound,51000,192.168.1.30,8007,java,/usr/bin/java,,\"java -jar backup.jar\r\n";
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var act = () => parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        await act.Should().ThrowAsync<AuditLogFormatException>().WithMessage("*unclosed_quote.csv*line 3*");
    }

    [Fact]
    public async Task ParseLogForResumeAsync_WhenTheLogHasNoHeader_ReadsItByPosition()
    {
        // Arrange
        var parser = new AuditCsvHistoryParser();
        var csvPath = Path.Combine(_tempDirectory, "positional.csv");
        var csvContent = "2026-11-01T08:00:00.0000000Z,2026-11-01T08:05:00.0000000Z,3,proxmox,Debian 12,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,51000,192.168.1.30,8007,pbs.home.arpa,300,proxmox-backup-client,/usr/bin/proxmox-backup-client,,,root\n";
        await File.WriteAllTextAsync(csvPath, csvContent);

        // Act
        var entries = await parser.ParseLogForResumeAsync(csvPath, maxRows: null, delimiter: ',');

        // Assert
        entries.Should().ContainSingle().Which.RemoteHostName.Should().Be("pbs.home.arpa");
    }
}
