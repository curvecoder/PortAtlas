using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Cli;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// The resolve command reads the name resolution keys of the configuration file with the meaning they have for the
/// audit, so that a server whose file turns the lookups off sends no query from resolve either.
/// </summary>
[Collection(ConsoleOutputCollection.Name)]
public sealed class ResolveConfigurationCliTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "PortAtlas_ResolveConfig_" + Guid.NewGuid().ToString("N"));

    public ResolveConfigurationCliTests() => Directory.CreateDirectory(_root);

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
    public async Task Resolve_WhenConfigurationTurnsLookupsOffNamesAnAliasAndExcludesANetwork_ResolvesNoAddressAndWritesTheAlias()
    {
        // Arrange
        var auditPath = Path.Combine(_root, "audit_nas.csv");
        await File.WriteAllLinesAsync(auditPath, [Header, Row("192.168.1.30"), Row("192.168.1.40")]);
        var configPath = Path.Combine(_root, "portatlas.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new
        {
            ResolveRemoteHostNames = false,
            ResolveIpOwners = false,
            CustomHostAliases = new Dictionary<string, string> { ["192.168.1.30"] = "db.home.arpa" },
            DnsExcludedNetworks = new[] { "192.168.1.40/32" },
            DnsCacheFilePath = Path.Combine(_root, "dns_cache.json")
        }));
        var masterCache = Path.Combine(_root, "master.json");

        // Act
        var (exitCode, output) = await RunAsync("resolve", "-i", auditPath, "--output-cache", masterCache, "--config", configPath);

        // Assert
        exitCode.Should().Be(0);
        output.Should().Contain("Execution mode: Active enrichment, reverse DNS off (ResolveRemoteHostNames), RDAP off (ResolveIpOwners)");
        output.Should().NotContain("unknown IP address(es) online", "the aliased and the excluded address are never sent to a resolver");
        var lines = await File.ReadAllLinesAsync(Path.Combine(_root, "audit_nas_enriched.csv"));
        lines[1].Should().Contain(",192.168.1.30,5432,db.home.arpa,");
        lines[2].Should().Contain(",192.168.1.40,5432,,");
        new PersistentDnsCacheStore(masterCache).Load().Should().BeEmpty();
    }

    private static string Row(string remoteIp) =>
        $"2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,{remoteIp},5432,,1234,psql,/usr/bin/psql,psql -h db,,backup";

    private static async Task<(int ExitCode, string Output)> RunAsync(params string[] args)
    {
        var consoleOut = Console.Out;
        using var capturedOut = new StringWriter();
        Console.SetOut(capturedOut);
        try
        {
            var exitCode = await Program.Main(args);
            return (exitCode, capturedOut.ToString());
        }
        finally
        {
            Console.SetOut(consoleOut);
        }
    }
}
