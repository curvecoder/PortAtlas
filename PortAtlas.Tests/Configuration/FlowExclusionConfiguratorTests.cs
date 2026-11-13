using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using PortAtlas.Core.Filtering;
using ProcessForge.Filtering;
using ProcessForge.Models;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Configuration;

public sealed class FlowExclusionConfiguratorTests : IDisposable
{
    private readonly string _tempConfigFile;

    public FlowExclusionConfiguratorTests()
    {
        _tempConfigFile = Path.Combine(Path.GetTempPath(), $"portatlas_test_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_tempConfigFile);
        }
        catch (IOException)
        {
            // A file still held by the OS must not fail the test run; it lives in the temp directory.
        }
    }

    [Fact]
    public void ExcludeLocalPort_WhenInvoked_UpdatesMemoryFilterAndPersistsConfig()
    {
        // Arrange
        var initialConfig = new AppConfiguration();
        var networkFlowFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions());
        var processFilter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var configurator = new FlowExclusionConfigurator(initialConfig, _tempConfigFile, networkFlowFilter, processFilter);

        var conn = new ActiveSocketConnection(
            SocketTransportProtocol.Tcp,
            SocketFlowDirection.Listening,
            new SocketEndpoint(IPAddress.Any, 8088),
            new SocketEndpoint(IPAddress.Any, 0),
            "Listen",
            new ProcessMetadata(10, "web", "/usr/bin/web", "", "", "user"));

        // Act
        var resultMessage = configurator.ExcludeLocalPort(8088);

        // Assert
        resultMessage.Should().Contain("8088");
        networkFlowFilter.ShouldExclude(conn, new HashSet<IPAddress>()).Should().BeTrue();
        configurator.CurrentConfiguration.ExcludedLocalPorts.Should().Contain(8088);

        File.Exists(_tempConfigFile).Should().BeTrue();
        var persistedJson = File.ReadAllText(_tempConfigFile);
        var loaded = JsonSerializer.Deserialize<AppConfiguration>(persistedJson);
        loaded.Should().NotBeNull();
        loaded!.ExcludedLocalPorts.Should().Contain(8088);
    }

    [Fact]
    public void ExcludeRemoteIp_WhenInvoked_UpdatesMemoryFilterAndPersistsConfig()
    {
        // Arrange
        var initialConfig = new AppConfiguration();
        var networkFlowFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions());
        var processFilter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var configurator = new FlowExclusionConfigurator(initialConfig, _tempConfigFile, networkFlowFilter, processFilter);

        var conn = new ActiveSocketConnection(
            SocketTransportProtocol.Tcp,
            SocketFlowDirection.Outbound,
            new SocketEndpoint(IPAddress.Parse("192.168.1.10"), 50000),
            new SocketEndpoint(IPAddress.Parse("52.123.242.181"), 443),
            "Established",
            new ProcessMetadata(10, "curl", "/usr/bin/curl", "", "", "user"));

        // Act
        var resultMessage = configurator.ExcludeRemoteIp("52.123.242.181");

        // Assert
        resultMessage.Should().Contain("52.123.242.181");
        networkFlowFilter.ShouldExclude(conn, new HashSet<IPAddress>()).Should().BeTrue();
        configurator.CurrentConfiguration.ExcludedRemoteIps.Should().Contain("52.123.242.181");

        File.Exists(_tempConfigFile).Should().BeTrue();
        var persistedJson = File.ReadAllText(_tempConfigFile);
        var loaded = JsonSerializer.Deserialize<AppConfiguration>(persistedJson);
        loaded.Should().NotBeNull();
        loaded!.ExcludedRemoteIps.Should().Contain("52.123.242.181");
    }

    [Fact]
    public void ExcludeRemotePort_WhenInvoked_UpdatesMemoryFilterAndPersistsConfig()
    {
        // Arrange
        var initialConfig = new AppConfiguration();
        var networkFlowFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions());
        var processFilter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var configurator = new FlowExclusionConfigurator(initialConfig, _tempConfigFile, networkFlowFilter, processFilter);

        var conn = new ActiveSocketConnection(
            SocketTransportProtocol.Tcp,
            SocketFlowDirection.Outbound,
            new SocketEndpoint(IPAddress.Parse("192.168.1.10"), 50000),
            new SocketEndpoint(IPAddress.Parse("8.8.4.4"), 853),
            "Established",
            new ProcessMetadata(10, "curl", "/usr/bin/curl", "", "", "user"));

        // Act
        var resultMessage = configurator.ExcludeRemotePort(853);

        // Assert
        resultMessage.Should().Contain("853");
        networkFlowFilter.ShouldExclude(conn, new HashSet<IPAddress>()).Should().BeTrue();
        configurator.CurrentConfiguration.ExcludedRemotePorts.Should().Contain(853);

        File.Exists(_tempConfigFile).Should().BeTrue();
        var persistedJson = File.ReadAllText(_tempConfigFile);
        var loaded = JsonSerializer.Deserialize<AppConfiguration>(persistedJson);
        loaded.Should().NotBeNull();
        loaded!.ExcludedRemotePorts.Should().Contain(853);
    }

    [Fact]
    public void ExcludeProcess_WhenInvoked_UpdatesProcessFilterAndPersistsConfig()
    {
        // Arrange
        var initialConfig = new AppConfiguration();
        var networkFlowFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions());
        var processFilter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var configurator = new FlowExclusionConfigurator(initialConfig, _tempConfigFile, networkFlowFilter, processFilter);

        var proc = new ProcessMetadata(250, "telemetry-agent", "/opt/agent", "", "", "user");

        // Act
        var resultMessage = configurator.ExcludeProcess("telemetry-agent");

        // Assert
        resultMessage.Should().Contain("telemetry-agent");
        processFilter.ShouldExclude(proc).Should().BeTrue();
        configurator.CurrentConfiguration.ExcludedExactNames.Should().Contain("telemetry-agent");

        File.Exists(_tempConfigFile).Should().BeTrue();
        var persistedJson = File.ReadAllText(_tempConfigFile);
        var loaded = JsonSerializer.Deserialize<AppConfiguration>(persistedJson);
        loaded.Should().NotBeNull();
        loaded!.ExcludedExactNames.Should().Contain("telemetry-agent");
    }

    [Fact]
    public void ExcludeTarget_WhenTargetContainsFullPath_ExtractsFileNameAndPersistsConfig()
    {
        // Arrange
        var initialConfig = new AppConfiguration();
        var networkFlowFilter = new NetworkFlowFilter(new NetworkFlowFilterOptions());
        var processFilter = new ProcessExclusionFilter(new ProcessExclusionOptions());
        var configurator = new FlowExclusionConfigurator(initialConfig, _tempConfigFile, networkFlowFilter, processFilter);

        var proc = new ProcessMetadata(250, "custom-daemon.exe", "C:\\Services\\custom-daemon.exe", "", "", "user");

        // Act
        var resultMessage = configurator.ExcludeTarget("\"C:\\Services\\custom-daemon.exe\"");

        // Assert
        resultMessage.Should().Contain("custom-daemon.exe");
        processFilter.ShouldExclude(proc).Should().BeTrue();
        configurator.CurrentConfiguration.ExcludedExactNames.Should().Contain("custom-daemon.exe");

        File.Exists(_tempConfigFile).Should().BeTrue();
        var persistedJson = File.ReadAllText(_tempConfigFile);
        var loaded = JsonSerializer.Deserialize<AppConfiguration>(persistedJson);
        loaded.Should().NotBeNull();
        loaded!.ExcludedExactNames.Should().Contain("custom-daemon.exe");
    }

    [Fact]
    public void ExcludeLocalPort_WhenFileContainsDateToken_KeepsTheTokenInTheFile()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "OutputCsvPath": "audit_{Date}.csv" }""");
        var configurator = CreateConfigurator(new AppConfiguration { OutputCsvPath = "audit_2026-11-13.csv" });

        // Act
        configurator.ExcludeLocalPort(8088);

        // Assert
        ReadConfigurationFile().GetProperty("OutputCsvPath").GetString().Should().Be("audit_{Date}.csv");
    }

    [Fact]
    public void ExcludeRemoteIp_WhenRunUsesCommandLineOverrides_DoesNotWriteThemToTheFile()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9 }""");
        var effectiveConfiguration = new AppConfiguration
        {
            ScanIntervalSeconds = 9,
            ResolveRemoteHostNames = false,
            MaskServerName = "HOMELAB-01",
            MaskLocalIp = "192.168.1.10",
            ExcludedRemoteIps = ["192.168.1.50"]
        };
        var configurator = CreateConfigurator(effectiveConfiguration);

        // Act
        configurator.ExcludeRemoteIp("52.123.242.181");

        // Assert
        var document = ReadConfigurationFile();
        document.EnumerateObject().Select(property => property.Name).Should().Equal("ScanIntervalSeconds", "ExcludedRemoteIps");
        document.GetProperty("ExcludedRemoteIps").EnumerateArray().Select(ip => ip.GetString()).Should().Equal("52.123.242.181");
    }

    [Fact]
    public void ExcludeLocalPort_WhenFileHasUnknownKeys_KeepsThem()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "Comment": "NAS audit", "ScanIntervalSeconds": 9 }""");
        var configurator = CreateConfigurator(new AppConfiguration { ScanIntervalSeconds = 9 });

        // Act
        configurator.ExcludeLocalPort(8088);

        // Assert
        ReadConfigurationFile().GetProperty("Comment").GetString().Should().Be("NAS audit");
    }

    [Fact]
    public void ExcludeLocalPort_WhenFileHoldsNonAsciiText_WritesItUnescaped()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "MaskServerName": "Média-NAS" }""");
        var configurator = CreateConfigurator(new AppConfiguration { MaskServerName = "Média-NAS" });

        // Act
        configurator.ExcludeLocalPort(8088);

        // Assert
        File.ReadAllText(_tempConfigFile).Should().Contain("Média-NAS");
    }

    [Theory]
    [InlineData("""{ "ScanIntervalSeconds": 9,""")]
    [InlineData("""{ "ScanIntervalSeconds": 9, "ScanIntervalSeconds": 5 }""")]
    [InlineData("""{ "ExcludedLocalPorts": "none" }""")]
    [InlineData("""{ "ExcludedLocalPorts": null }""")]
    [InlineData("""{ "ExcludedLocalPorts": [ "http" ] }""")]
    [InlineData("""[ 8088 ]""")]
    public void ExcludeLocalPort_WhenFileIsNoLongerAValidConfiguration_LeavesItUntouched(string editedByHand)
    {
        // Arrange
        var configurator = CreateConfigurator(new AppConfiguration());
        File.WriteAllText(_tempConfigFile, editedByHand);

        // Act
        configurator.ExcludeLocalPort(8088);

        // Assert
        File.ReadAllText(_tempConfigFile).Should().Be(editedByHand);
    }

    [Fact]
    public void ExcludeProcess_WhenFileHasNoExclusionList_KeepsTheBuiltInNames()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9 }""");
        var configurator = CreateConfigurator(new AppConfiguration { ScanIntervalSeconds = 9 });

        // Act
        configurator.ExcludeProcess("telemetry-agent");

        // Assert
        ReadConfigurationFile().GetProperty("ExcludedExactNames").EnumerateArray().Select(name => name.GetString())
            .Should().Equal("System", "Idle", "telemetry-agent");
    }

    [Fact]
    public void ExcludeProcess_WhenFileEmptiedTheExclusionList_DoesNotBringTheBuiltInNamesBack()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ExcludedExactNames": [] }""");
        var configurator = CreateConfigurator(new AppConfiguration { ExcludedExactNames = [] });

        // Act
        configurator.ExcludeProcess("telemetry-agent");

        // Assert
        ReadConfigurationFile().GetProperty("ExcludedExactNames").EnumerateArray().Select(name => name.GetString())
            .Should().Equal("telemetry-agent");
    }

    [Fact]
    public void ExcludeLocalPort_WhenFileAlreadyListsThePort_DoesNotDuplicateIt()
    {
        // Arrange
        var configurator = CreateConfigurator(new AppConfiguration());
        File.WriteAllText(_tempConfigFile, """{ "ExcludedLocalPorts": [ 8088 ] }""");

        // Act
        configurator.ExcludeLocalPort(8088);

        // Assert
        ReadExcludedLocalPorts().Should().Equal(8088);
    }

    [Fact]
    public void ExcludeLocalPort_WhenThePreviousSaveFailed_WritesBothPorts()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9 }""");
        var configurator = CreateConfigurator(new AppConfiguration { ScanIntervalSeconds = 9 });
        using (new FileStream(_tempConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            configurator.ExcludeLocalPort(8088);
        }

        // Act
        configurator.ExcludeLocalPort(9090);

        // Assert
        ReadExcludedLocalPorts().Should().Equal(8088, 9090);
    }

    [Fact]
    public void ExcludeLocalPort_WhenSaved_DoesNotMentionTheSave()
    {
        // Arrange
        var configurator = CreateConfigurator(new AppConfiguration());

        // Act
        var message = configurator.ExcludeLocalPort(8088);

        // Assert
        message.Should().Be("Excluded local port 8088");
        ReadExcludedLocalPorts().Should().Contain(8088);
    }

    [Fact]
    public void ExcludeLocalPort_WhenFileIsLockedByAnotherProcess_SaysTheExclusionWasNotSaved()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9 }""");
        var configurator = CreateConfigurator(new AppConfiguration { ScanIntervalSeconds = 9 });
        using var exclusiveHandle = new FileStream(_tempConfigFile, FileMode.Open, FileAccess.Read, FileShare.None);

        // Act
        var message = configurator.ExcludeLocalPort(8088);

        // Assert
        message.Should().StartWith("Excluded local port 8088").And.Contain("not saved").And.Contain(Path.GetFileName(_tempConfigFile));
    }

    [Fact]
    public void ExcludeRemoteIp_WhenFileIsNoLongerAValidConfiguration_SaysTheExclusionWasNotSaved()
    {
        // Arrange
        var configurator = CreateConfigurator(new AppConfiguration());
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9,""");

        // Act
        var message = configurator.ExcludeRemoteIp("192.0.2.7");

        // Assert
        message.Should().StartWith("Excluded remote IP 192.0.2.7").And.Contain("not saved").And.Contain("valid configuration");
    }

    [Fact]
    public void ExcludeProcess_WhenAnEarlierExclusionIsStillUnsaved_SaysSoEvenWithoutANewSave()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9 }""");
        var configurator = CreateConfigurator(new AppConfiguration { ScanIntervalSeconds = 9 });
        using var exclusiveHandle = new FileStream(_tempConfigFile, FileMode.Open, FileAccess.Read, FileShare.None);
        configurator.ExcludeProcess("telemetry-agent");

        // Act
        var message = configurator.ExcludeProcess("telemetry-agent");

        // Assert
        message.Should().Contain("not saved");
    }

    [Fact]
    public void ExcludeLocalPort_WhenTheSaveSucceedsAfterAFailure_NoLongerSaysNotSaved()
    {
        // Arrange
        File.WriteAllText(_tempConfigFile, """{ "ScanIntervalSeconds": 9 }""");
        var configurator = CreateConfigurator(new AppConfiguration { ScanIntervalSeconds = 9 });
        using (new FileStream(_tempConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            configurator.ExcludeLocalPort(8088);
        }

        // Act
        var message = configurator.ExcludeLocalPort(9090);

        // Assert
        message.Should().Be("Excluded local port 9090");
    }

    private FlowExclusionConfigurator CreateConfigurator(AppConfiguration effectiveConfiguration) =>
        new(
            effectiveConfiguration,
            _tempConfigFile,
            new NetworkFlowFilter(new NetworkFlowFilterOptions()),
            new ProcessExclusionFilter(new ProcessExclusionOptions()));

    private JsonElement ReadConfigurationFile() => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(_tempConfigFile));

    private IEnumerable<int> ReadExcludedLocalPorts() =>
        ReadConfigurationFile().GetProperty("ExcludedLocalPorts").EnumerateArray().Select(port => port.GetInt32());
}
