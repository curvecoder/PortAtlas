using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Export;
using PortAtlas.Core.Models;
using ProcessForge.Models;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditNdjsonExporterTests
{
    private readonly AuditNdjsonExporter _exporter = new();

    [Fact]
    public async Task ExportEntriesAsync_WhenEntriesSupplied_WritesValidNdjsonFile()
    {
        // Arrange
        var tempFile = Path.Combine(Path.GetTempPath(), $"export_test_{Guid.NewGuid():N}.ndjson");
        try
        {
            var entries = new List<NetworkAuditEntry>
            {
                CreateSampleEntry("SRV-ALPHA", 80),
                CreateSampleEntry("SRV-BETA", 443)
            };

            // Act
            var exportedCount = await _exporter.ExportEntriesAsync(entries, tempFile);

            // Assert
            exportedCount.Should().Be(2);
            File.Exists(tempFile).Should().BeTrue();

            var lines = await File.ReadAllLinesAsync(tempFile);
            lines.Should().HaveCount(2);

            var parser = new AuditNdjsonParser();
            var entry1 = parser.ParseLine(lines[0]);
            var entry2 = parser.ParseLine(lines[1]);

            entry1.Should().NotBeNull();
            entry1!.Server.HostName.Should().Be("SRV-ALPHA");
            entry1.Socket.RemoteEndpoint.Port.Should().Be(80);

            entry2.Should().NotBeNull();
            entry2!.Server.HostName.Should().Be("SRV-BETA");
            entry2.Socket.RemoteEndpoint.Port.Should().Be(443);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ExportCsvToNdjsonAsync_WhenCsvFileSupplied_ConvertsAccuratelyToNdjson()
    {
        // Arrange
        var tempCsv = Path.Combine(Path.GetTempPath(), $"source_csv_{Guid.NewGuid():N}.csv");
        var tempNdjson = Path.Combine(Path.GetTempPath(), $"target_ndjson_{Guid.NewGuid():N}.ndjson");

        try
        {
            var csvContent = """
FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName
2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,1,SRV-NODE1,Windows 11,10.0.0.5,Tcp,Outbound,ESTABLISHED,10.0.0.5,50000,10.0.0.6,8080,srv-api.internal,1024,node.exe,C:\node.exe,node server.js,server.js,DOMAIN\user
""";
            await File.WriteAllTextAsync(tempCsv, csvContent);

            // Act
            var convertedCount = await _exporter.ExportCsvToNdjsonAsync(tempCsv, tempNdjson);

            // Assert
            convertedCount.Should().Be(1);
            File.Exists(tempNdjson).Should().BeTrue();

            var lines = await File.ReadAllLinesAsync(tempNdjson);
            lines.Should().HaveCount(1);

            var parser = new AuditNdjsonParser();
            var parsed = parser.ParseLine(lines[0]);
            parsed.Should().NotBeNull();
            parsed!.Server.HostName.Should().Be("SRV-NODE1");
            parsed.Socket.LocalEndpoint.Port.Should().Be(50000);
            parsed.Socket.RemoteEndpoint.Port.Should().Be(8080);
            parsed.RemoteHostName.Should().Be("srv-api.internal");
            parsed.Socket.Process.ProcessName.Should().Be("node.exe");
        }
        finally
        {
            if (File.Exists(tempCsv))
            {
                File.Delete(tempCsv);
            }

            if (File.Exists(tempNdjson))
            {
                File.Delete(tempNdjson);
            }
        }
    }

    private const string CsvHeader = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";

    [Fact]
    public async Task ExportAuditLogsToNdjsonAsync_WhenALogUsesAnotherSeparator_ExportsItsFlows()
    {
        // Arrange
        var semicolonLog = Path.Combine(Path.GetTempPath(), $"stream_semicolon_{Guid.NewGuid():N}.csv");
        var tempNdjson = Path.Combine(Path.GetTempPath(), $"stream_target_{Guid.NewGuid():N}.ndjson");

        try
        {
            await File.WriteAllLinesAsync(semicolonLog,
            [
                CsvHeader.Replace(',', ';'),
                "2026-10-25T10:00:00.0000000Z;2026-10-25T10:00:00.0000000Z;3;pi.home.arpa;Linux;192.168.1.30;Tcp;Outbound;ESTABLISHED;192.168.1.30;50002;192.168.1.40;53;pihole.home.arpa;1026;unbound;/usr/sbin/unbound;unbound;unbound;unbound"
            ]);

            // Act
            var exportedCount = await _exporter.ExportAuditLogsToNdjsonAsync([semicolonLog], tempNdjson, delimiter: ',');

            // Assert
            exportedCount.Should().Be(1);
            var entry = new AuditNdjsonParser().ParseLine((await File.ReadAllLinesAsync(tempNdjson)).Single());
            entry.Should().NotBeNull();
            entry!.Socket.RemoteEndpoint.Port.Should().Be(53);
        }
        finally
        {
            File.Delete(semicolonLog);
            File.Delete(tempNdjson);
        }
    }

    [Fact]
    public async Task ExportAuditLogsToNdjsonAsync_WhenTwoCsvFilesSupplied_WritesEveryEntryInInputOrder()
    {
        // Arrange
        var tempCsv1 = Path.Combine(Path.GetTempPath(), $"stream_src1_{Guid.NewGuid():N}.csv");
        var tempCsv2 = Path.Combine(Path.GetTempPath(), $"stream_src2_{Guid.NewGuid():N}.csv");
        var tempNdjson = Path.Combine(Path.GetTempPath(), $"stream_target_{Guid.NewGuid():N}.ndjson");

        try
        {
            var content1 = string.Join(
                Environment.NewLine,
                CsvHeader,
                "2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,1,SRV-APP-01,Linux,192.0.2.10,Tcp,Outbound,ESTABLISHED,192.0.2.10,50000,192.0.2.20,8080,srv-api.internal,1024,node.exe,C:\\node.exe,node server.js,server.js,DOMAIN\\user",
                "2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,2,SRV-APP-01,Linux,192.0.2.10,Tcp,Outbound,ESTABLISHED,192.0.2.10,50001,192.0.2.21,443,srv-web.internal,1025,node.exe,C:\\node.exe,node server.js,server.js,DOMAIN\\user");

            var content2 = string.Join(
                Environment.NewLine,
                CsvHeader,
                "2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,3,SRV-APP-02,Linux,192.0.2.11,Tcp,Outbound,ESTABLISHED,192.0.2.11,50002,192.0.2.22,1521,srv-db.internal,1026,oracle.exe,C:\\oracle.exe,oracle,oracle,DOMAIN\\user");

            await File.WriteAllTextAsync(tempCsv1, content1);
            await File.WriteAllTextAsync(tempCsv2, content2);

            // Act
            var exportedCount = await _exporter.ExportAuditLogsToNdjsonAsync([tempCsv1, tempCsv2], tempNdjson);

            // Assert
            exportedCount.Should().Be(3);
            File.Exists(tempNdjson).Should().BeTrue();

            var lines = await File.ReadAllLinesAsync(tempNdjson);
            lines.Should().HaveCount(3);

            var parser = new AuditNdjsonParser();
            var entry1 = parser.ParseLine(lines[0]);
            var entry2 = parser.ParseLine(lines[1]);
            var entry3 = parser.ParseLine(lines[2]);

            entry1.Should().NotBeNull();
            entry1!.Server.HostName.Should().Be("SRV-APP-01");
            entry1.Socket.RemoteEndpoint.Port.Should().Be(8080);

            entry2.Should().NotBeNull();
            entry2!.Server.HostName.Should().Be("SRV-APP-01");
            entry2.Socket.RemoteEndpoint.Port.Should().Be(443);

            entry3.Should().NotBeNull();
            entry3!.Server.HostName.Should().Be("SRV-APP-02");
            entry3.Socket.RemoteEndpoint.Port.Should().Be(1521);
        }
        finally
        {
            if (File.Exists(tempCsv1))
            {
                File.Delete(tempCsv1);
            }

            if (File.Exists(tempCsv2))
            {
                File.Delete(tempCsv2);
            }

            if (File.Exists(tempNdjson))
            {
                File.Delete(tempNdjson);
            }
        }
    }

    [Fact]
    public async Task ExportAuditLogsToNdjsonAsync_WhenOutputAlreadyExists_ReplacesItsContent()
    {
        // Arrange
        var tempCsv = Path.Combine(Path.GetTempPath(), $"stream_src_replace_{Guid.NewGuid():N}.csv");
        var tempNdjson = Path.Combine(Path.GetTempPath(), $"stream_target_replace_{Guid.NewGuid():N}.ndjson");

        try
        {
            var content = string.Join(
                Environment.NewLine,
                CsvHeader,
                "2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,1,SRV-APP-01,Linux,192.0.2.10,Tcp,Outbound,ESTABLISHED,192.0.2.10,50000,192.0.2.20,8080,srv-api.internal,1024,node.exe,C:\\node.exe,node server.js,server.js,DOMAIN\\user");

            await File.WriteAllTextAsync(tempCsv, content);
            await File.WriteAllTextAsync(tempNdjson, "{\"preexisting\":\"data\"}\n{\"preexisting\":\"second_line\"}");

            // Act
            var exportedCount = await _exporter.ExportAuditLogsToNdjsonAsync([tempCsv], tempNdjson);

            // Assert
            exportedCount.Should().Be(1);
            var lines = await File.ReadAllLinesAsync(tempNdjson);
            lines.Should().HaveCount(1);
            lines[0].Should().NotContain("preexisting");

            var parser = new AuditNdjsonParser();
            var entry = parser.ParseLine(lines[0]);
            entry.Should().NotBeNull();
            entry!.Server.HostName.Should().Be("SRV-APP-01");
        }
        finally
        {
            if (File.Exists(tempCsv))
            {
                File.Delete(tempCsv);
            }

            if (File.Exists(tempNdjson))
            {
                File.Delete(tempNdjson);
            }
        }
    }

    [Fact]
    public async Task ExportAuditLogsToNdjsonAsync_WhenInputListIsEmpty_WritesEmptyFileAndReturnsZero()
    {
        // Arrange
        var tempNdjson = Path.Combine(Path.GetTempPath(), $"stream_empty_{Guid.NewGuid():N}.ndjson");

        try
        {
            // Act
            var exportedCount = await _exporter.ExportAuditLogsToNdjsonAsync(Array.Empty<string>(), tempNdjson);

            // Assert
            exportedCount.Should().Be(0);
            File.Exists(tempNdjson).Should().BeTrue();
            var lines = await File.ReadAllLinesAsync(tempNdjson);
            lines.Should().BeEmpty();
        }
        finally
        {
            if (File.Exists(tempNdjson))
            {
                File.Delete(tempNdjson);
            }
        }
    }

    [Fact]
    public Task ExportAuditLogsToNdjsonAsync_WhenInputListIsNull_ThrowsArgumentNullException()
    {
        // Arrange
        var tempNdjson = Path.Combine(Path.GetTempPath(), $"stream_null_{Guid.NewGuid():N}.ndjson");

        // Act
        var act = () => _exporter.ExportAuditLogsToNdjsonAsync(null!, tempNdjson);

        // Assert
        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    // The logs are read while the output is written: a log that stops the export halfway must not leave the
    // previous output emptied or cut.
    [Fact]
    public async Task ExportAuditLogsToNdjsonAsync_WhenALogCannotBeReadHalfway_KeepsThePreviousOutput()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("PortAtlas_NdjsonAtomic_").FullName;
        try
        {
            var readable = Path.Combine(folder, "nas.csv");
            await File.WriteAllLinesAsync(readable, [CsvHeader, "2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,1,nas,Linux,192.168.1.20,Tcp,Outbound,ESTABLISHED,192.168.1.20,50000,192.168.1.30,5432,db.home.arpa,1024,psql,/usr/bin/psql,psql -h db,,backup"]);
            var unclosedQuote = Path.Combine(folder, "proxmox.csv");
            await File.WriteAllLinesAsync(unclosedQuote, [CsvHeader, "2026-10-25T10:00:00.0000000Z,2026-10-25T10:00:00.0000000Z,1,proxmox,Linux,192.168.1.10,Tcp,Outbound,ESTABLISHED,192.168.1.10,50000,192.168.1.30,5432,,1024,psql,/usr/bin/psql,\"psql -h db,,backup"]);
            var output = Path.Combine(folder, "audit.ndjson");
            await File.WriteAllTextAsync(output, "previous export\n");

            // Act
            var act = () => _exporter.ExportAuditLogsToNdjsonAsync([readable, unclosedQuote], output);

            // Assert
            await act.Should().ThrowAsync<AuditLogFormatException>();
            (await File.ReadAllTextAsync(output)).Should().Be("previous export\n");
            Directory.GetFiles(folder).Should().BeEquivalentTo([readable, unclosedQuote, output]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAuditLogsToNdjsonAsync_WhenNdjsonLinesAreNotValidJson_NamesTheLogAndTheLines()
    {
        // Arrange
        var folder = Directory.CreateTempSubdirectory("PortAtlas_NdjsonRejects_").FullName;
        try
        {
            var source = Path.Combine(folder, "nas.ndjson");
            var valid = new AuditNdjsonFormatter().FormatEntry(CreateSampleEntry("nas", 5432));
            await File.WriteAllLinesAsync(source, [valid, "{\"serverName\":\"nas\",", "", "not json", valid]);
            var output = Path.Combine(folder, "audit.ndjson");
            var messages = new List<string>();
            var exporter = new AuditNdjsonExporter(messages.Add);

            // Act
            var count = await exporter.ExportAuditLogsToNdjsonAsync([source], output);

            // Assert
            count.Should().Be(2);
            messages.Should().ContainSingle().Which.Should().Be($"Skipped 2 line(s) of {source} that are not valid JSON: lines 2, 4.");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static NetworkAuditEntry CreateSampleEntry(string hostName, int remotePort)
    {
        var now = DateTimeOffset.UtcNow;
        var server = new ServerHostMetadata(hostName, "Linux", "10.0.0.1");
        var process = new ProcessMetadata(100, "proc", "/bin/proc", "proc", "/bin/proc", "root");
        var socket = new ActiveSocketConnection(
            Protocol: SocketTransportProtocol.Tcp,
            Direction: SocketFlowDirection.Outbound,
            LocalEndpoint: new SocketEndpoint(IPAddress.Parse("10.0.0.1"), 40000),
            RemoteEndpoint: new SocketEndpoint(IPAddress.Parse("10.0.0.2"), remotePort),
            State: "ESTABLISHED",
            Process: process);

        return new NetworkAuditEntry(now, now, 1, server, socket);
    }
}
