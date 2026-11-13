using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditCsvDelimiterDetectorTests
{
    [Theory]
    [InlineData("FirstSeenUtc;LastSeenUtc;ServerName;LocalPort", ',', ';')]
    [InlineData("FirstSeenUtc,LastSeenUtc,ServerName,LocalPort", ';', ',')]
    [InlineData("FirstSeenUtc\tLastSeenUtc\tServerName\tLocalPort", ',', '\t')]
    [InlineData("FirstSeenUtc|LastSeenUtc|ServerName|LocalPort", ',', '|')]
    [InlineData("\"FirstSeenUtc\";\"LastSeenUtc\";\"ServerName\"", ',', ';')]
    [InlineData("firstseenutc ; lastseenutc ; servername", ',', ';')]
    public void Detect_WhenHeaderNamesAuditColumns_ReturnsTheSeparatorBetweenThem(string header, char configured, char expected)
    {
        // Act
        var detected = AuditCsvDelimiterDetector.Detect(header, configured);

        // Assert
        detected.Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-10-24T07:00:00Z;2026-10-24T07:05:00Z;10;desktop;curl -H a,b", ';')]
    [InlineData("2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,a;b", ',')]
    [InlineData("", ';')]
    [InlineData("   ", '#')]
    [InlineData(null, ';')]
    public void Detect_WhenLineNamesNoAuditColumn_ReturnsTheConfiguredSeparator(string? firstLine, char configured)
    {
        // Act
        var detected = AuditCsvDelimiterDetector.Detect(firstLine, configured);

        // Assert
        detected.Should().Be(configured);
    }

    [Theory]
    [InlineData("2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,desktop,\"curl -H a;b\"", ';', ',')]
    [InlineData("2026-10-24T07:00:00Z;2026-10-24T07:05:00Z;10;desktop;curl -H a,b", ',', ';')]
    [InlineData("2026-10-24T07:00:00Z\t2026-10-24T07:05:00Z\t10\tdesktop", ';', '\t')]
    public void Detect_WhenLineNamesNoAuditColumnAndAnotherSeparatorSplitsItFurther_ReturnsThatSeparator(string firstLine, char configured, char expected)
    {
        // Act
        var detected = AuditCsvDelimiterDetector.Detect(firstLine, configured);

        // Assert
        detected.Should().Be(expected);
    }

    [Fact]
    public void Detect_WhenHeaderHoldsASingleColumn_ReturnsTheConfiguredSeparator()
    {
        // Act
        var detected = AuditCsvDelimiterDetector.Detect("RemoteIp", ';');

        // Assert
        detected.Should().Be(';');
    }

    [Fact]
    public void Detect_WhenConfiguredSeparatorIsUnusualAndSplitsTheHeader_ReturnsTheConfiguredSeparator()
    {
        // Act
        var detected = AuditCsvDelimiterDetector.Detect("FirstSeenUtc#LastSeenUtc#ServerName", '#');

        // Assert
        detected.Should().Be('#');
    }
}
