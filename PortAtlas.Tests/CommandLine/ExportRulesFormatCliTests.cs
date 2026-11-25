using AwesomeAssertions;
using PortAtlas.Cli;
using PortAtlas.Cli.CommandLine;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// <c>export rules --format</c> chooses the file format of every matrix written by the run.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class ExportRulesFormatCliTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
    private const string HttpsRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,Linux,192.168.1.10,Tcp,Outbound,Established,192.168.1.10,50000,192.168.1.50,443,nas.home.arpa,1234,curl,/usr/bin/curl,curl,,user";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PortAtlas_RulesFormat_" + Guid.NewGuid().ToString("N"));
    private readonly string _auditPath;
    private readonly string _configPath;

    public ExportRulesFormatCliTests()
    {
        Directory.CreateDirectory(_root);
        _auditPath = Path.Combine(_root, "audit_desktop.csv");
        File.WriteAllLines(_auditPath, [Header, HttpsRow]);
        _configPath = Path.Combine(_root, "portatlas.json");
        File.WriteAllText(_configPath, "{}");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by an antivirus scan only leaves a folder in the temp directory; the test result stands.
        }
    }

    [Fact]
    public async Task ExportRules_WhenFormatIsMarkdown_WritesTheMatrixAndTheHostRulesAsMarkdown()
    {
        // Arrange
        var rulesPath = Path.Combine(_root, "rules.md");
        var hostRulesPath = Path.Combine(_root, "host_rules.md");

        // Act
        var (exitCode, error) = await RunAsync(["-o", rulesPath, "--host-rules-output", hostRulesPath, "--format", "markdown"]);

        // Assert
        exitCode.Should().Be(0, error);
        (await File.ReadAllLinesAsync(rulesPath))[0].Should().Be($"# {FirewallMatrixDescriptions.Rules.Title}");
        (await File.ReadAllLinesAsync(hostRulesPath))[0].Should().Be($"# {FirewallMatrixDescriptions.HostRules.Title}");
    }

    [Fact]
    public async Task ExportRules_WhenFormatIsNotGiven_WritesTheMatrixAsCsv()
    {
        // Arrange
        var rulesPath = Path.Combine(_root, "rules.csv");

        // Act
        var (exitCode, error) = await RunAsync(["-o", rulesPath]);

        // Assert
        exitCode.Should().Be(0, error);
        (await File.ReadAllLinesAsync(rulesPath))[0].Should().StartWith("RuleId,Direction,");
    }

    [Fact]
    public void Parse_WhenFormatIsUnknown_ReportsTheAcceptedValues()
    {
        // Act
        var parseResult = PortAtlasCommandLine.CreateRootCommand().Parse(["export", "rules", "--format", "html"]);

        // Assert
        parseResult.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain("'html'").And.Contain("csv, markdown");
    }

    private async Task<(int ExitCode, string Error)> RunAsync(string[] extraArguments)
    {
        string[] arguments = ["export", "rules", "-i", _auditPath, .. extraArguments, "--config", _configPath];
        var consoleOut = Console.Out;
        var consoleError = Console.Error;
        using var capturedOut = new StringWriter();
        using var capturedError = new StringWriter();
        Console.SetOut(capturedOut);
        Console.SetError(capturedError);
        try
        {
            var exitCode = await Program.Main(arguments);
            return (exitCode, capturedError.ToString());
        }
        finally
        {
            Console.SetOut(consoleOut);
            Console.SetError(consoleError);
        }
    }
}
