using System.Security.AccessControl;
using System.Security.Principal;
using AwesomeAssertions;
using PortAtlas.Gui.Core.Profiles;

namespace PortAtlas.Gui.Tests.Profiles;

public sealed class UserPreferencesStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("portatlas-preferences-").FullName;
    private readonly UserPreferencesStore _store;

    public UserPreferencesStoreTests() =>
        _store = new UserPreferencesStore(new FixedProfileDirectory(Path.Combine(_root, "profiles")));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Load_WhenNothingIsStored_ReturnsTheDefaults()
    {
        // Act
        var preferences = _store.Load();

        // Assert
        preferences.Should().Be(new UserPreferences());
    }

    [Fact]
    public async Task Update_WhenCalledTwice_KeepsTheFieldsTheSecondCallDoesNotTouch()
    {
        // Act
        await _store.UpdateAsync(p => p with { Theme = ThemeChoice.Dark });
        await _store.UpdateAsync(p => p with { LastProfile = "homelab" });

        // Assert
        _store.Load().Should().Be(new UserPreferences(ThemeChoice.Dark, "homelab"));
    }

    [Fact]
    public async Task Update_WhenThePreferencesFileIsASymbolicLink_WritesThroughTheLink()
    {
        // Arrange
        var linked = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "linked")).FullName, "preferences.json");
        await File.WriteAllTextAsync(linked, "{}");
        File.CreateSymbolicLink(Path.Combine(_root, "preferences.json"), linked);

        // Act
        await _store.UpdateAsync(p => p with { LastProfile = "homelab" });

        // Assert
        (await File.ReadAllTextAsync(linked)).Should().Contain("homelab");
    }

    [Fact]
    public void Load_WhenFileIsNotJson_ReturnsTheDefaults()
    {
        // Arrange
        File.WriteAllText(Path.Combine(_root, "preferences.json"), "{ not json");

        // Act
        var preferences = _store.Load();

        // Assert
        preferences.Should().Be(new UserPreferences());
    }

    [Fact]
    public void Load_WhenTheUserIsDeniedReadingTheFile_ReturnsTheDefaults()
    {
        // Access control lists, and the error they raise, are those of Windows, where the GUI is published.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Arrange
        var file = new FileInfo(Path.Combine(_root, "preferences.json"));
        File.WriteAllText(file.FullName, """{ "Theme": "Dark" }""");
        var security = file.GetAccessControl();
        var denyContent = new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, domainSid: null), FileSystemRights.ReadData, AccessControlType.Deny);
        security.AddAccessRule(denyContent);
        file.SetAccessControl(security);

        try
        {
            // Act
            var preferences = _store.Load();

            // Assert
            preferences.Should().Be(new UserPreferences());
        }
        finally
        {
            security.RemoveAccessRule(denyContent);
            file.SetAccessControl(security);
        }
    }

    [Fact]
    public async Task Update_WhenProfilesAreListed_DoesNotShowThePreferencesFileAsAProfile()
    {
        // Arrange
        await _store.UpdateAsync(p => p with { Theme = ThemeChoice.Light });
        var profiles = new ProfileStore(new FixedProfileDirectory(Path.Combine(_root, "profiles")));

        // Act
        var names = profiles.ListNames();

        // Assert
        names.Should().BeEmpty();
    }

    private sealed class FixedProfileDirectory(string path) : IProfileDirectoryProvider
    {
        public string GetProfileDirectory() => path;
    }
}
