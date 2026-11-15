using System.Text;
using AwesomeAssertions;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AuditLogSelectorTests : IDisposable
{
    private const string Header = "FirstSeenUtc,LastSeenUtc,ObservationCount,ServerName,ServerOsVersion,ServerPrimaryIp,Protocol,Direction,TcpState,LocalIp,LocalPort,RemoteIp,RemotePort,RemoteHostName,ProcessId,ProcessName,ExecutablePath,CommandLine,TargetExecutable,UserName";
    private const string NdjsonRecord = "{\"firstSeenUtc\":\"2026-10-24T07:00:00+00:00\",\"serverName\":\"nas\",\"localPort\":50000}";

    private readonly string _directory;

    public AuditLogSelectorTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_LogSelector_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the OS must not fail the test run; the folder lives in the temp directory.
        }
    }

    [Fact]
    public async Task SelectAsync_WhenDirectoryHoldsFilesThatAreNotAuditLogs_KeepsTheAuditLogsAndNamesTheOthers()
    {
        // Arrange
        var log = WriteCsvLog("audit_nas.csv");
        var matrix = Write("firewall_rules.csv", new FirewallRuleCsvFormatter().BuildHeader());
        var pairs = Write("firewall_pairs.csv", new FirewallRulePairCsvFormatter().BuildHeader());
        var empty = Write("empty.csv");
        var headerless = Write("headerless.csv", "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas,Linux,192.168.1.20,Tcp,Outbound,Established,192.168.1.20,50000,192.168.1.30,5432,,1234,psql,/usr/bin/psql,psql,,backup");
        var otherJson = Write("events.ndjson", "{\"event\":\"backup\",\"host\":\"nas\"}");
        var notJson = Write("notes.jsonl", "not a json line");

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(log);
        selection.Skipped.Should().BeEquivalentTo(
            new[] { empty, pairs, matrix, headerless, otherJson, notJson }
                .Select(path => new SkippedAuditInput(path, AuditInputSkipReason.NotAnAuditLog)));
    }

    [Theory]
    [InlineData("audit_nas.ndjson")]
    [InlineData("audit_nas.jsonl")]
    public async Task SelectAsync_WhenNdjsonLogStartsWithAByteOrderMarkAndABlankLine_KeepsIt(string name)
    {
        // Arrange
        var log = Path.Combine(_directory, name);
        await File.WriteAllTextAsync(log, Environment.NewLine + NdjsonRecord + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(log);
        selection.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_WhenOneFilePerAuditAndEnrichedCopyIsPresent_KeepsTheEnrichedCopy()
    {
        // Arrange
        var original = WriteCsvLog("audit_nas.csv");
        var enriched = WriteCsvLog("audit_nas_enriched.csv");
        var other = WriteCsvLog("audit_pihole.csv");

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(enriched, other);
        selection.Skipped.Should().Equal(new SkippedAuditInput(original, AuditInputSkipReason.SameAuditAsAnotherFile, enriched));
    }

    [Fact]
    public async Task SelectAsync_WhenOneFilePerAuditAndCsvAndNdjsonShareAName_KeepsTheCsv()
    {
        // Arrange
        var csv = WriteCsvLog("audit_nas.csv");
        var ndjson = Write("audit_nas.ndjson", NdjsonRecord);
        var jsonl = Write("audit_nas.jsonl", NdjsonRecord);

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(csv);
        selection.Skipped.Should().BeEquivalentTo(
        [
            new SkippedAuditInput(ndjson, AuditInputSkipReason.SameAuditAsAnotherFile, csv),
            new SkippedAuditInput(jsonl, AuditInputSkipReason.SameAuditAsAnotherFile, csv)
        ]);
    }

    // The names that resolve added outweigh the format: the enriched NDJSON is read before the plain CSV.
    [Fact]
    public async Task SelectAsync_WhenOneFilePerAuditAndOnlyTheNdjsonIsEnriched_KeepsTheEnrichedNdjson()
    {
        // Arrange
        WriteCsvLog("audit_nas.csv");
        Write("audit_nas.ndjson", NdjsonRecord);
        var enrichedNdjson = Write("audit_nas_enriched.ndjson", NdjsonRecord);

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(enrichedNdjson);
        selection.Skipped.Should().HaveCount(2).And.OnlyContain(skipped => skipped.KeptPath == enrichedNdjson);
    }

    [Fact]
    public async Task SelectAsync_WhenEachOriginalLogAndEnrichedCopiesArePresent_KeepsTheCsvAndTheNdjsonOriginals()
    {
        // Arrange
        var csv = WriteCsvLog("audit_nas.csv");
        var enrichedCsv = WriteCsvLog("audit_nas_enriched.csv");
        var copyOfCopy = WriteCsvLog("audit_nas_enriched_enriched.csv");
        var ndjson = Write("audit_nas.ndjson", NdjsonRecord);
        var enrichedNdjson = Write("audit_nas_enriched.ndjson", NdjsonRecord);

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.EachOriginalLog);

        // Assert
        selection.Files.Should().BeEquivalentTo(csv, ndjson);
        selection.Skipped.Should().BeEquivalentTo(
        [
            new SkippedAuditInput(enrichedCsv, AuditInputSkipReason.SameAuditAsAnotherFile, csv),
            new SkippedAuditInput(copyOfCopy, AuditInputSkipReason.SameAuditAsAnotherFile, csv),
            new SkippedAuditInput(enrichedNdjson, AuditInputSkipReason.SameAuditAsAnotherFile, ndjson)
        ]);
    }

    // Naming a copy asks for it to be read; it does not turn the original found next to it into a duplicate, which
    // would enrich the copy and leave the log itself untouched.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectAsync_WhenEachOriginalLogAndAnEnrichedCopyIsNamedNextToItsDirectory_KeepsTheOriginalToo(bool namedFirst)
    {
        // Arrange
        var original = WriteCsvLog("audit_nas.csv");
        var enriched = WriteCsvLog("audit_nas_enriched.csv");
        var copyOfCopy = WriteCsvLog("audit_nas_enriched_enriched.csv");
        string[] inputs = namedFirst ? [enriched, _directory] : [_directory, enriched];

        // Act
        var selection = await AuditLogSelector.SelectAsync(inputs, AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.EachOriginalLog);

        // Assert
        selection.Files.Should().BeEquivalentTo(original, enriched);
        selection.Skipped.Should().Equal(new SkippedAuditInput(copyOfCopy, AuditInputSkipReason.SameAuditAsAnotherFile, original));
    }

    [Fact]
    public async Task SelectAsync_WhenCsvLogStartsWithAByteOrderMark_KeepsIt()
    {
        // Arrange
        var log = Path.Combine(_directory, "audit_nas.csv");
        await File.WriteAllTextAsync(log, Header + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(log);
        selection.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_WhenTheSameDirectoryIsGivenTwiceInAnotherCase_KeepsEachLogOnce()
    {
        // Arrange
        var log = WriteCsvLog("audit_nas.csv");

        // Act
        var selection = await AuditLogSelector.SelectAsync(
            [_directory, _directory.ToUpperInvariant()],
            AuditInputPathResolver.AuditLogExtensions,
            AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(log);
        selection.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_WhenCancellationIsRequested_ThrowsOperationCanceledException()
    {
        // Arrange
        WriteCsvLog("audit_nas.csv");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var select = () => AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit, cancellation.Token);

        // Assert
        await select.Should().ThrowAsync<OperationCanceledException>();
    }

    // A log collected already enriched has no original to prefer: it is the log of its audit.
    [Theory]
    [InlineData(AuditDuplicatePolicy.OneFilePerAudit)]
    [InlineData(AuditDuplicatePolicy.EachOriginalLog)]
    public async Task SelectAsync_WhenEnrichedCopyHasNoOriginal_KeepsIt(AuditDuplicatePolicy duplicatePolicy)
    {
        // Arrange
        var enriched = WriteCsvLog("audit_nas_enriched.csv");

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, duplicatePolicy);

        // Assert
        selection.Files.Should().Equal(enriched);
        selection.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_WhenWildcardPatternGiven_SelectsAsForADirectory()
    {
        // Arrange
        var original = WriteCsvLog("audit_nas.csv");
        var enriched = WriteCsvLog("audit_nas_enriched.csv");
        var matrix = Write("audit_matrix.csv", new FirewallRuleCsvFormatter().BuildHeader());

        // Act
        var selection = await AuditLogSelector.SelectAsync([Path.Combine(_directory, "audit_*.csv")], [".csv"], AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(enriched);
        selection.Skipped.Should().BeEquivalentTo(
        [
            new SkippedAuditInput(matrix, AuditInputSkipReason.NotAnAuditLog),
            new SkippedAuditInput(original, AuditInputSkipReason.SameAuditAsAnotherFile, enriched)
        ]);
    }

    [Theory]
    [InlineData(AuditDuplicatePolicy.OneFilePerAudit)]
    [InlineData(AuditDuplicatePolicy.EachOriginalLog)]
    public async Task SelectAsync_WhenFilesAreNamed_KeepsEachOfThemWhateverTheyHold(AuditDuplicatePolicy duplicatePolicy)
    {
        // Arrange
        var original = WriteCsvLog("audit_nas.csv");
        var enriched = WriteCsvLog("audit_nas_enriched.csv");
        var headerless = Write("headerless.csv", "2026-10-24T07:00:00Z,2026-10-24T07:05:00Z,10,nas");

        // Act
        var selection = await AuditLogSelector.SelectAsync([original, enriched, headerless], AuditInputPathResolver.AuditLogExtensions, duplicatePolicy);

        // Assert
        selection.Files.Should().Equal(original, enriched, headerless);
        selection.Skipped.Should().BeEmpty();
    }

    // The named file is the one kept for its audit, in whichever order the inputs come.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectAsync_WhenAFileIsNamedNextToItsDirectory_KeepsTheNamedFileForItsAudit(bool namedFirst)
    {
        // Arrange
        var original = WriteCsvLog("audit_nas.csv");
        var enriched = WriteCsvLog("audit_nas_enriched.csv");
        string[] inputs = namedFirst ? [original, _directory] : [_directory, original];

        // Act
        var selection = await AuditLogSelector.SelectAsync(inputs, AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(original);
        selection.Skipped.Should().Equal(new SkippedAuditInput(enriched, AuditInputSkipReason.SameAuditAsAnotherFile, original));
    }

    [Fact]
    public async Task SelectAsync_WhenTwoDirectoriesHoldLogsOfTheSameName_KeepsBoth()
    {
        // Arrange
        var first = WriteCsvLog(Path.Combine("site-a", "audit_nas.csv"));
        var second = WriteCsvLog(Path.Combine("site-b", "audit_nas.csv"));

        // Act
        var selection = await AuditLogSelector.SelectAsync(
            [Path.Combine(_directory, "site-a"), Path.Combine(_directory, "site-b")],
            AuditInputPathResolver.AuditLogExtensions,
            AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(first, second);
        selection.Skipped.Should().BeEmpty();
    }

    // A file called "_enriched" is not the copy of a log without a name.
    [Fact]
    public async Task SelectAsync_WhenFileNameIsTheSuffixAlone_ReadsItAsALogOfItsOwn()
    {
        // Arrange
        var suffixOnly = WriteCsvLog("_enriched.csv");
        var other = WriteCsvLog("audit_nas.csv");

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().BeEquivalentTo(suffixOnly, other);
        selection.Skipped.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_WhenInputsMatchNothing_ReturnsAnEmptySelection()
    {
        // Act
        var selection = await AuditLogSelector.SelectAsync([Path.Combine(_directory, "missing.csv"), "  "], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().BeEmpty();
        selection.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void Describe_WhenFileHoldsAnAuditAlreadyKept_NamesBothFiles()
    {
        // Arrange
        var skipped = new SkippedAuditInput("audit_nas.csv", AuditInputSkipReason.SameAuditAsAnotherFile, "audit_nas_enriched.csv");

        // Act
        var sentence = skipped.Describe();

        // Assert
        sentence.Should().Be("Skipped audit_nas.csv: same audit as audit_nas_enriched.csv, the file kept.");
    }

    [Fact]
    public void Describe_WhenFileIsNotAnAuditLog_NamesTheFileAndTheReason()
    {
        // Arrange
        var skipped = new SkippedAuditInput("firewall_rules.csv", AuditInputSkipReason.NotAnAuditLog);

        // Act
        var sentence = skipped.Describe();

        // Assert
        sentence.Should().Be("Skipped firewall_rules.csv: its first line is not the header of an audit log.");
    }

    [Fact]
    public async Task SelectAsync_WhenAFileOfTheDirectoryIsHeldExclusively_SkipsItAndKeepsTheOthers()
    {
        // Arrange
        var readable = WriteCsvLog("audit_nas.csv");
        var locked = WriteCsvLog("audit_proxmox.csv");
        using var exclusiveHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        var selection = await AuditLogSelector.SelectAsync([_directory], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().Equal(readable);
        selection.Skipped.Should().Equal(new SkippedAuditInput(locked, AuditInputSkipReason.Unreadable));
        selection.Skipped[0].Describe().Should().Contain(locked).And.Contain("cannot be read");
    }

    [Fact]
    public async Task SelectAsync_WhenANamedFileIsHeldExclusively_ThrowsNamingTheFile()
    {
        // Arrange
        var locked = WriteCsvLog("audit_nas.csv");
        using var exclusiveHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        var select = () => AuditLogSelector.SelectAsync([locked], AuditInputPathResolver.AuditLogExtensions, AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        await select.Should().ThrowAsync<AuditLogFormatException>().WithMessage("*audit_nas.csv*cannot be read*");
    }

    [Fact]
    public async Task SelectAsync_WhenEveryFileOfTheDirectoryIsHeldExclusively_SelectsNothing()
    {
        // Arrange
        var locked = WriteCsvLog("audit_nas.csv");
        using var exclusiveHandle = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        var selection = await AuditLogSelector.SelectAsync([Path.Combine(_directory, "*.csv")], [".csv"], AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        selection.Files.Should().BeEmpty();
        selection.Skipped.Should().ContainSingle();
    }

    [Fact]
    public async Task SelectAsync_WhenWildcardPatternMatchesAnNdjsonLogForACsvCommand_LeavesItOutAsADirectoryDoes()
    {
        // Arrange
        var csv = WriteCsvLog("audit_nas.csv");
        Write("audit_nas_copy.ndjson", NdjsonRecord);

        // Act
        var fromPattern = await AuditLogSelector.SelectAsync([Path.Combine(_directory, "*")], [".csv"], AuditDuplicatePolicy.OneFilePerAudit);
        var fromDirectory = await AuditLogSelector.SelectAsync([_directory], [".csv"], AuditDuplicatePolicy.OneFilePerAudit);

        // Assert
        fromDirectory.Files.Should().Equal(csv);
        fromPattern.Should().BeEquivalentTo(fromDirectory);
    }

    private string WriteCsvLog(string relativePath) => Write(relativePath, Header);

    private string Write(string relativePath, params string[] lines)
    {
        var path = Path.Combine(_directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _directory);
        File.WriteAllLines(path, lines);
        return path;
    }
}
