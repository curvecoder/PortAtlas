namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Values of the settings that take one value of a closed set, shared by their command line options and by the check of
/// the configuration file, so that an option and its key accept the same values and their messages list the same ones.
/// </summary>
internal static class ClosedSettingValues
{
    public static IReadOnlyList<string> MapDirection { get; } = ["all", "in", "out"];

    public static IReadOnlyList<string> MapLayoutMode { get; } = ["layered", "tree", "horizontal-tree", "organic", "circular", "grid"];

    public static IReadOnlyList<string> MapSortMethod { get; } = ["barycentric", "tiers", "subnet", "degree", "alpha", "ip"];

    public static IReadOnlyList<string> MapRoutingMode { get; } = ["bezier", "orthogonal", "straight", "bus"];

    public static IReadOnlyList<string> MapEdgeLabels { get; } = ["ports", "none"];

    public static IReadOnlyList<string> MapSpacingProfile { get; } = ["compact", "balanced", "spacious"];

    public static IReadOnlyList<string> DrawioEdgeMode { get; } = ["adaptive", "direction", "pair", "none"];

    public static IReadOnlyList<string> DrawioSubnets { get; } = ["on", "off"];

    public static IReadOnlyList<string> DrawioTheme { get; } = ["dark", "light"];
}
