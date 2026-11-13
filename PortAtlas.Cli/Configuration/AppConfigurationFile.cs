using System.Text.Json;
using PortAtlas.Core.Export;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Configuration read from a <c>portatlas.json</c> file, with the path it was read from.
/// </summary>
/// <param name="Configuration">Configuration values (defaults when the file is missing).</param>
/// <param name="FilePath">Path of the configuration file, used to save exclusions added from the dashboard.</param>
public sealed record LoadedAppConfiguration(AppConfiguration Configuration, string FilePath);

/// <summary>
/// Reads the JSON configuration file and resolves the file name tokens of its output paths.
/// </summary>
public static class AppConfigurationFile
{
    /// <summary>
    /// File name read when <c>--config</c> is not given.
    /// </summary>
    public const string DefaultPath = "portatlas.json";

    internal const string RemovedIsolatedModeKey = "IsolatedMode";

    private const string DefaultsNotApplied = "Correct the file, or remove it to run on the default settings.";

    /// <summary>
    /// Loads the configuration file.
    /// </summary>
    /// <param name="path">
    /// Requested path, or <see langword="null"/> for <see cref="DefaultPath"/>. A relative path missing from the working
    /// directory is looked up next to the executable, where the release archives place the template.
    /// </param>
    /// <returns>
    /// The configuration and the path it was read from; default values when no path is requested and
    /// <see cref="DefaultPath"/> is missing from both directories.
    /// </returns>
    /// <exception cref="ConfigurationFileException">
    /// When a requested path is empty or missing from both directories, or when the file exists but is locked, denied
    /// to the user, not valid JSON, holds a key that is not a setting, a value of the wrong type, a <c>null</c> where
    /// a value is needed, or still sets the removed <c>IsolatedMode</c> key to <see langword="true"/>.
    /// </exception>
    public static LoadedAppConfiguration Load(string? path) =>
        Load(path, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

    // The two directories are parameters so that a test chooses what each one holds without changing the current
    // directory of the test process, which the other tests share.
    internal static LoadedAppConfiguration Load(string? path, string workingDirectory, string executableDirectory)
    {
        // An empty value is what a script passes when the variable holding the path is not set. Reading it as "no
        // option" would run the command on portatlas.json or on the defaults in place of the file the script meant.
        if (path is not null && string.IsNullOrWhiteSpace(path))
        {
            throw new ConfigurationFileException(
                $"--config: the path of the configuration file is empty. Give a path, or remove the option to read {DefaultPath}.");
        }

        var inWorkingDirectory = Path.Combine(workingDirectory, path ?? DefaultPath);
        var besideExecutable = Path.Combine(executableDirectory, path ?? DefaultPath);

        var existingPath = new[] { inWorkingDirectory, besideExecutable }.FirstOrDefault(File.Exists);
        if (existingPath is not null)
        {
            // Tokens are expanded here so that every command reads the same file names the audit writes.
            return new LoadedAppConfiguration(ResolveOutputPathTokens(Read(existingPath)), existingPath);
        }

        // A path given on the command line states which file the operator wants. A mistyped one would otherwise start
        // the command on the built-in defaults, RDAP queries included, without a word.
        if (path is not null)
        {
            throw new ConfigurationFileException(DescribeMissingRequestedFile(path, workingDirectory, executableDirectory));
        }

        // Without the option, a missing portatlas.json is the plain first run: the defaults apply, and an exclusion
        // added from the dashboard creates the file in the working directory.
        return new LoadedAppConfiguration(ResolveOutputPathTokens(new AppConfiguration()), inWorkingDirectory);
    }

    /// <summary>
    /// Expands the <c>{ServerName}</c>, <c>{HostName}</c>, <c>{Date}</c> and <c>{Timestamp}</c> tokens of every output path.
    /// </summary>
    /// <param name="config">Configuration whose paths may contain tokens.</param>
    /// <returns>A copy of the configuration with expanded paths.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="config"/> is null.</exception>
    public static AppConfiguration ResolveOutputPathTokens(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config with
        {
            OutputCsvPath = OutputFilePathResolver.Resolve(config.OutputCsvPath),
            OutputNdjsonPath = OutputFilePathResolver.Resolve(config.OutputNdjsonPath),
            OutputHtmlMapPath = OutputFilePathResolver.Resolve(config.OutputHtmlMapPath),
            OutputDrawioPath = OutputFilePathResolver.Resolve(config.OutputDrawioPath),
            DnsCacheFilePath = OutputFilePathResolver.Resolve(config.DnsCacheFilePath)
        };
    }

    private static string DescribeMissingRequestedFile(string requestedPath, string workingDirectory, string executableDirectory)
    {
        // Path.Combine returns a rooted path as it is, so only a relative one was looked up in the two directories.
        var lookedIn = Path.IsPathRooted(requestedPath)
            ? string.Empty
            : $", neither in {workingDirectory} nor in {executableDirectory}";

        return $"{requestedPath}: the configuration file given by --config does not exist{lookedIn}. " +
            $"Correct the path, or remove the option to read {DefaultPath}.";
    }

    private static AppConfiguration Read(string configPath)
    {
        // A file that is present states what the operator wants. Starting on the built-in defaults in its place would
        // turn the RDAP queries back on (ResolveIpOwners defaults to true) on a server whose file switched them off,
        // so every failure to read it stops the command instead.
        string json;
        try
        {
            json = File.ReadAllText(configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationFileException(
                $"{configPath}: the configuration file cannot be read ({ex.Message}). {DefaultsNotApplied}", ex);
        }

        try
        {
            using (var document = JsonDocument.Parse(json))
            {
                RejectRemovedIsolatedMode(document.RootElement, configPath);
                ConfigurationKeyValidator.Validate(document.RootElement, configPath);
            }

            return JsonSerializer.Deserialize<AppConfiguration>(json)
                ?? throw new ConfigurationFileException(
                    $"{configPath}: the configuration file holds null instead of a JSON object. {DefaultsNotApplied}");
        }
        catch (JsonException ex)
        {
            throw new ConfigurationFileException(
                $"{configPath}: the configuration file is not valid JSON or holds a value of the wrong type ({ex.Message}). {DefaultsNotApplied}",
                ex);
        }
    }

    // IsolatedMode (removed in 2.0.0) kept RDAP queries off. Ignoring a true value would make an isolated server
    // start sending Internet queries after the upgrade, so the agent refuses to start until the key is migrated.
    private static void RejectRemovedIsolatedMode(JsonElement root, string configPath)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(RemovedIsolatedModeKey, out var isolatedMode))
        {
            return;
        }

        if (isolatedMode.ValueKind == JsonValueKind.True)
        {
            throw new ConfigurationFileException(
                $"{configPath}: the IsolatedMode key was removed in 2.0.0. Set \"ResolveIpOwners\": false to keep RDAP queries off, then remove IsolatedMode.");
        }

        Console.WriteLine($"Warning: {configPath}: the IsolatedMode key was removed in 2.0.0 and is ignored; remove it.");
    }
}
