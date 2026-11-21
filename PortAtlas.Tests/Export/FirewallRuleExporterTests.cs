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
        lines[1].Should().StartWith("RULE-001,Outbound,10.0.0.1,SRV-APP-01,10.20.1.5,db-oracle.prod,Internal,TCP,1521,Oracle-TNS,tnslsnr,");
        lines[1].Should().Contain(",35,"); // Sum of observation count (10 + 25)
        report.PairsCsvPath.Should().BeNull();
        report.Flows.Should().BeNull();
        report.HostRules.Should().BeNull();
        report.Listeners.Should().BeNull();
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

    // Spreadsheet programs need the byte order mark to show accented process or host names.
    [Fact]
    public async Task ExportRulesAsync_WhenMatricesAreWritten_StartsEachWithAUtf8ByteOrderMark()
    {
        // Arrange
        var auditCsv = await WriteNasAuditAsync();
        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var pairsCsv = Path.Combine(_tempDirectory, "firewall_pairs.csv");
        var flowsCsv = Path.Combine(_tempDirectory, "firewall_flows.csv");
        var hostRulesCsv = Path.Combine(_tempDirectory, "firewall_host_rules.csv");
        var listenersCsv = Path.Combine(_tempDirectory, "firewall_listeners.csv");
        var options = new FirewallRuleExportOptions
        {
            PairsOutputPath = pairsCsv,
            FlowsOutputPath = flowsCsv,
            HostRulesOutputPath = hostRulesCsv,
            ListenersOutputPath = listenersCsv
        };

        // Act
        await new FirewallRuleExporter().ExportRulesAsync([auditCsv], targetMatrixCsv, options);

        // Assert
        byte[] byteOrderMark = [0xEF, 0xBB, 0xBF];
        (await File.ReadAllBytesAsync(targetMatrixCsv)).Take(3).Should().Equal(byteOrderMark);
        (await File.ReadAllBytesAsync(pairsCsv)).Take(3).Should().Equal(byteOrderMark);
        (await File.ReadAllBytesAsync(flowsCsv)).Take(3).Should().Equal(byteOrderMark);
        (await File.ReadAllBytesAsync(hostRulesCsv)).Take(3).Should().Equal(byteOrderMark);
        (await File.ReadAllBytesAsync(listenersCsv)).Take(3).Should().Equal(byteOrderMark);
    }

    // Stopped between the two matrices (Ctrl+C), the export used to open the pair matrix, emptying it, before it
    // noticed the cancellation.
    [Fact]
    public async Task ExportRulesAsync_WhenCancelledBeforeThePairMatrixIsWritten_KeepsThePreviousPairMatrix()
    {
        // Arrange
        var auditCsv = await WriteNasAuditAsync();
        var targetMatrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var pairsCsv = Path.Combine(_tempDirectory, "firewall_pairs.csv");
        const string previousPairs = "PairId,SourceIp\r\nPAIR-001,192.168.1.10\r\n";
        await File.WriteAllTextAsync(pairsCsv, previousPairs);
        using var interruption = new CancellationTokenSource();
        var cancellation = Task.CompletedTask;
        var exporter = new FirewallRuleExporter(logger: message =>
        {
            if (message.StartsWith("Firewall rule matrix", StringComparison.Ordinal))
            {
                cancellation = interruption.CancelAsync();
            }
        });
        var options = new FirewallRuleExportOptions { PairsOutputPath = pairsCsv };

        // Act
        var act = () => exporter.ExportRulesAsync([auditCsv], targetMatrixCsv, options, interruption.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        await cancellation;
        (await File.ReadAllTextAsync(pairsCsv)).Should().Be(previousPairs);
    }

    [Fact]
    public async Task ExportRulesAsync_WhenTheOutputIsAnExistingFolder_ThrowsNamingItAndLeavesNoFileBesideIt()
    {
        // Arrange
        var auditCsv = await WriteNasAuditAsync();
        var output = Directory.CreateDirectory(Path.Combine(_tempDirectory, "folder-output")).FullName;

        // Act
        var act = () => new FirewallRuleExporter().ExportRulesAsync([auditCsv], output, new FirewallRuleExportOptions());

        // Assert
        await act.Should().ThrowAsync<OutputFileUnwritableException>().WithMessage($"The output '{output}' cannot be written: *");
        Directory.GetFiles(_tempDirectory, "folder-output*").Should().BeEmpty();
    }

    [Fact]
    public async Task ExportRulesAsync_WhenTheSameAuditsAreGivenInAnotherOrder_WritesTheSameMatrix()
    {
        // Arrange
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var dnsOverTcp = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,4,proxmox,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,51000,192.168.1.2,53,pihole.home.arpa,1235,systemd-resolve,/usr/lib/systemd/systemd-resolved,systemd-resolved,,systemd-resolve";
        var dnsOverUdp = "2026-10-24T07:01:00Z,2026-10-24T07:06:00Z,25,proxmox,Linux,192.168.1.20,Udp,Outbound,UDP,192.168.1.20,51000,192.168.1.2,53,pihole.home.arpa,1235,systemd-resolve,/usr/lib/systemd/systemd-resolved,systemd-resolved,,systemd-resolve";
        var monday = Path.Combine(_tempDirectory, "proxmox_monday.csv");
        var tuesday = Path.Combine(_tempDirectory, "proxmox_tuesday.csv");
        await File.WriteAllLinesAsync(monday, [header, dnsOverUdp]);
        await File.WriteAllLinesAsync(tuesday, [header, dnsOverTcp]);
        var inGivenOrder = Path.Combine(_tempDirectory, "matrix_given_order.csv");
        var inReverseOrder = Path.Combine(_tempDirectory, "matrix_reverse_order.csv");
        var exporter = new FirewallRuleExporter();

        // Act
        await exporter.ExportRulesAsync([monday, tuesday], inGivenOrder, new FirewallRuleExportOptions());
        await exporter.ExportRulesAsync([tuesday, monday], inReverseOrder, new FirewallRuleExportOptions());

        // Assert
        var givenOrderLines = await File.ReadAllLinesAsync(inGivenOrder);
        var reverseOrderLines = await File.ReadAllLinesAsync(inReverseOrder);
        givenOrderLines.Should().HaveCount(3);
        givenOrderLines[1].Should().StartWith("RULE-001,Outbound,192.168.1.20,proxmox,192.168.1.2,pihole.home.arpa,Internal,TCP,53,");
        reverseOrderLines.Should().Equal(givenOrderLines);
    }

    [Fact]
    public async Task ExportRulesAsync_WhenFlowsAreRead_LogsTheConsolidationInEnglish()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "nas.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var https = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.50,Tcp,Outbound,Established,192.168.1.50,50000,192.168.1.10,443,desktop,1234,curl,/usr/bin/curl,curl,,root";
        await File.WriteAllLinesAsync(auditCsv, [header, https, https]);
        var messages = new List<string>();
        var exporter = new FirewallRuleExporter(logger: messages.Add);

        // Act
        await exporter.ExportRulesAsync([auditCsv], Path.Combine(_tempDirectory, "firewall_matrix.csv"), new FirewallRuleExportOptions());

        // Assert
        messages.Should().Contain("Consolidating 2 raw flow(s) from 1 file(s)...");
    }

    [Fact]
    public async Task ExportRulesAsync_WhenFlowsOutputPathIsSet_WritesOneRowForAFlowSeenByBothEnds()
    {
        // Arrange
        var logs = await WriteDesktopAndNasAuditsAsync();
        var flowsCsv = Path.Combine(_tempDirectory, "flows", "firewall_flows.csv");
        var options = new FirewallRuleExportOptions { FlowsOutputPath = flowsCsv };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync(logs, Path.Combine(_tempDirectory, "firewall_matrix.csv"), options);

        // Assert
        report.TotalRulesExported.Should().Be(2);
        report.Flows.Should().Be(new ExportedMatrixFile(flowsCsv, 1));
        var lines = await File.ReadAllLinesAsync(flowsCsv);
        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("FlowId,SourceIp,SourceHost,");
        lines[1].Should().StartWith("FLOW-001,192.168.1.10,desktop,192.168.1.50,nas,Internal,TCP,443,HTTPS,curl,caddy,Both,10,8,");
    }

    [Fact]
    public async Task ExportRulesAsync_WhenHostRulesOutputPathIsSet_WritesAnObservedRuleOnEachAuditedEnd()
    {
        // Arrange
        var logs = await WriteDesktopAndNasAuditsAsync();
        var hostRulesCsv = Path.Combine(_tempDirectory, "firewall_host_rules.csv");
        var options = new FirewallRuleExportOptions { HostRulesOutputPath = hostRulesCsv };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync(logs, Path.Combine(_tempDirectory, "firewall_matrix.csv"), options);

        // Assert
        report.HostRules.Should().Be(new ExportedMatrixFile(hostRulesCsv, 2));
        var lines = await File.ReadAllLinesAsync(hostRulesCsv);
        lines.Should().HaveCount(3);
        lines[1].Should().StartWith("HOST-001,desktop,192.168.1.10,Yes,Outbound,192.168.1.50,nas,Internal,TCP,443,HTTPS,curl,Observed,10,");
        lines[2].Should().StartWith("HOST-002,nas,192.168.1.50,Yes,Inbound,192.168.1.10,desktop,Internal,TCP,443,HTTPS,caddy,Observed,8,");
    }

    // The listening sockets used to need a second run with --listeners-only, which replaced the whole matrix.
    [Fact]
    public async Task ExportRulesAsync_WhenListenersOutputPathIsSet_WritesTheListeningSocketsBesideTheFlows()
    {
        // Arrange
        var logs = await WriteDesktopAndNasAuditsAsync();
        var matrixCsv = Path.Combine(_tempDirectory, "firewall_matrix.csv");
        var listenersCsv = Path.Combine(_tempDirectory, "firewall_listeners.csv");
        var options = new FirewallRuleExportOptions { ListenersOutputPath = listenersCsv };

        // Act
        var report = await new FirewallRuleExporter().ExportRulesAsync(logs, matrixCsv, options);

        // Assert
        (await File.ReadAllLinesAsync(matrixCsv)).Should().NotContain(line => line.Contains(",Any,", StringComparison.Ordinal));
        report.Listeners.Should().Be(new ExportedMatrixFile(listenersCsv, 1));
        var lines = await File.ReadAllLinesAsync(listenersCsv);
        lines.Should().HaveCount(2);
        lines[0].Should().StartWith("RuleId,Direction,SourceIp,");
        lines[1].Should().StartWith("RULE-001,Inbound,0.0.0.0,Any,0.0.0.0,nas,Internal,TCP,443,HTTPS,caddy,");
    }

    // The same HTTPS connection seen from desktop (192.168.1.10, curl) and from nas (192.168.1.50, caddy listening on 443).
    private async Task<IReadOnlyList<string>> WriteDesktopAndNasAuditsAsync()
    {
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var desktopLog = Path.Combine(_tempDirectory, "audit_desktop.csv");
        var nasLog = Path.Combine(_tempDirectory, "audit_nas.csv");
        await File.WriteAllLinesAsync(desktopLog,
        [
            header,
            "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50000,192.168.1.50,443,nas.home.arpa,1234,curl,C:\\curl.exe,curl,,user"
        ]);
        await File.WriteAllLinesAsync(nasLog,
        [
            header,
            "2026-10-24T07:00:05Z,2026-10-24T07:05:05Z,8,nas,Linux,192.168.1.50,Tcp,Inbound,Established,192.168.1.50,443,192.168.1.10,50000,desktop.home.arpa,80,caddy,/usr/bin/caddy,caddy run,,caddy",
            "2026-10-24T07:00:00Z,2026-10-24T07:05:05Z,60,nas,Linux,192.168.1.50,Tcp,Listening,Listen,0.0.0.0,443,0.0.0.0,0,,80,caddy,/usr/bin/caddy,caddy run,,caddy"
        ]);
        return [desktopLog, nasLog];
    }

    // Two flows from desktop (192.168.1.10) to nas.home.arpa (192.168.1.50): HTTPS and rsync.
    private async Task<string> WriteNasAuditAsync()
    {
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var https = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50000,192.168.1.50,443,nas.home.arpa,1234,curl,C:\\curl.exe,curl,,user";
        var rsync = "2026-10-24T07:01:00Z,2026-10-24T07:06:00Z,25,desktop,Windows,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50002,192.168.1.50,873,nas.home.arpa,1235,rsync,C:\\rsync.exe,rsync,,user";
        await File.WriteAllLinesAsync(auditCsv, [header, https, rsync]);
        return auditCsv;
    }
}
