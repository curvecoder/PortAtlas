using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Cli;
using PortAtlas.Cli.CommandLine;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// <c>export rules --group-rules-output</c> merges the rules of the addresses of each address group, given by the
/// <c>AddressGroups</c> key and by <c>--address-group</c>.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class ExportRulesAddressGroupCliTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
    private const string FirstPcRow = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,pc-21,Linux,192.168.10.21,Tcp,Outbound,Established,192.168.10.21,50000,192.168.1.50,443,nas.home.arpa,1234,curl,/usr/bin/curl,curl,,lab";
    private const string SecondPcRow = "2026-10-24T07:00:00Z,2026-10-24T07:06:00Z,4,pc-22,Linux,192.168.10.22,Tcp,Outbound,Established,192.168.10.22,50001,192.168.1.51,443,git.home.arpa,2345,git,/usr/bin/git,git fetch,,lab";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PortAtlas_AddressGroups_" + Guid.NewGuid().ToString("N"));
    private readonly string _auditPath;
    private readonly string _rulesPath;
    private readonly string _groupRulesPath;

    public ExportRulesAddressGroupCliTests()
    {
        Directory.CreateDirectory(_root);
        _auditPath = Path.Combine(_root, "audit_lab.csv");
        File.WriteAllLines(_auditPath, [Header, FirstPcRow, SecondPcRow]);
        _rulesPath = Path.Combine(_root, "rules.csv");
        _groupRulesPath = Path.Combine(_root, "group_rules.csv");
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
    public async Task ExportRules_WhenGroupsComeFromTheFileAndTheOption_WritesOneRowPerGroupPair()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { AddressGroups = new Dictionary<string, string[]> { ["lab"] = ["192.168.10.0/24"] } });

        // Act
        var (exitCode, error) = await RunAsync(config, ["--address-group", "servers=192.168.1.0/24"]);

        // Assert
        exitCode.Should().Be(0, error);
        var lines = await File.ReadAllLinesAsync(_groupRulesPath);
        lines.Should().HaveCount(2);
        lines[1].Should().StartWith("GROUP-001,lab,servers,TCP,443,2,14,");
        lines[1].Should().EndWith(",\"curl, git\"");
        (await File.ReadAllLinesAsync(_rulesPath)).Should().HaveCount(3, "the detailed matrix keeps one rule per address");
    }

    [Fact]
    public async Task ExportRules_WhenTwoGroupsHoldTheSameRange_StopsWithExitCodeOneBeforeWritingAnything()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { AddressGroups = new Dictionary<string, string[]> { ["lab"] = ["192.168.10.0/24"] } });

        // Act
        var (exitCode, error) = await RunAsync(config, ["--address-group", "vlan10=192.168.10.0/24"]);

        // Assert
        exitCode.Should().Be(1);
        error.Should().Contain("192.168.10.0/24").And.Contain("'lab'").And.Contain("'vlan10'");
        File.Exists(_rulesPath).Should().BeFalse();
        File.Exists(_groupRulesPath).Should().BeFalse();
    }

    [Fact]
    public async Task ExportRules_WhenTheFileHoldsAnUnreadableRange_StopsWithExitCodeOneNamingTheKeyAndTheValue()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { AddressGroups = new Dictionary<string, string[]> { ["lab"] = ["192.168.10.0/33"] } });

        // Act
        var (exitCode, error) = await RunAsync(config, []);

        // Assert
        exitCode.Should().Be(1);
        error.Should().Contain("AddressGroups").And.Contain("'192.168.10.0/33'").And.Contain("'lab'");
        File.Exists(_rulesPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("lab")]
    [InlineData("=192.168.10.0/24")]
    [InlineData("lab=")]
    [InlineData("lab=192.168.10")]
    [InlineData("lab=192.168.10.0/33")]
    public void Parse_WhenAnAddressGroupIsMalformed_ReportsErrorNamingTheValue(string value)
    {
        // Act
        var parseResult = PortAtlasCommandLine.CreateRootCommand().Parse(["export", "rules", "--address-group", value]);

        // Assert
        parseResult.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain($"'{value}'");
    }

    [Fact]
    public async Task ExportRules_WhenTheGroupMatrixNamesTheDetailedMatrix_StopsWithExitCodeOne()
    {
        // Arrange
        var config = await WriteConfigurationAsync(new { });

        // Act
        var (exitCode, error) = await RunAsync(config, [], groupRulesPath: _rulesPath);

        // Assert
        exitCode.Should().Be(1);
        error.Should().Contain("--group-rules-output");
    }

    private async Task<string> WriteConfigurationAsync(object values)
    {
        var path = Path.Combine(_root, "portatlas.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(values));
        return path;
    }

    private async Task<(int ExitCode, string Error)> RunAsync(string configPath, string[] extraArguments, string? groupRulesPath = null)
    {
        string[] arguments =
        [
            "export", "rules", "-i", _auditPath, "-o", _rulesPath, "--group-rules-output", groupRulesPath ?? _groupRulesPath,
            .. extraArguments, "--config", configPath
        ];
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
