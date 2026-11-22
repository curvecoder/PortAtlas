using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

/// <summary>
/// The timeouts given on the command line of <c>lookup</c> and <c>resolve</c> replace the values of the configuration
/// file, as they do for <c>audit</c>.
/// </summary>
public sealed class ResolutionTimeoutOptionTests
{
    private static readonly AppConfiguration FileConfiguration = new() { DnsResolutionTimeoutMs = 2000, RdapTimeoutMs = 4000 };

    [Fact]
    public void BuildConfiguration_WhenLookupGetsBothTimeouts_ReplacesTheFileValues()
    {
        // Act
        var config = LookupCommandConfiguration.Build(FileConfiguration, "lookup", "192.0.2.1", "--dns-timeout-ms", "1500", "--rdap-timeout-ms", "6000");

        // Assert
        config.DnsResolutionTimeoutMs.Should().Be(1500);
        config.RdapTimeoutMs.Should().Be(6000);
    }

    [Fact]
    public void BuildConfiguration_WhenLookupGetsNoTimeout_KeepsTheFileValues()
    {
        // Act
        var config = LookupCommandConfiguration.Build(FileConfiguration, "lookup", "192.0.2.1");

        // Assert
        config.DnsResolutionTimeoutMs.Should().Be(2000);
        config.RdapTimeoutMs.Should().Be(4000);
    }

    [Fact]
    public void BuildConfiguration_WhenResolveGetsBothTimeouts_ReplacesTheFileValues()
    {
        // Act
        var config = ResolveCommandConfiguration.Build(FileConfiguration, "resolve", "-i", "audit.csv", "--dns-timeout-ms", "1500", "--rdap-timeout-ms", "6000");

        // Assert
        config.DnsResolutionTimeoutMs.Should().Be(1500);
        config.RdapTimeoutMs.Should().Be(6000);
    }

    [Fact]
    public void BuildConfiguration_WhenResolveGetsNoTimeout_KeepsTheFileValues()
    {
        // Act
        var config = ResolveCommandConfiguration.Build(FileConfiguration, "resolve", "-i", "audit.csv");

        // Assert
        config.DnsResolutionTimeoutMs.Should().Be(2000);
        config.RdapTimeoutMs.Should().Be(4000);
    }
}
