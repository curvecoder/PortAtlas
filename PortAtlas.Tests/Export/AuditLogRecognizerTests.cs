using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditLogRecognizerTests
{
    [Theory]
    [InlineData("FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,Protocol,Direction,LocalIp,LocalPort,RemoteIp,RemotePort")]
    [InlineData("FirstSeenUtc;LastSeenUtc;ServerName;LocalPort;RemoteIp;RemotePort")]
    [InlineData("FirstSeenUtc\tServerName\tRemoteIp")]
    [InlineData("LastSeenUtc|LocalPort|RemotePort")]
    [InlineData("\"FirstSeenUtc\",\"ServerName\",\"LocalPort\"")]
    [InlineData("servername,localport")]
    [InlineData("LocalPort")]
    public void IsAuditCsvHeader_WhenLineNamesAColumnTheParserReadsAHeaderFrom_ReturnsTrue(string firstLine)
    {
        // Act
        var isAuditHeader = AuditLogRecognizer.IsAuditCsvHeader(firstLine);

        // Assert
        isAuditHeader.Should().BeTrue();
    }

    [Theory]
    [InlineData("RuleId,Direction,SourceIp,SourceHost,DestinationIp,DestinationHost,DestinationZone,Protocol,DestinationPort,ServiceName,ProcessName,ExecutablePath,UserName,ObservationCount,FirstSeenUtc,LastSeenUtc")]
    [InlineData("PairId,SourceIp,SourceHost,DestinationIp,DestinationHost,FlowCount,Ports,ObservationCount,FirstSeenUtc,LastSeenUtc")]
    [InlineData("2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,,1234,psql,/usr/bin/psql,psql,,backup")]
    [InlineData("FirstSeenUtc,RemoteIp,RemotePort")]
    [InlineData("{\"serverName\":\"nas\",\"localPort\":50000}")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void IsAuditCsvHeader_WhenLineIsNotAnAuditHeader_ReturnsFalse(string? firstLine)
    {
        // Act
        var isAuditHeader = AuditLogRecognizer.IsAuditCsvHeader(firstLine);

        // Assert
        isAuditHeader.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"firstSeenUtc\":\"2026-10-24T07:00:00+00:00\",\"serverName\":\"nas\"}")]
    [InlineData("{\"localPort\":50000}")]
    [InlineData("{\"ServerName\":\"nas\"}")]
    public void IsAuditNdjsonRecord_WhenRecordCarriesAnAuditProperty_ReturnsTrue(string line)
    {
        // Act
        var isAuditRecord = AuditLogRecognizer.IsAuditNdjsonRecord(line);

        // Assert
        isAuditRecord.Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"event\":\"backup\",\"host\":\"nas\"}")]
    [InlineData("[\"serverName\"]")]
    [InlineData("\"serverName\"")]
    [InlineData("{\"serverName\":\"nas\"")]
    [InlineData("FirstSeenUtc,ServerName,LocalPort")]
    [InlineData("  ")]
    [InlineData(null)]
    public void IsAuditNdjsonRecord_WhenLineIsNotAnAuditRecord_ReturnsFalse(string? line)
    {
        // Act
        var isAuditRecord = AuditLogRecognizer.IsAuditNdjsonRecord(line);

        // Assert
        isAuditRecord.Should().BeFalse();
    }

    [Theory]
    [InlineData("audit.ndjson", true)]
    [InlineData("audit.JSONL", true)]
    [InlineData("audit.csv", false)]
    [InlineData("audit.json", false)]
    [InlineData("audit", false)]
    public void IsNdjsonPath_WhenExtensionGiven_TellsTheNdjsonFormatsFromTheOthers(string path, bool expected)
    {
        // Act
        var isNdjson = AuditLogRecognizer.IsNdjsonPath(path);

        // Assert
        isNdjson.Should().Be(expected);
    }
}
