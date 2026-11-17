using PortAtlas.Gui.Core.Profiles;

namespace PortAtlas.Gui.Adapters;

/// <summary>
/// Stores profiles in a <c>Data</c> folder next to the executable, and falls back to the per-user folder
/// when the installation folder cannot be written. Profiles found in the per-user folder are copied once.
/// </summary>
/// <param name="applicationDirectory">Folder of the executable.</param>
/// <param name="legacyDataDirectory">Per-user folder used by earlier versions.</param>
public sealed class ProfileDirectoryProvider(string applicationDirectory, string legacyDataDirectory) : IProfileDirectoryProvider
{
    private const string DataFolderName = "Data";
    private const string ProfilesFolderName = "profiles";
    private const string PreferencesFileName = "preferences.json";
    private const string MigrationSuffix = ".migrating";

    /// <summary>
    /// Per-user folder of the earlier versions, kept as fallback and as migration source.
    /// </summary>
    /// <returns>Absolute path of the per-user data folder.</returns>
    public static string GetLegacyDataDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PortAtlas");
        }

        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configRoot = string.IsNullOrWhiteSpace(xdgConfig)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : xdgConfig;
        return Path.Combine(configRoot, "portatlas");
    }

    /// <inheritdoc />
    public string GetProfileDirectory()
    {
        var dataDirectory = Path.Combine(applicationDirectory, DataFolderName);
        var profileDirectory = Path.Combine(dataDirectory, ProfilesFolderName);
        var legacyProfiles = Path.Combine(legacyDataDirectory, ProfilesFolderName);

        try
        {
            if (!Directory.Exists(profileDirectory))
            {
                CopyLegacyData(legacyProfiles, profileDirectory, dataDirectory);
            }

            return profileDirectory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An installation under Program Files is read-only for a standard user: the profiles then stay in the
            // per-user folder, as in earlier versions, instead of blocking the application at startup.
            return legacyProfiles;
        }
    }

    // The copy is made beside its destination and renamed into place only once complete: a copy stopped half-way
    // (a profile held open, a full disk) leaves no profile folder, so the next start copies everything again instead of
    // using a folder that lacks some profiles. A staging folder left by such a stop is discarded first.
    private void CopyLegacyData(string legacyProfiles, string profileDirectory, string dataDirectory)
    {
        var stagingDirectory = profileDirectory + MigrationSuffix;
        if (Directory.Exists(stagingDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(stagingDirectory))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);
        if (Directory.Exists(legacyProfiles))
        {
            foreach (var file in Directory.EnumerateFiles(legacyProfiles))
            {
                CopyWritable(file, Path.Combine(stagingDirectory, Path.GetFileName(file)));
            }
        }

        // A preferences file already in place came whole from an earlier start that stopped before the profiles were
        // renamed: it is kept.
        var legacyPreferences = Path.Combine(legacyDataDirectory, PreferencesFileName);
        var preferences = Path.Combine(dataDirectory, PreferencesFileName);
        if (File.Exists(legacyPreferences) && !File.Exists(preferences))
        {
            var stagingPreferences = preferences + MigrationSuffix;
            if (File.Exists(stagingPreferences))
            {
                File.SetAttributes(stagingPreferences, FileAttributes.Normal);
            }

            CopyWritable(legacyPreferences, stagingPreferences);
            File.Move(stagingPreferences, preferences);
        }

        Directory.Move(stagingDirectory, profileDirectory);
    }

    // File.Copy keeps the read-only attribute: the application could not save a profile copied that way, and a copy
    // stopped half-way would leave a staging file that the next start cannot delete.
    private static void CopyWritable(string source, string destination)
    {
        File.Copy(source, destination, overwrite: true);
        File.SetAttributes(destination, FileAttributes.Normal);
    }
}
