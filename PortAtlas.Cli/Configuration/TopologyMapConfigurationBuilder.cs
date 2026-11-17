using TopologyForge.Export.Drawio.Adapters;
using TopologyForge.Export.Drawio.Styling;
using TopologyForge.Filtering;
using TopologyForge.Models;
using TopologyForge.Rendering;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Builds topology filtering and rendering options from application configuration and CLI arguments.
/// </summary>
public static class TopologyMapConfigurationBuilder
{
    /// <summary>
    /// Converts global configuration into topology filter options for <see cref="TopologyForge"/>.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <returns>Configured topology filter options.</returns>
    public static TopologyFilterOptions BuildFilterOptions(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var filters = FlowFilters.Parse(config);
        var excludedPorts = new List<NetworkPortRange>(filters.ExcludedPorts);

        foreach (var port in config.ExcludedLocalPorts.Concat(config.ExcludedRemotePorts))
        {
            if (port is > 0 and <= 65535)
            {
                excludedPorts.Add(NetworkPortRange.Single(port));
            }
        }

        var excludedIps = filters.ExcludedIps.Select(range => range.ToString()).ToList();
        foreach (var ip in config.ExcludedRemoteIps)
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                excludedIps.Add(ip);
            }
        }

        var includeInbound = !config.MapListenersOnly && !string.Equals(config.MapDirection, "out", StringComparison.OrdinalIgnoreCase);
        var includeOutbound = !config.MapListenersOnly && !string.Equals(config.MapDirection, "in", StringComparison.OrdinalIgnoreCase);

        return new TopologyFilterOptions
        {
            ExcludeEphemeralPorts = config.MapExcludeEphemeralPorts,
            IncludeListeners = true,
            IncludeInboundFlows = includeInbound,
            IncludeOutboundFlows = includeOutbound,
            IncludedPorts = filters.IncludedPorts,
            ExcludedPorts = excludedPorts,
            IncludedHostPatterns = config.MapIncludedHosts,
            ExcludedHostPatterns = config.MapExcludedHosts,
            IncludedIpRanges = filters.IncludedIps.Select(range => range.ToString()).ToList(),
            ExcludedIpRanges = excludedIps,
            IncludedProcesses = config.MapIncludedProcesses,
            ExcludedProcesses = config.MapExcludedProcesses
        };
    }

    /// <summary>
    /// Converts global configuration into layout, sorting, and routing options for <see cref="TopologyForge"/>.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <returns>Configured topology rendering options.</returns>
    public static TopologyRenderingOptions BuildRenderingOptions(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        // The configuration file is checked when it is read (ClosedSettingValidator) and the options take closed sets,
        // so a default below only stands in for a value of a configuration built in code.
        var layoutMode = ParseLayoutMode(config.MapLayoutMode) ?? TopologyLayoutMode.Layered;
        var routingMode = ParseRoutingMode(config.MapRoutingMode) ?? TopologyEdgeRoutingMode.BezierCurved;

        var edgeLabelMode = config.MapEdgeLabels.Equals("none", StringComparison.OrdinalIgnoreCase)
            ? TopologyEdgeLabelMode.None
            : TopologyEdgeLabelMode.Ports;

        var spacingProfile = ParseSpacingProfile(config.MapSpacingProfile) ?? TopologySpacingProfile.Balanced;

        var initialIncludePorts = config.MapIncludedPorts.Count > 0 ? string.Join(", ", config.MapIncludedPorts) : string.Empty;
        var initialExcludePorts = config.MapExcludedPorts.Count > 0 ? string.Join(", ", config.MapExcludedPorts) : string.Empty;
        var initialExcludeHosts = config.MapExcludedHosts.Count > 0 ? string.Join(", ", config.MapExcludedHosts) : string.Empty;
        var initialDirection = config.MapListenersOnly
            ? "IN"
            : (string.Equals(config.MapDirection, "out", StringComparison.OrdinalIgnoreCase)
                ? "OUT"
                : (string.Equals(config.MapDirection, "in", StringComparison.OrdinalIgnoreCase)
                    ? "IN"
                    : "all"));

        return new TopologyRenderingOptions
        {
            DefaultLayoutMode = layoutMode,
            DefaultSortMethod = ParseSortMethod(config.MapSortMethod) ?? TopologyNodeSortMethod.BarycentricSugiyama,
            DefaultRoutingMode = routingMode,
            DefaultSpacingProfile = spacingProfile,
            DefaultEdgeLabelMode = edgeLabelMode,
            InitialIncludePorts = initialIncludePorts,
            InitialExcludePorts = initialExcludePorts,
            InitialExcludeHosts = initialExcludeHosts,
            InitialDirection = initialDirection,
            CliCommandFormat = MapCommandFormat
        };
    }

    // Syntax of the "map" command (ADR-0001), copied from the HTML map to reproduce the displayed view.
    private static readonly TopologyCliCommandFormat MapCommandFormat = new()
    {
        Prefix = "PortAtlas map",
        LayoutOption = "--layout",
        SortOption = "--sort",
        SugiyamaSortValue = "barycentric",
        RoutingOption = "--routing",
        SpacingOption = "--spacing",
        EdgeLabelsOption = "--edge-labels"
    };

    /// <summary>
    /// Converts the configured Draw.io edge mode (<c>adaptive</c>, <c>direction</c>, <c>pair</c>, <c>none</c>) into the exporter setting.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <returns>The edge aggregation mode; <see cref="EdgeAggregationMode.AdaptiveThreshold"/> for any other value.</returns>
    public static EdgeAggregationMode BuildDrawioEdgeMode(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return ParseDrawioEdgeMode(config.DrawioEdgeMode) ?? EdgeAggregationMode.AdaptiveThreshold;
    }

    /// <summary>
    /// Converts the configured Draw.io theme (<c>dark</c>, <c>light</c>) into the exporter theme.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <returns><see cref="DiagramTheme.EnterpriseLight"/> for <c>light</c>, otherwise <see cref="DiagramTheme.DarkModern"/>.</returns>
    public static DiagramTheme BuildDrawioTheme(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return config.DrawioTheme.Equals("light", StringComparison.OrdinalIgnoreCase)
            ? DiagramTheme.EnterpriseLight
            : DiagramTheme.DarkModern;
    }

    /// <summary>
    /// Converts the configured Draw.io subnet setting (<c>on</c>, <c>off</c>) and the map sort method into the exporter layout options.
    /// </summary>
    /// <param name="config">Application configuration.</param>
    /// <returns>Layout options that group the servers by subnet unless the setting is <c>off</c>.</returns>
    public static TopologyDrawioLayoutOptions BuildDrawioLayoutOptions(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new TopologyDrawioLayoutOptions
        {
            GroupBySubnets = !config.DrawioSubnets.Trim().Equals("off", StringComparison.OrdinalIgnoreCase),
            SortMethod = ParseSortMethod(config.MapSortMethod) ?? TopologyNodeSortMethod.BarycentricSugiyama
        };
    }

    // Each reading takes the values of the matching option, without case, and the spellings of earlier versions that
    // configuration files may still hold; any other value gives null.
    internal static TopologyLayoutMode? ParseLayoutMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "LAYERED" => TopologyLayoutMode.Layered,
        "ORGANIC" or "FORCE" or "FORCEDIRECTED" => TopologyLayoutMode.ForceDirectedOrganic,
        "CIRCULAR" or "RING" => TopologyLayoutMode.Circular,
        "GRID" or "MATRIX" => TopologyLayoutMode.Grid,
        "TREE" or "TOPDOWN" or "ARBRE" => TopologyLayoutMode.Tree,
        "HORIZONTALTREE" or "HORIZONTAL-TREE" or "HORIZONTAL" or "LEFTTORIGHT" => TopologyLayoutMode.HorizontalTree,
        _ => null
    };

    internal static TopologyEdgeRoutingMode? ParseRoutingMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "BEZIER" => TopologyEdgeRoutingMode.BezierCurved,
        "ORTHOGONAL" or "ORTHOGONALCORRIDORS" => TopologyEdgeRoutingMode.OrthogonalCorridors,
        "STRAIGHT" or "DIRECT" or "DIRECTSTRAIGHT" => TopologyEdgeRoutingMode.DirectStraight,
        "BUS" => TopologyEdgeRoutingMode.Bus,
        _ => null
    };

    internal static TopologySpacingProfile? ParseSpacingProfile(string value) => value.Trim().ToUpperInvariant() switch
    {
        "BALANCED" => TopologySpacingProfile.Balanced,
        "COMPACT" => TopologySpacingProfile.Compact,
        "SPACIOUS" or "LARGE" or "WIDE" => TopologySpacingProfile.Spacious,
        _ => null
    };

    internal static EdgeAggregationMode? ParseDrawioEdgeMode(string value) => value.Trim().ToUpperInvariant() switch
    {
        "ADAPTIVE" => EdgeAggregationMode.AdaptiveThreshold,
        "DIRECTION" => EdgeAggregationMode.ByDirection,
        "PAIR" => EdgeAggregationMode.ByServerPair,
        "NONE" => EdgeAggregationMode.None,
        _ => null
    };

    internal static TopologyNodeSortMethod? ParseSortMethod(string value) => value.Trim().ToUpperInvariant() switch
    {
        "BARYCENTRIC" => TopologyNodeSortMethod.BarycentricSugiyama,
        "DEGREE" or "CONNECTIONDEGREE" => TopologyNodeSortMethod.ConnectionDegree,
        "ALPHA" or "ALPHABETICAL" or "NAME" => TopologyNodeSortMethod.Alphabetical,
        "TIERS" or "TIER" or "FUNCTIONALTIER" => TopologyNodeSortMethod.FunctionalTier,
        "CLUSTER" or "SUBNETCLUSTER" or "ZONE" or "ZONES" or "SUBNET" => TopologyNodeSortMethod.SubnetCluster,
        "IP" or "SUBNETIP" => TopologyNodeSortMethod.SubnetIp,
        _ => null
    };
}
