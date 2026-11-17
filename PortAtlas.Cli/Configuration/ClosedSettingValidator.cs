namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Checks the settings of a <c>portatlas.json</c> file that take one value of a closed set.
/// </summary>
/// <remarks>
/// The map reads any value it does not know as the default, so a mistyped <c>"MapDirection": "outbound"</c> would draw
/// every flow without a word, where <c>--direction outbound</c> is refused.
/// </remarks>
internal static class ClosedSettingValidator
{
    /// <summary>
    /// Refuses a closed setting holding a value that is neither one of its values nor a spelling the map still reads.
    /// </summary>
    /// <param name="config">Configuration read from the file.</param>
    /// <param name="configPath">Path of the file, placed at the start of the message.</param>
    /// <exception cref="ConfigurationFileException">When a setting holds another value; the message names the key, the value and the accepted values.</exception>
    public static void Validate(AppConfiguration config, string configPath)
    {
        ArgumentNullException.ThrowIfNull(config);

        Check(configPath, nameof(AppConfiguration.MapDirection), config.MapDirection, ClosedSettingValues.MapDirection);
        Check(configPath, nameof(AppConfiguration.MapEdgeLabels), config.MapEdgeLabels, ClosedSettingValues.MapEdgeLabels);
        Check(configPath, nameof(AppConfiguration.DrawioTheme), config.DrawioTheme, ClosedSettingValues.DrawioTheme);

        // The Draw.io diagram reads this key without its surrounding spaces, so "off " keeps working as it did.
        Check(configPath, nameof(AppConfiguration.DrawioSubnets), config.DrawioSubnets, ClosedSettingValues.DrawioSubnets,
            value => ClosedSettingValues.DrawioSubnets.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase));

        // These four keys and the Draw.io edge mode also take the spellings of earlier versions ("force", "cluster"),
        // so the map's own reading decides, and the message lists only the values of the options.
        Check(configPath, nameof(AppConfiguration.MapLayoutMode), config.MapLayoutMode, ClosedSettingValues.MapLayoutMode,
            value => TopologyMapConfigurationBuilder.ParseLayoutMode(value) is not null);
        Check(configPath, nameof(AppConfiguration.MapSortMethod), config.MapSortMethod, ClosedSettingValues.MapSortMethod,
            value => TopologyMapConfigurationBuilder.ParseSortMethod(value) is not null);
        Check(configPath, nameof(AppConfiguration.MapRoutingMode), config.MapRoutingMode, ClosedSettingValues.MapRoutingMode,
            value => TopologyMapConfigurationBuilder.ParseRoutingMode(value) is not null);
        Check(configPath, nameof(AppConfiguration.MapSpacingProfile), config.MapSpacingProfile, ClosedSettingValues.MapSpacingProfile,
            value => TopologyMapConfigurationBuilder.ParseSpacingProfile(value) is not null);
        Check(configPath, nameof(AppConfiguration.DrawioEdgeMode), config.DrawioEdgeMode, ClosedSettingValues.DrawioEdgeMode,
            value => TopologyMapConfigurationBuilder.ParseDrawioEdgeMode(value) is not null);
    }

    private static void Check(string configPath, string key, string value, IReadOnlyList<string> acceptedValues, Func<string, bool>? isRead = null)
    {
        var accepted = isRead?.Invoke(value) ?? acceptedValues.Contains(value, StringComparer.OrdinalIgnoreCase);
        if (accepted)
        {
            return;
        }

        throw new ConfigurationFileException(
            $"{configPath}: \"{key}\" is \"{value}\", which the setting does not take. Accepted values: {string.Join(", ", acceptedValues)}.");
    }
}
