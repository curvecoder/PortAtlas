using AwesomeAssertions;
using PortAtlas.Cli;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// An output file that cannot be written ends the command with one line naming the file and exit code 1, as an audit
/// log that cannot be read does, instead of a stack trace.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class UnwritableOutputCliTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
    private const string Row = "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,db.home.arpa,1234,psql,/usr/bin/psql,psql,,backup";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PortAtlas_UnwritableOutput_" + Guid.NewGuid().ToString("N"));
    private readonly string _auditPath;
    private readonly string _configPath;

    // A file where a folder of the output path is expected: the folder can be neither found nor created.
    private readonly string _blocker;

    public UnwritableOutputCliTests()
    {
        Directory.CreateDirectory(_root);
        _auditPath = Path.Combine(_root, "audit_nas.csv");
        File.WriteAllLines(_auditPath, [Header, Row]);
        _configPath = Path.Combine(_root, "empty-config.json");
        File.WriteAllText(_configPath, "{}");
        _blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(_blocker, "not a folder");
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

    [Theory]
    [InlineData("map", "--html", "map.html")]
    [InlineData("map", "--drawio", "map.drawio")]
    [InlineData("export rules", "-o", "rules.csv")]
    [InlineData("export rules", "--pairs-output", "pairs.csv")]
    [InlineData("export ndjson", "-o", "audit.ndjson")]
    public async Task Main_WhenTheOutputFolderCannotBeCreated_ReturnsExitCodeOneWithOneLineNamingTheOutput(string command, string outputOption, string fileName)
    {
        // Arrange
        var output = Path.Combine(_blocker, "sub", fileName);
        string[] arguments = [.. command.Split(' '), "-i", _auditPath, outputOption, output, "--config", _configPath];
        if (outputOption == "--pairs-output")
        {
            arguments = [.. arguments, "-o", Path.Combine(_root, "rules.csv")];
        }

        // Act
        var (exitCode, error) = await RunAsync(arguments);

        // Assert
        exitCode.Should().Be(1);
        error.Trim().Should().StartWith($"The output '{output}' cannot be written: ").And.NotContain(Environment.NewLine);
    }

    [Theory]
    [InlineData("export ndjson", "-o")]
    [InlineData("export rules", "-o")]
    [InlineData("map", "--html")]
    public async Task Main_WhenTheOutputIsAnExistingFolder_ReturnsExitCodeOneWithOneLineNamingTheOutput(string command, string outputOption)
    {
        // Arrange
        var output = Directory.CreateDirectory(Path.Combine(_root, "folder-output")).FullName;
        string[] arguments = [.. command.Split(' '), "-i", _auditPath, outputOption, output, "--config", _configPath];

        // Act
        var (exitCode, error) = await RunAsync(arguments);

        // Assert
        exitCode.Should().Be(1);
        error.Trim().Should().StartWith($"The output '{output}' cannot be written: ").And.NotContain(Environment.NewLine);
    }

    [Fact]
    public async Task Main_WhenConfigInitCannotCreateTheFolder_ReturnsExitCodeOneWithOneLineNamingTheFile()
    {
        // Arrange
        var output = Path.Combine(_blocker, "sub", "portatlas.json");

        // Act
        var (exitCode, error) = await RunAsync(["config", "init", output]);

        // Assert
        exitCode.Should().Be(1);
        error.Trim().Should().StartWith($"The output '{output}' cannot be written: ").And.NotContain(Environment.NewLine);
    }

    private static async Task<(int ExitCode, string Error)> RunAsync(string[] arguments)
    {
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
