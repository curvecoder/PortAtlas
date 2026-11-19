using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using PortAtlas.Cli.Diagnostics;
using Xunit;

namespace PortAtlas.Tests.Diagnostics;

public sealed class IpResolutionDiagnosticReporterTests
{
    [Fact]
    public Task RunDiagnosticAsync_WhenTargetIpStringIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var reporter = new IpResolutionDiagnosticReporter(TextWriter.Null);
        var config = new AppConfiguration();

        // Act
        var act = () => reporter.RunDiagnosticAsync(null!, config);

        // Assert
        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public Task RunDiagnosticAsync_WhenConfigIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var reporter = new IpResolutionDiagnosticReporter(TextWriter.Null);

        // Act
        var act = () => reporter.RunDiagnosticAsync("127.0.0.1", null!);

        // Assert
        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenInvalidIpString_OutputsErrorMessageWithoutThrowing()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);

        // Act
        await reporter.RunDiagnosticAsync("not-an-ip", new AppConfiguration());

        // Assert
        output.ToString().Should().Contain("Invalid IP address format");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenLoopbackIp_ExecutesAllStagesAndIdentifiesLocalSource()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);

        // Act
        await reporter.RunDiagnosticAsync("127.0.0.1", new AppConfiguration());

        // Assert
        var report = output.ToString();
        report.Should().Contain("[1/6] Syntax Analysis & Classification");
        report.Should().Contain("Loopback / Local       : YES");
        report.Should().Contain("Synthesized Source     : Local");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenDnsTimeoutConfigured_ProbesWithTheAuditTimeout()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);

        // Act
        await reporter.RunDiagnosticAsync("127.0.0.1", new AppConfiguration { DnsResolutionTimeoutMs = 4500 });

        // Assert
        output.ToString().Should().Contain("Configured timeout     : 4500 ms");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenRdapTimeoutConfigured_ProbesWithTheAuditRdapTimeout()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);
        var config = new AppConfiguration { RdapTimeoutMs = 4500, DnsResolutionTimeoutMs = 500 };
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        // Act
        // A public address reaches the RDAP step; the cancelled token stops each query before it is sent.
        await reporter.RunDiagnosticAsync("8.8.8.8", config, cancelled.Token);

        // Assert
        output.ToString().Should().Contain("Configured timeout     : 4500 ms (RdapTimeoutMs)");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenTheAddressHasAnAlias_SendsNoQueryAndSynthesizesTheAliasLikeTheAudit()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);
        var config = new AppConfiguration
        {
            CustomHostAliases = new() { ["8.8.8.8"] = "resolver.home.arpa" },
            DnsResolutionTimeoutMs = 500
        };

        // Act
        // A public address, so that the RDAP step is skipped by the alias and not by the private range.
        await reporter.RunDiagnosticAsync("8.8.8.8", config);

        // Assert
        var report = output.ToString();
        report.Should().Contain("Alias in config        : resolver.home.arpa (CustomHostAliases)");
        report.Split("SKIPPED (CustomHostAliases names the address; the audit sends no query)").Should().HaveCount(3, "the reverse DNS and RDAP steps are both skipped");
        report.Should().NotContain("HTTP request target");
        report.Should().Contain("Synthesized HostName   : resolver.home.arpa");
        report.Should().Contain("Synthesized Source     : Custom");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenTheAddressIsInDnsExcludedNetworks_SendsNoQueryAndSynthesizesAnExclusion()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);
        var config = new AppConfiguration { DnsExcludedNetworks = ["8.8.8.0/24"], DnsResolutionTimeoutMs = 500 };

        // Act
        await reporter.RunDiagnosticAsync("8.8.8.8", config);

        // Assert
        var report = output.ToString();
        report.Should().Contain("Excluded from DNS      : YES (DnsExcludedNetworks)");
        report.Split("SKIPPED (DnsExcludedNetworks; the audit sends no query)").Should().HaveCount(3, "the reverse DNS and RDAP steps are both skipped");
        report.Should().NotContain("HTTP request target");
        report.Should().Contain("Synthesized Source     : Excluded");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenTheAddressIsOnlyInExcludedRemoteIps_ReportsItsFlowsAsDroppedWithoutExcludingItFromDns()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);
        var config = new AppConfiguration { ExcludedRemoteIps = ["127.0.0.1"], DnsResolutionTimeoutMs = 500 };

        // Act
        await reporter.RunDiagnosticAsync("127.0.0.1", config);

        // Assert
        var report = output.ToString();
        report.Should().Contain("Excluded from DNS      : NO");
        report.Should().Contain("Flows dropped by audit : YES (ExcludedRemoteIps)");
        report.Should().NotContain("--exclude-remote-ips");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenUnspecifiedIp_SkipsDnsCallWithDefensiveExclusion()
    {
        // Arrange
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);

        // Act
        await reporter.RunDiagnosticAsync("0.0.0.0", new AppConfiguration());

        // Assert
        var report = output.ToString();
        report.Should().Contain("Unspecified / Any      : YES");
        report.Should().Contain("SKIPPED (Defensive guard)");
        report.Should().Contain("Synthesized Source     : Excluded");
    }

    [Fact]
    public async Task RunDiagnosticAsync_WhenTheCacheCannotBeRead_ReportsTheCacheErrorNamingTheFile()
    {
        // Arrange
        var cachePath = Path.Combine(Path.GetTempPath(), $"PortAtlas_LookupCache_{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(cachePath, """{ "192.168.1.10": { "HostName": """);
        using var output = new StringWriter();
        var reporter = new IpResolutionDiagnosticReporter(output);
        try
        {
            // Act
            await reporter.RunDiagnosticAsync("127.0.0.1", new AppConfiguration { DnsCacheFilePath = cachePath });

            // Assert
            output.ToString().Should().Contain($"Cache access error     : The DNS cache '{cachePath}' cannot be read");
        }
        finally
        {
            File.Delete(cachePath);
        }
    }
}
