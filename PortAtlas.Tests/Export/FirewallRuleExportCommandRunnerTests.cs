using AwesomeAssertions;
using PortAtlas.Cli.Export;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

[Collection(ConsoleOutputCollection.Name)]
public sealed class FirewallRuleExportCommandRunnerTests : IDisposable
{
    private readonly string _tempDirectory;

    public FirewallRuleExportCommandRunnerTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "PortAtlas_CmdExportTests_" + Guid.NewGuid().ToString("N"));
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
    public async Task RunAsync_WhenInvokedWithValidAudit_GeneratesFirewallMatrixCsv()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,SRV-APP-01,Linux,10.0.0.1,Tcp,Outbound,Established,10.0.0.1,50000,10.20.1.5,1521,db-oracle.prod,1234,tnslsnr,/usr/bin/tnslsnr,tnslsnr --run,,oracle";
        await File.WriteAllLinesAsync(auditCsv, [header, row]);

        var targetMatrix = Path.Combine(_tempDirectory, "rules.csv");
        var runner = new FirewallRuleExportCommandRunner();

        // Act
        var exitCode = await runner.RunAsync([auditCsv], targetMatrix, new FirewallRuleExportOptions());

        // Assert
        exitCode.Should().Be(0);
        File.Exists(targetMatrix).Should().BeTrue();
        var lines = await File.ReadAllLinesAsync(targetMatrix);
        lines.Should().HaveCount(2);
        lines[1].Should().StartWith("RULE-001,Outbound,10.0.0.1,SRV-APP-01,10.20.1.5,db-oracle.prod,Internal,TCP,1521,Oracle-TNS,");
    }

    [Fact]
    public async Task RunAsync_WhenFlowHostRuleAndListenerMatricesAreRequested_NamesEachFileWithItsRowCount()
    {
        // Arrange
        var auditCsv = Path.Combine(_tempDirectory, "audit.csv");
        var header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
        var row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.50,Tcp,Outbound,Established,192.168.1.50,50000,192.168.1.2,53,pihole.home.arpa,1234,systemd-resolved,/usr/lib/systemd/systemd-resolved,systemd-resolved,,systemd-resolve";
        await File.WriteAllLinesAsync(auditCsv, [header, row]);
        var options = new FirewallRuleExportOptions
        {
            FlowsOutputPath = Path.Combine(_tempDirectory, "flows.csv"),
            HostRulesOutputPath = Path.Combine(_tempDirectory, "host_rules.csv"),
            ListenersOutputPath = Path.Combine(_tempDirectory, "listeners.csv")
        };
        var consoleOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);

        // Act
        try
        {
            await new FirewallRuleExportCommandRunner().RunAsync([auditCsv], Path.Combine(_tempDirectory, "rules.csv"), options);
        }
        finally
        {
            Console.SetOut(consoleOut);
        }

        // Assert
        var output = capturedOut.ToString();
        output.Should().Contain($"Flow matrix file: {options.FlowsOutputPath}");
        output.Should().Contain($"  * Flows: 1, exported to: {options.FlowsOutputPath}");
        output.Should().Contain($"  * Host rules: 2, exported to: {options.HostRulesOutputPath}");
        output.Should().Contain($"  * Listening sockets: 0, exported to: {options.ListenersOutputPath}");
    }
}
