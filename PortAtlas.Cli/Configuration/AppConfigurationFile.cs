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

    private const string DefaultsNotApplied = "Correct the file, or remove it to run on the default settings.";

    /// <summary>
    /// Loads the configuration file.
    /// </summary>
    /// <param name="path">
    /// Requested path, or <see langword="null"/> for <see cref="DefaultPath"/>. A relative path missing from the working
    /// directory is looked up next to the executable, where the release archives place the template.
    /// </param>
    /// <returns>The configuration and the path it was read from; default values when the file is missing.</returns>
    /// <exception cref="ConfigurationFileException">
    /// When the file exists but is locked, denied to the user, not valid JSON, holds a value of the wrong type or
    /// <c>null</c>, or still sets the removed <c>IsolatedMode</c> key to <see langword="true"/>.
    /// </exception>
    public static LoadedAppConfiguration Load(string? path)
    {
        var configPath = string.IsNullOrWhiteSpace(path) ? DefaultPath : path;
        if (!File.Exists(configPath))
        {
            var besideExecutable = Path.Combine(AppContext.BaseDirectory, configPath);
            if (File.Exists(besideExecutable))
            {
                configPath = besideExecutable;
            }
        }

        // Tokens are expanded here so that every command reads the same file names the audit writes.
        return new LoadedAppConfiguration(ResolveOutputPathTokens(Read(configPath)), configPath);
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

    private static AppConfiguration Read(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new AppConfiguration();
        }

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
            RejectRemovedIsolatedMode(json, configPath);
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
    private static void RejectRemovedIsolatedMode(string json, string configPath)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("IsolatedMode", out var isolatedMode))
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
