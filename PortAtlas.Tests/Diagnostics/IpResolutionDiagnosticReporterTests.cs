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
