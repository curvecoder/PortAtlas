using System.Text.Json;
using System.Text.Json.Serialization;
using FileForge.IO;

namespace PortAtlas.Gui.Core.Profiles;

/// <summary>
/// Reads and writes <see cref="UserPreferences"/> in <c>preferences.json</c>, next to the profile directory.
/// </summary>
/// <param name="directoryProvider">Supplies the profile directory, whose parent holds the preferences file.</param>
public sealed class UserPreferencesStore(IProfileDirectoryProvider directoryProvider)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Reads the preferences.
    /// </summary>
    /// <returns>The stored preferences, or the defaults when the file is missing or unreadable.</returns>
    public UserPreferences Load()
    {
        var path = PreferencesPath();
        if (!File.Exists(path))
        {
            return new UserPreferences();
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<UserPreferences>(stream, SerializerOptions) ?? new UserPreferences();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preferences are a convenience: a damaged, locked or denied file must not stop the GUI from starting, so the defaults apply.
            return new UserPreferences();
        }
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the stored preferences and writes the result.
    /// </summary>
    /// <param name="change">Produces the new preferences from the current ones.</param>
    /// <param name="cancellationToken">Token that cancels the write.</param>
    /// <returns>A task that ends when the preferences are stored.</returns>
    public Task UpdateAsync(Func<UserPreferences, UserPreferences> change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        var preferences = change(Load());
        return AtomicFileWriter.WriteAsync(
            PreferencesPath(),
            (stream, token) => JsonSerializer.SerializeAsync(stream, preferences, SerializerOptions, token),
            cancellationToken);
    }

    // The profile directory only lists *.json files as profiles, so the preferences live one level above it.
    private string PreferencesPath()
    {
        var profileDirectory = directoryProvider.GetProfileDirectory().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(profileDirectory) ?? profileDirectory;
        return Path.Combine(parent, "preferences.json");
    }
}
