using System.Text;
using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditLogRecognizerTests : IDisposable
{
    private const string CsvHeader = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,Protocol,Direction,LocalPort,RemoteIp,RemotePort";

    private readonly string _directory = Directory.CreateTempSubdirectory("portatlas-recognizer-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

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

    [Theory]
    [InlineData("\uFEFF" + CsvHeader + "\r\n2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,1,nas,Tcp,Outbound,50000,192.168.1.30,5432\r\n")]
    [InlineData(CsvHeader + "\n")]
    [InlineData(CsvHeader)]
    public async Task IsAuditLogAsync_WhenCsvStartsWithAnAuditHeader_ReturnsTrue(string content)
    {
        // Arrange
        var path = WriteFile("audit.csv", content);

        // Act
        var isAuditLog = await AuditLogRecognizer.IsAuditLogAsync(path);

        // Assert
        isAuditLog.Should().BeTrue();
    }

    [Fact]
    public async Task IsAuditLogAsync_WhenCsvStartsWithABlankLine_ReturnsFalse()
    {
        // Arrange
        var path = WriteFile("audit.csv", "\r\n" + CsvHeader + "\r\n");

        // Act
        var isAuditLog = await AuditLogRecognizer.IsAuditLogAsync(path);

        // Assert
        isAuditLog.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"serverName\":\"nas\",\"localPort\":50000}\n")]
    [InlineData("\r\n\r\n  \n{\"serverName\":\"nas\",\"localPort\":50000}")]
    [InlineData("\r\n{\"serverName\":\"nas\",\"localPort\":50000}\r{\"serverName\":\"pve\"}")]
    public async Task IsAuditLogAsync_WhenNdjsonFirstRecordIsAnAuditRecord_ReturnsTrue(string content)
    {
        // Arrange
        var path = WriteFile("audit.ndjson", content);

        // Act
        var isAuditLog = await AuditLogRecognizer.IsAuditLogAsync(path);

        // Assert
        isAuditLog.Should().BeTrue();
    }

    [Fact]
    public async Task IsAuditLogAsync_WhenCsvFirstLineIsJustUnderTheReadLimit_ReturnsTrue()
    {
        // Arrange
        var padding = new string('x', AuditLogRecognizer.MaxFirstLineLength - CsvHeader.Length - 2);
        var path = WriteFile("audit.csv", CsvHeader + "," + padding + "\r\n");

        // Act
        var isAuditLog = await AuditLogRecognizer.IsAuditLogAsync(path);

        // Assert
        isAuditLog.Should().BeTrue();
    }

    [Fact]
    public async Task IsAuditLogAsync_WhenCsvFirstLineIsLongerThanTheReadLimit_ReturnsFalse()
    {
        // Arrange: an audit header followed, on the same line, by more text than an audit header can hold.
        var path = WriteFile("audit.csv", CsvHeader + "," + new string('x', AuditLogRecognizer.MaxFirstLineLength) + "\r\n");

        // Act
        var isAuditLog = await AuditLogRecognizer.IsAuditLogAsync(path);

        // Assert
        isAuditLog.Should().BeFalse();
    }

    [Fact]
    public async Task IsAuditLogAsync_WhenNdjsonFirstRecordIsLongerThanTheReadLimit_ReturnsFalse()
    {
        // Arrange
        var path = WriteFile(
            "audit.ndjson",
            "\n{\"serverName\":\"nas\",\"commandLine\":\"" + new string('x', AuditLogRecognizer.MaxFirstLineLength) + "\"}\n");

        // Act
        var isAuditLog = await AuditLogRecognizer.IsAuditLogAsync(path);

        // Assert
        isAuditLog.Should().BeFalse();
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }
}
