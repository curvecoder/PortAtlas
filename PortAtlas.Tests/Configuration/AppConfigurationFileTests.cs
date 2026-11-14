using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using PortAtlas.Core.Export;
using Xunit;

namespace PortAtlas.Tests.Configuration;

public sealed class AppConfigurationFileTests : IDisposable
{
    private readonly string _directory;

    public AppConfigurationFileTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_ConfigFile_" + Guid.NewGuid().ToString("N"));
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
    public void Load_WhenOutputPathsContainTokens_ExpandsThem()
    {
        // Arrange
        var path = WriteConfiguration("""{ "OutputCsvPath": "audit_{HostName}.csv", "DnsCacheFilePath": "cache_{HostName}.json" }""");

        // Act
        var loaded = AppConfigurationFile.Load(path);

        // Assert
        loaded.Configuration.OutputCsvPath.Should().NotContain("{HostName}").And.StartWith("audit_");
        loaded.Configuration.DnsCacheFilePath.Should().NotContain("{HostName}").And.StartWith("cache_");
    }

    [Fact]
    public void Load_WhenFileIsMissing_KeepsSystemAccountProcessesRecorded()
    {
        // Act
        var loaded = AppConfigurationFile.Load(path: null, _directory, _directory);

        // Assert
        loaded.Configuration.ExcludeSystemAccounts.Should().BeFalse();
    }

    [Fact]
    public void Load_WhenFileIsMissing_WaitsThreeSecondsForReverseDns()
    {
        // Act
        var loaded = AppConfigurationFile.Load(path: null, _directory, _directory);

        // Assert
        loaded.Configuration.DnsResolutionTimeoutMs.Should().Be(3000);
    }

    [Fact]
    public void Load_WhenGivenPathDoesNotExist_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = Path.Combine(_directory, "absent.json");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path)
            .And.Contain("given by --config does not exist")
            .And.NotContain("neither in", "an absolute path is looked up in one place only");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Load_WhenGivenPathIsBlank_ThrowsConfigurationFileException(string blankPath)
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");
        File.WriteAllText(Path.Combine(workingDirectory, AppConfigurationFile.DefaultPath), """{ "ScanIntervalSeconds": 9 }""");

        // Act
        var act = () => AppConfigurationFile.Load(blankPath, workingDirectory, executableDirectory);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith("--config").And.Contain("is empty");
    }

    [Fact]
    public void Load_WhenGivenRelativePathIsMissingFromBothDirectories_ThrowsConfigurationFileExceptionNamingThem()
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");

        // Act
        var act = () => AppConfigurationFile.Load("custom.json", workingDirectory, executableDirectory);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith("custom.json")
            .And.Contain("given by --config does not exist")
            .And.Contain(workingDirectory)
            .And.Contain(executableDirectory);
    }

    [Fact]
    public void Load_WhenGivenRelativePathExistsOnlyBesideTheExecutable_ReadsIt()
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");
        var besideExecutable = Path.Combine(executableDirectory, "custom.json");
        File.WriteAllText(besideExecutable, """{ "ScanIntervalSeconds": 9 }""");

        // Act
        var loaded = AppConfigurationFile.Load("custom.json", workingDirectory, executableDirectory);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(9);
        loaded.FilePath.Should().Be(besideExecutable);
    }

    [Fact]
    public void Load_WhenGivenRelativePathExistsInBothDirectories_ReadsTheOneOfTheWorkingDirectory()
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");
        var inWorkingDirectory = Path.Combine(workingDirectory, "custom.json");
        File.WriteAllText(inWorkingDirectory, """{ "ScanIntervalSeconds": 7 }""");
        File.WriteAllText(Path.Combine(executableDirectory, "custom.json"), """{ "ScanIntervalSeconds": 9 }""");

        // Act
        var loaded = AppConfigurationFile.Load("custom.json", workingDirectory, executableDirectory);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(7);
        loaded.FilePath.Should().Be(inWorkingDirectory);
    }

    [Fact]
    public void Load_WhenNoPathIsGivenAndFileExistsOnlyBesideTheExecutable_ReadsIt()
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");
        var besideExecutable = Path.Combine(executableDirectory, AppConfigurationFile.DefaultPath);
        File.WriteAllText(besideExecutable, """{ "ScanIntervalSeconds": 9 }""");

        // Act
        var loaded = AppConfigurationFile.Load(path: null, workingDirectory, executableDirectory);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(9);
        loaded.FilePath.Should().Be(besideExecutable);
    }

    [Fact]
    public void Load_WhenNoPathIsGivenAndFileExistsInBothDirectories_ReadsTheOneOfTheWorkingDirectory()
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");
        var inWorkingDirectory = Path.Combine(workingDirectory, AppConfigurationFile.DefaultPath);
        File.WriteAllText(inWorkingDirectory, """{ "ScanIntervalSeconds": 7 }""");
        File.WriteAllText(Path.Combine(executableDirectory, AppConfigurationFile.DefaultPath), """{ "ScanIntervalSeconds": 9 }""");

        // Act
        var loaded = AppConfigurationFile.Load(path: null, workingDirectory, executableDirectory);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(7);
        loaded.FilePath.Should().Be(inWorkingDirectory);
    }

    [Fact]
    public void Load_WhenNoPathIsGivenAndNoFileExists_ReturnsDefaultsAndNamesTheFileOfTheWorkingDirectory()
    {
        // Arrange
        var workingDirectory = CreateSubdirectory("work");
        var executableDirectory = CreateSubdirectory("exe");

        // Act
        var loaded = AppConfigurationFile.Load(path: null, workingDirectory, executableDirectory);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(5);
        loaded.FilePath.Should().Be(Path.Combine(workingDirectory, AppConfigurationFile.DefaultPath));
    }

    [Fact]
    public void Load_WhenRemovedIsolatedModeKeyIsTrue_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "IsolatedMode": true }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>().WithMessage("*IsolatedMode*ResolveIpOwners*");
    }

    [Fact]
    public void Load_WhenRemovedIsolatedModeKeyIsFalse_LoadsTheOtherValues()
    {
        // Arrange
        var path = WriteConfiguration("""{ "IsolatedMode": false, "ScanIntervalSeconds": 9 }""");

        // Act
        var loaded = AppConfigurationFile.Load(path);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(9);
    }

    [Fact]
    public void Load_WhenFileIsMalformedJson_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ResolveIpOwners": false, """);

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain("not valid JSON");
    }

    [Fact]
    public void Load_WhenValueHasTheWrongType_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ResolveIpOwners": false, "ScanIntervalSeconds": "five" }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain("$.ScanIntervalSeconds");
    }

    [Fact]
    public void Load_WhenDocumentIsNull_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("null");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain("JSON object");
    }

    [Fact]
    public void Load_WhenKeyIsUnknown_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ResolveIpOwner": false }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain("\"ResolveIpOwner\" is not a configuration key");
    }

    [Fact]
    public void Load_WhenKeyDiffersOnlyByCase_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "resolveipowners": false }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path)
            .And.Contain("\"resolveipowners\" is not a configuration key")
            .And.Contain("write \"ResolveIpOwners\"");
    }

    [Fact]
    public void Load_WhenRemovedIsolatedModeKeyIsWrittenInAnotherCase_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "isolatedMode": true }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path)
            .And.Contain("\"isolatedMode\" is not a configuration key")
            .And.Contain("removed in 2.0.0")
            .And.Contain("\"ResolveIpOwners\": false");
    }

    [Fact]
    public void Load_WhenRemovedIsolatedModeKeyIsFalseBesideAnUnknownKey_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "IsolatedMode": false, "ResolveIpOwner": false }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().Contain("\"ResolveIpOwner\" is not a configuration key");
    }

    public static TheoryData<string> KeysThatCannotBeNull() =>
        new(typeof(AppConfiguration).GetProperties().Select(property => property.Name));

    [Theory]
    [MemberData(nameof(KeysThatCannotBeNull))]
    public void Load_WhenAKeyThatCannotBeNullIsNull_ThrowsConfigurationFileException(string key)
    {
        // Arrange
        var path = WriteConfiguration($$"""{ "{{key}}": null }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain($"\"{key}\" is null");
    }

    [Theory]
    [InlineData("ExcludedExactNames", """["System", null]""")]
    [InlineData("ExcludedPids", "[0, null]")]
    public void Load_WhenAListHoldsNull_ThrowsConfigurationFileException(string key, string list)
    {
        // Arrange
        var path = WriteConfiguration($$"""{ "{{key}}": {{list}} }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain($"\"{key}\" holds a null entry");
    }

    [Fact]
    public void Load_WhenANumberKeyHoldsAnObject_ReportsTheWrongTypeAndNotANullEntry()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ScanIntervalSeconds": { "Seconds": null } }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().Contain("wrong type").And.Contain("$.ScanIntervalSeconds");
    }

    [Fact]
    public void Load_WhenAHostAliasIsNull_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "CustomHostAliases": { "192.168.1.10": null } }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain("\"CustomHostAliases\" holds a null entry");
    }

    [Fact]
    public void Load_WhenTheFileStillHoldsReloadHistoryLimit_ThrowsNamingTheKey()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ReloadHistoryLimit": null }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().Contain("\"ReloadHistoryLimit\" is not a configuration key");
    }

    [Fact]
    public void Load_WhenFileIsTheTemplateShippedWithTheExecutable_LoadsIt()
    {
        // Arrange
        var template = Path.Combine(AppContext.BaseDirectory, AppConfigurationFile.DefaultPath);
        File.Exists(template).Should().BeTrue("the build copies the template next to the test assembly");

        // Act
        var loaded = AppConfigurationFile.Load(template);

        // Assert
        loaded.Configuration.ResolveIpOwners.Should().BeTrue();
        loaded.Configuration.CsvColumns.Should().Equal(AuditCsvColumnCatalog.GetAllDescriptors().Select(column => column.Name));
    }

    [Fact]
    public void Load_WhenFileIsGeneratedByConfigInit_LoadsIt()
    {
        // Arrange
        var path = ConfigurationTemplateGenerator.GenerateDefaultConfigFile(Path.Combine(_directory, "generated.json"));

        // Act
        var loaded = AppConfigurationFile.Load(path);

        // Assert
        loaded.Configuration.ExcludedPids.Should().Equal(0, 4);
        loaded.Configuration.LogRewriteIntervalMinutes.Should().Be(5);
    }

    [Fact]
    public void Load_WhenFileIsLockedByAnotherProcess_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ResolveIpOwners": false }""");
        using var exclusiveHandle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path).And.Contain("cannot be read");
    }

    [Fact]
    public void Load_WhenFileContentIsDeniedToTheUser_ThrowsConfigurationFileException()
    {
        // Arrange
        var path = WriteConfiguration("""{ "ResolveIpOwners": false }""");
        var file = new FileInfo(path);
        var denyContent = new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null),
            FileSystemRights.ReadData,
            AccessControlType.Deny);
        var security = file.GetAccessControl();
        security.AddAccessRule(denyContent);
        file.SetAccessControl(security);

        try
        {
            // Act
            var act = () => AppConfigurationFile.Load(path);

            // Assert
            act.Should().Throw<ConfigurationFileException>()
                .Which.Message.Should().StartWith(path).And.Contain("cannot be read");
        }
        finally
        {
            security.RemoveAccessRule(denyContent);
            file.SetAccessControl(security);
        }
    }

    [Fact]
    public void Load_WhenOnlyLegacyNetflowauditJsonExists_IgnoresIt()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_directory, "netflowaudit.json"), """{ "ScanIntervalSeconds": 42 }""");

        // Act
        var loaded = AppConfigurationFile.Load(path: null, _directory, _directory);

        // Assert
        loaded.Configuration.ScanIntervalSeconds.Should().Be(5);
    }

    private string CreateSubdirectory(string name) =>
        Directory.CreateDirectory(Path.Combine(_directory, name)).FullName;

    private string WriteConfiguration(string json)
    {
        var path = Path.Combine(_directory, "portatlas.json");
        File.WriteAllText(path, json);
        return path;
    }
}
