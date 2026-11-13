using PortAtlas.Core.Filtering;
using TopologyForge.Models;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Port and address filters of the topology map and of the rule matrix, read from the <c>Map*</c> lists of the
/// configuration once the command line has been applied to them.
/// </summary>
/// <param name="IncludedPorts">Service ports to keep; empty keeps them all.</param>
/// <param name="ExcludedPorts">Service ports to remove.</param>
/// <param name="IncludedIps">Addresses and subnets to keep; empty keeps them all.</param>
/// <param name="ExcludedIps">Addresses and subnets to remove.</param>
public sealed record FlowFilters(
    IReadOnlyList<NetworkPortRange> IncludedPorts,
    IReadOnlyList<NetworkPortRange> ExcludedPorts,
    IReadOnlyList<CidrRange> IncludedIps,
    IReadOnlyList<CidrRange> ExcludedIps)
{
    private static readonly char[] PortSeparators = [',', ';'];

    /// <summary>
    /// Reads the four lists. A subnet written with host bits is kept as its network; any other value that is not a
    /// port, a port range, an address or a subnet stops the reading.
    /// </summary>
    /// <param name="config">Configuration holding the lists as written by the operator.</param>
    /// <returns>The validated filters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    /// <exception cref="FlowFilterFormatException">A value cannot be read; the message names the option, the key and the value.</exception>
    public static FlowFilters Parse(AppConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        return new FlowFilters(
            ParsePorts(config.MapIncludedPorts, "--include-ports", nameof(AppConfiguration.MapIncludedPorts)),
            ParsePorts(config.MapExcludedPorts, "--exclude-ports", nameof(AppConfiguration.MapExcludedPorts)),
            ParseAddresses(config.MapIncludedIps, "--include-ips", nameof(AppConfiguration.MapIncludedIps)),
            ParseAddresses(config.MapExcludedIps, "--exclude-ips", nameof(AppConfiguration.MapExcludedIps)));
    }

    private static List<NetworkPortRange> ParsePorts(IEnumerable<string> values, string option, string key)
    {
        var ranges = new List<NetworkPortRange>();

        // One entry of the configuration list can hold several ports ("80, 443"), as one value of the option does.
        foreach (var token in values.SelectMany(value => value.Split(PortSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            try
            {
                ranges.Add(NetworkPortRange.Parse(token));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new FlowFilterFormatException(
                    $"{option} (configuration key {key}): '{token}' is not a port or a port range. Expected a port between 0 and 65535 (443) or a range in increasing order (8000-8080).",
                    ex);
            }
        }

        return ranges;
    }

    private static List<CidrRange> ParseAddresses(IEnumerable<string> values, string option, string key)
    {
        var ranges = new List<CidrRange>();

        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!CidrRange.TryParse(value, out var range))
            {
                throw new FlowFilterFormatException(
                    $"{option} (configuration key {key}): '{value.Trim()}' is not an IP address or a CIDR subnet. Expected an address (192.168.1.50) or a subnet (192.168.1.0/24).");
            }

            ranges.Add(range);
        }

        return ranges;
    }
}
