using AwesomeAssertions;
using PortAtlas.Gui.Adapters;

namespace PortAtlas.Gui.Tests.Adapters;

public sealed class ProfileDirectoryProviderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("portatlas-provider-").FullName;
    private string ApplicationDirectory => Path.Combine(_root, "app");
    private string LegacyDirectory => Path.Combine(_root, "legacy");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void GetProfileDirectory_WhenNothingExists_ReturnsProfilesFolderNextToTheApplication()
    {
        // Arrange
        var provider = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory);

        // Act
        var directory = provider.GetProfileDirectory();

        // Assert
        directory.Should().Be(Path.Combine(ApplicationDirectory, "Data", "profiles"));
        Directory.Exists(directory).Should().BeTrue();
    }

    [Fact]
    public void GetProfileDirectory_WhenLegacyProfilesExist_CopiesProfilesAndPreferences()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(LegacyDirectory, "profiles"));
        File.WriteAllText(Path.Combine(LegacyDirectory, "profiles", "home.json"), "{}");
        File.WriteAllText(Path.Combine(LegacyDirectory, "preferences.json"), "{\"theme\":1}");
        var provider = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory);

        // Act
        var directory = provider.GetProfileDirectory();

        // Assert
        File.ReadAllText(Path.Combine(directory, "home.json")).Should().Be("{}");
        File.ReadAllText(Path.Combine(directory, "..", "preferences.json")).Should().Be("{\"theme\":1}");
        File.Exists(Path.Combine(LegacyDirectory, "profiles", "home.json")).Should().BeTrue();
    }

    [Fact]
    public void GetProfileDirectory_WhenACopyFailedAtThePreviousStart_CopiesEveryLegacyProfileAtTheNextOne()
    {
        // Arrange: a profile held open by another program cannot be read, so the first copy stops on it.
        var legacyProfiles = Path.Combine(LegacyDirectory, "profiles");
        Directory.CreateDirectory(legacyProfiles);
        File.WriteAllText(Path.Combine(legacyProfiles, "home.json"), "home");
        File.WriteAllText(Path.Combine(legacyProfiles, "nas.json"), "nas");
        File.WriteAllText(Path.Combine(LegacyDirectory, "preferences.json"), "{\"theme\":1}");
        string firstDirectory;
        using (new FileStream(Path.Combine(legacyProfiles, "nas.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            firstDirectory = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory).GetProfileDirectory();
        }

        // Act
        var directory = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory).GetProfileDirectory();

        // Assert
        firstDirectory.Should().Be(legacyProfiles);
        directory.Should().Be(Path.Combine(ApplicationDirectory, "Data", "profiles"));
        Directory.GetFiles(directory).Select(Path.GetFileName).Should().BeEquivalentTo("home.json", "nas.json");
        File.ReadAllText(Path.Combine(directory, "nas.json")).Should().Be("nas");
        File.ReadAllText(Path.Combine(directory, "..", "preferences.json")).Should().Be("{\"theme\":1}");
        Directory.Exists(directory + ".migrating").Should().BeFalse();
    }

    [Fact]
    public void GetProfileDirectory_WhenAStoppedCopyLeftAReadOnlyFile_CopiesTheLegacyProfilesAndRemovesIt()
    {
        // Arrange: File.Copy keeps the read-only attribute of a legacy profile.
        var legacyProfiles = Path.Combine(LegacyDirectory, "profiles");
        Directory.CreateDirectory(legacyProfiles);
        File.WriteAllText(Path.Combine(legacyProfiles, "home.json"), "home");
        var staging = Path.Combine(ApplicationDirectory, "Data", "profiles.migrating");
        Directory.CreateDirectory(staging);
        var leftover = Path.Combine(staging, "home.json");
        File.WriteAllText(leftover, "partial");
        File.SetAttributes(leftover, FileAttributes.ReadOnly);
        var provider = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory);

        // Act
        var directory = provider.GetProfileDirectory();

        // Assert
        directory.Should().Be(Path.Combine(ApplicationDirectory, "Data", "profiles"));
        File.ReadAllText(Path.Combine(directory, "home.json")).Should().Be("home");
        Directory.Exists(staging).Should().BeFalse();
    }

    [Fact]
    public void GetProfileDirectory_WhenALegacyProfileIsReadOnly_CopiesItWritable()
    {
        // Arrange
        var legacyProfiles = Path.Combine(LegacyDirectory, "profiles");
        Directory.CreateDirectory(legacyProfiles);
        var legacyProfile = Path.Combine(legacyProfiles, "home.json");
        File.WriteAllText(legacyProfile, "home");
        File.SetAttributes(legacyProfile, FileAttributes.ReadOnly);
        var provider = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory);

        try
        {
            // Act
            var directory = provider.GetProfileDirectory();

            // Assert
            File.GetAttributes(Path.Combine(directory, "home.json")).HasFlag(FileAttributes.ReadOnly).Should().BeFalse();
        }
        finally
        {
            File.SetAttributes(legacyProfile, FileAttributes.Normal);
        }
    }

    [Fact]
    public void GetProfileDirectory_WhenNewProfilesAlreadyExist_DoesNotOverwriteThemWithLegacyOnes()
    {
        // Arrange
        Directory.CreateDirectory(Path.Combine(LegacyDirectory, "profiles"));
        File.WriteAllText(Path.Combine(LegacyDirectory, "profiles", "home.json"), "old");
        var newProfiles = Path.Combine(ApplicationDirectory, "Data", "profiles");
        Directory.CreateDirectory(newProfiles);
        File.WriteAllText(Path.Combine(newProfiles, "home.json"), "new");
        var provider = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory);

        // Act
        var directory = provider.GetProfileDirectory();

        // Assert
        File.ReadAllText(Path.Combine(directory, "home.json")).Should().Be("new");
    }

    [Fact]
    public void GetProfileDirectory_WhenApplicationFolderIsNotWritable_FallsBackToTheLegacyFolder()
    {
        // Arrange: a file where the Data folder should be makes the folder impossible to create.
        Directory.CreateDirectory(ApplicationDirectory);
        File.WriteAllText(Path.Combine(ApplicationDirectory, "Data"), "blocker");
        var provider = new ProfileDirectoryProvider(ApplicationDirectory, LegacyDirectory);

        // Act
        var directory = provider.GetProfileDirectory();

        // Assert
        directory.Should().Be(Path.Combine(LegacyDirectory, "profiles"));
    }
}
