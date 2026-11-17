using AwesomeAssertions;
using PortAtlas.Cli.CommandLine;
using PortAtlas.Cli.Configuration;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

public sealed class AuditCommandTests
{
    [Fact]
    public void BuildConfiguration_WhenObfuscateFlagProvided_SetsDefaultFictitiousServerAndIp()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(new AppConfiguration(), "audit", "--obfuscate");

        // Assert
        updated.MaskServerName.Should().Be("HOMELAB-01");
        updated.MaskLocalIp.Should().Be("192.168.1.10");
    }

    [Fact]
    public void BuildConfiguration_WhenExplicitMaskServerAndIpProvided_SetsConfiguredValues()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(
            new AppConfiguration(), "audit", "--mask-server", "DEMO-SERVER", "--mask-ip", "172.16.0.5");

        // Assert
        updated.MaskServerName.Should().Be("DEMO-SERVER");
        updated.MaskLocalIp.Should().Be("172.16.0.5");
    }

    [Fact]
    public void BuildConfiguration_WhenSingleAliasProvided_PopulatesCustomHostAliases()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(new AppConfiguration(), "audit", "--alias", "203.0.113.20=cloud.example.test");

        // Assert
        updated.CustomHostAliases.Should().ContainKey("203.0.113.20")
            .WhoseValue.Should().Be("cloud.example.test");
    }

    [Fact]
    public void BuildConfiguration_WhenMultipleAliasesProvided_PopulatesAllAliases()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(
            new AppConfiguration(),
            "audit", "--alias", "192.168.1.10=srv-db", "--alias", "10.0.0.1=router.corp,10.0.0.2:gateway.corp");

        // Assert
        updated.CustomHostAliases.Should().HaveCount(3);
        updated.CustomHostAliases["192.168.1.10"].Should().Be("srv-db");
        updated.CustomHostAliases["10.0.0.1"].Should().Be("router.corp");
        updated.CustomHostAliases["10.0.0.2"].Should().Be("gateway.corp");
    }

    [Fact]
    public void BuildConfiguration_WhenIpv6AliasProvided_KeepsTheWholeAddress()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(new AppConfiguration(), "audit", "--alias", "fe80::10=nas.home.arpa");

        // Assert
        updated.CustomHostAliases.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("fe80::10", "nas.home.arpa"));
    }

    [Theory]
    [InlineData("nas")]
    [InlineData("=nas")]
    [InlineData("192.168.1.5=")]
    [InlineData("192.168.1.5:")]
    [InlineData("nas.home.arpa=192.168.1.5")]
    [InlineData("fe80::10:nas")]
    [InlineData("2001:db8::1")]
    [InlineData("10:nas")]
    [InlineData("10=nas")]
    public void Parse_WhenAliasIsMalformed_ReportsErrorNamingThePair(string pair)
    {
        // Act
        var parseResult = PortAtlasCommandLine.CreateRootCommand().Parse(["audit", "--alias", $"192.168.1.20=pi.home.arpa,{pair}"]);

        // Assert
        parseResult.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain($"'{pair}'");
    }

    [Fact]
    public void BuildConfiguration_WhenOutputProvided_SetsOutputCsvPath()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(
            new AppConfiguration { OutputCsvPath = "default.csv" }, "audit", "--output", "custom_audit_{ServerName}.csv");

        // Assert
        updated.OutputCsvPath.Should().Be("custom_audit_{ServerName}.csv");
    }

    [Fact]
    public void BuildConfiguration_WhenShortOutputFlagProvided_SetsOutputCsvPath()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(
            new AppConfiguration { OutputCsvPath = "default.csv" }, "audit", "-o", "short_output.csv");

        // Assert
        updated.OutputCsvPath.Should().Be("short_output.csv");
    }

    [Fact]
    public void BuildConfiguration_WhenRewriteIntervalProvided_OverridesTheFileValue()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(
            new AppConfiguration { LogRewriteIntervalMinutes = 5 }, "audit", "--rewrite-interval", "15");

        // Assert
        updated.LogRewriteIntervalMinutes.Should().Be(15);
    }

    [Fact]
    public void BuildConfiguration_WhenRewriteIntervalIsNotGiven_KeepsTheFileValue()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(new AppConfiguration { LogRewriteIntervalMinutes = 30 }, "audit");

        // Assert
        updated.LogRewriteIntervalMinutes.Should().Be(30);
    }

    [Fact]
    public void BuildConfiguration_WhenNdjsonArgumentProvided_SetsOutputNdjsonPath()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(
            new AppConfiguration { OutputNdjsonPath = string.Empty }, "audit", "--ndjson", "audit_stream_{ServerName}.ndjson");

        // Assert
        updated.OutputNdjsonPath.Should().Be("audit_stream_{ServerName}.ndjson");
    }

    [Fact]
    public void BuildConfiguration_WhenDnsTimeoutGiven_SetsResolutionTimeout()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(new AppConfiguration(), "audit", "--dns-timeout-ms", "4500");

        // Assert
        updated.DnsResolutionTimeoutMs.Should().Be(4500);
    }

    [Fact]
    public void BuildConfiguration_WhenNoTuiGiven_DisablesDashboard()
    {
        // Act
        var updated = AuditCommandConfiguration.Build(new AppConfiguration(), "audit", "--no-tui");

        // Assert
        updated.EnableDashboard.Should().BeFalse();
    }

    [Fact]
    public void BuildConfiguration_WhenNoOptionGiven_KeepsFileConfigurationValues()
    {
        // Arrange
        var fileConfiguration = new AppConfiguration
        {
            ScanIntervalSeconds = 12,
            OutputCsvPath = "from_file.csv",
            ResolveIpOwners = false,
            MapLayoutMode = "grid",
            MapIncludedPorts = ["443"]
        };

        // Act
        var updated = AuditCommandConfiguration.Build(fileConfiguration, "audit");

        // Assert
        updated.Should().BeEquivalentTo(fileConfiguration);
    }

    // The audit reads its map filters when it writes the map, long after its start: an unreadable value must stop it
    // before the first scan.
    [Theory]
    [InlineData("--include-ports", "http")]
    [InlineData("--exclude-ports", "8080-8000")]
    [InlineData("--include-ips", "nas")]
    [InlineData("--exclude-ips", "192.168.1.0/33")]
    public void BuildConfiguration_WhenFlowFilterValueIsUnreadable_ThrowsFlowFilterFormatExceptionNamingTheOptionAndTheValue(
        string option,
        string value)
    {
        // Act
        var act = () => AuditCommandConfiguration.Build(new AppConfiguration(), "audit", option, value);

        // Assert
        act.Should().Throw<FlowFilterFormatException>().WithMessage($"*{option}*'{value}'*");
    }

    [Fact]
    public void BuildConfiguration_WhenFlowFilterKeyOfTheFileIsUnreadable_ThrowsFlowFilterFormatExceptionNamingTheKey()
    {
        // Arrange
        var fileConfiguration = new AppConfiguration { MapIncludedPorts = ["443", "https"] };

        // Act
        var act = () => AuditCommandConfiguration.Build(fileConfiguration, "audit");

        // Assert
        act.Should().Throw<FlowFilterFormatException>().WithMessage("*MapIncludedPorts*'https'*");
    }
}
