using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class FirewallRuleExporterTests : IDisposable
{
    private readonly string _tempDirectory;

    public FirewallRuleExporterTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PortAtlas_RuleExportTests_" + Guid.NewGuid().ToString("N"));
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
                // Tolerated in cleanup
            }
        }
    }

    [Fact]
    public async Task ExportRulesAsync_WhenValidCsvInput_GeneratesFormattedFirewallMatrixFile()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row1 = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.20.1.5,1521,db-oracle.prod,1234,tnslsnr,/usr/bin/tnslsnr,tnslsnr --run,,oracle";
        var row2 = "2026-10-24T07:01:00Z,2026-10-24T07:06:00Z,25,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50002,10.20.1.5,1521,db-oracle.prod,1234,tnslsnr,/usr/bin/tnslsnr,tnslsnr --run,,oracle";
        await File.WriteAllLinesAsync(auditCsv, [header, row1, row2]);

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var exporter = new FirewallRuleExporter();
        var options = new FirewallRuleExportOptions();

        // Act
        var report = await exporter.ExportRulesAsync([auditCsv], targetMatrixCsv, options);

        // Assert
        report.TotalRawFlowsScanned.Should().Be(2);
        report.TotalRulesExported.Should().Be(1);
        report.DestinationCsvPath.Should().Be(targetMatrixCsv);

        File.Exists(targetMatrixCsv).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(targetMatrixCsv);
        lines.Should().HaveCount(2); // Header + 1 consolidated rule
        lines[0].Should().StartWith("RuleId,Direction,SourceIp,");
        lines[1].Should().StartWith("FLUX-001,Outbound,10.0.0.1,SRV-APP-01,10.20.1.5,db-oracle.prod,Internal,TCP,1521,Oracle-TNS,tnslsnr,");
        lines[1].Should().Contain(",35,"); // Sum of observation count (10 + 25)
        report.PairsCsvPath.Should().BeNull();
    }

    [Theory]
    [InlineData(';', ',')]
    [InlineData(';', ';')]
    [InlineData('\t', ',')]
    [InlineData(',', ';')]
    public async Task ExportRulesAsync_WhenAuditCsvHeaderUsesAnotherSeparator_ReadsTheFlowsWithTheSeparatorOfTheFile(char fileSeparator, char configuredSeparator)
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        string[] header = ["FirstSeenUtc", "LastSeenUtc", "ObservationCount", "ServerName", "ServerOsVersion", "ServerPrimaryIp", "Protocol", "Direction", "TcpState", "LocalIp", "LocalPort", "RemoteIp", "RemotePort", "RemoteHostName", "ProcessId", "ProcessName", "ExecutablePath", "CommandLine", "TargetExecutable", "UserName"];
        string[] https = ["2026-10-24T07:00:00Z", "2026-10-24T07:05:00Z", "10", "desktop", "Windows", "192.168.1.10", "Tcp", "Outbound", "Established", "192.168.1.10", "50000", "192.168.1.50", "443", "nas.home.arpa", "1234", "curl", "C:\\curl.exe", "curl", "", "user"];
        string[] rsync = ["2026-10-24T07:01:00Z", "2026-10-24T07:06:00Z", "25", "desktop", "Windows", "192.168.1.10", "Tcp", "Outbound", "Established", "192.168.1.10", "50002", "192.168.1.50", "873", "nas.home.arpa", "1235", "rsync", "C:\\rsync.exe", "rsync", "", "user"];
        await File.WriteAllLinesAsync(auditCsv, [string.Join(fileSeparator, header), string.Join(fileSeparator, https), string.Join(fileSeparator, rsync)]);

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var options = new FirewallRuleExportOptions { Delimiter = configuredSeparator };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync([auditCsv], targetMatrixCsv, options);

        // Assert
        report.TotalRawFlowsScanned.Should().Be(2);
        report.TotalRulesExported.Should().Be(2);
        var lines = await File.ReadAllLinesAsync(targetMatrixCsv);
        lines.Should().HaveCount(3);
        lines[1].Split(configuredSeparator).Should().ContainInOrder("192.168.1.10", "desktop", "192.168.1.50", "nas.home.arpa", "Internal", "TCP", "443");
    }

    [Fact]
    public async Task ExportRulesAsync_WhenAuditCsvHasNoHeader_ReadsTheFlowsWithTheConfiguredSeparator()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var https = "2026-10-24T07:00:00Z;2026-10-24T07:05:00Z;10;desktop;Windows;192.168.1.10;Tcp;Outbound;Established;192.168.1.10;50000;192.168.1.50;443;nas.home.arpa;1234;curl;C:\\curl.exe;curl -H a,b;;user";
        await File.WriteAllLinesAsync(auditCsv, [https]);

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var options = new FirewallRuleExportOptions { Delimiter = ';' };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync([auditCsv], targetMatrixCsv, options);

        // Assert
        report.TotalRawFlowsScanned.Should().Be(1);
        report.TotalRulesExported.Should().Be(1);
    }

    [Fact]
    public async Task ExportRulesAsync_WhenAuditCsvHasNoHeaderAndAnotherSeparatorThanTheConfiguredOne_ReadsTheFlowsWithTheSeparatorOfTheFile()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var https = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50000,192.168.1.50,443,nas.home.arpa,1234,curl,C:\\curl.exe,\"curl -H a;b\",,user";
        await File.WriteAllLinesAsync(auditCsv, [https]);

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var options = new FirewallRuleExportOptions { Delimiter = ';' };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync([auditCsv], targetMatrixCsv, options);

        // Assert
        report.TotalRawFlowsScanned.Should().Be(1);
        report.TotalRulesExported.Should().Be(1);
    }

    [Fact]
    public async Task ExportRulesAsync_WhenAuditCsvStartsWithAByteOrderMark_ReadsTheFlowsWithTheSeparatorOfTheFile()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc;LastSeenUtc;ObservationCount;ServerName;ServerOsVersion;ServerPrimaryIp;Protocol;Direction;TcpState;LocalIp;LocalPort;RemoteIp;RemotePort;RemoteHostName;ProcessId;ProcessName;ExecutablePath;CommandLine;TargetExecutable;UserName";
        var https = "2026-10-24T07:00:00Z;2026-10-24T07:05:00Z;10;desktop;Windows;192.168.1.10;Tcp;Outbound;Established;192.168.1.10;50000;192.168.1.50;443;nas.home.arpa;1234;curl;C:\\curl.exe;curl;;user";
        await File.WriteAllLinesAsync(auditCsv, [header, https], new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync([auditCsv], targetMatrixCsv, new FirewallRuleExportOptions());

        // Assert
        report.TotalRawFlowsScanned.Should().Be(1);
        report.TotalRulesExported.Should().Be(1);
    }

    [Fact]
    public async Task ExportRulesAsync_WhenTwoAuditCsvUseDifferentSeparators_ReadsEachFileWithItsOwnSeparator()
    {
        // Arrange
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var https = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50000,192.168.1.50,443,nas.home.arpa,1234,curl,C:\\curl.exe,curl,,user";
        var dns = "2026-10-24T07:01:00Z,2026-10-24T07:06:00Z,25,proxmox,Linux,192.168.1.20,Udp,Outbound,UDP,192.168.1.20,50002,192.168.1.2,53,pihole.home.arpa,1235,systemd-resolve,/usr/lib/systemd/systemd-resolved,systemd-resolved,,systemd-resolve";
        var commaCsv = Path.Combine(_tempDirectory, "desktop.csv");
        var semicolonCsv = Path.Combine(_tempDirectory, "proxmox.csv");
        await File.WriteAllLinesAsync(commaCsv, [header, https]);
        await File.WriteAllLinesAsync(semicolonCsv, [header.Replace(',', ';'), dns.Replace(',', ';')]);

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync([commaCsv, semicolonCsv], targetMatrixCsv, new FirewallRuleExportOptions());

        // Assert
        report.TotalRawFlowsScanned.Should().Be(2);
        report.TotalRulesExported.Should().Be(2);
    }

    [Fact]
    public async Task ExportRulesAsync_WhenPairsOutputPathIsSet_WritesThePairMatrixBesideTheDetailedOne()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var https = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50000,192.168.1.50,443,nas.home.arpa,1234,curl,C:\\curl.exe,curl,,user";
        var rsync = "2026-10-24T07:01:00Z,2026-10-24T07:06:00Z,25,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50002,192.168.1.50,873,nas.home.arpa,1235,rsync,C:\\rsync.exe,rsync,,user";
        await File.WriteAllLinesAsync(auditCsv, [header, https, rsync]);

        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var pairsCsv = Path.Combine(_tempDirectory, "pairs", "firewall_pairs.csv");
        var options = new FirewallRuleExportOptions { PairsOutputPath = pairsCsv };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync([auditCsv], targetMatrixCsv, options);

        // Assert
        report.TotalRulesExported.Should().Be(2);
        report.TotalPairsExported.Should().Be(1);
        report.PairsCsvPath.Should().Be(pairsCsv);
        var lines = await File.ReadAllLinesAsync(pairsCsv);
        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("PairId,SourceIp,SourceHost,");
        lines[1].Should().StartWith("PAIR-001,192.168.1.10,desktop,192.168.1.50,nas.home.arpa,2,\"443/TCP, 873/TCP\",35,");
    }
}
