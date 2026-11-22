using System.Diagnostics.CodeAnalysis;
using PortAtlas.Core.Export;
using TopologyForge.Filtering;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Reads the address groups of <c>export rules</c> from the <c>AddressGroups</c> key and the <c>--address-group</c>
/// option.
/// </summary>
internal static class AddressGroupParser
{
    private const string OptionName = "--address-group";

    /// <summary>
    /// Reads one <c>--address-group</c> value, <c>name=range</c>.
    /// </summary>
    /// <param name="value">Value given to the option.</param>
    /// <param name="range">The group and its range, when the value holds both.</param>
    /// <param name="error">Why the value was refused, naming it, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the value names a group and an address or a CIDR subnet.</returns>
    public static bool TryParseArgument(string value, [NotNullWhen(true)] out AddressGroupRange? range, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(value);

        var separatorIndex = value.IndexOf('=', StringComparison.Ordinal);
        var name = separatorIndex < 0 ? string.Empty : value[..separatorIndex].Trim();
        var rangeText = separatorIndex < 0 ? string.Empty : value[(separatorIndex + 1)..];
        if (name.Length == 0 || !IpAddressRange.TryParse(rangeText, out var parsed))
        {
            range = null;
            error = $"'{value}' is not accepted by {OptionName}: write name=range, for example lab=192.168.10.0/24 or nas=192.168.1.20.";
            return false;
        }

        range = new AddressGroupRange(name, parsed);
        error = null;
        return true;
    }

    /// <summary>
    /// Builds the address groups of the configuration file and of the command line.
    /// </summary>
    /// <param name="configured">Ranges by group name, from the <c>AddressGroups</c> key.</param>
    /// <param name="optionValues">Values of <c>--address-group</c>, already checked by the option's validator.</param>
    /// <param name="directory">The address groups, when every range can be read and belongs to one group.</param>
    /// <param name="error">The message naming the value that cannot be read or the range given to two groups.</param>
    /// <returns><see langword="true"/> when the groups were built.</returns>
    /// <remarks>
    /// The option adds ranges to the groups of the file: a group named in both, whatever the case of its name, holds the
    /// ranges of both under the name written first.
    /// </remarks>
    public static bool TryBuild(
        IReadOnlyDictionary<string, List<string>> configured,
        IEnumerable<string> optionValues,
        [NotNullWhen(true)] out AddressGroupDirectory? directory,
        [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(configured);
        ArgumentNullException.ThrowIfNull(optionValues);

        directory = null;
        var ranges = new List<AddressGroupRange>();
        foreach (var (name, values) in configured)
        {
            foreach (var value in values)
            {
                if (string.IsNullOrWhiteSpace(name) || !IpAddressRange.TryParse(value, out var range))
                {
                    var entry = value is null ? "a null entry" : $"'{value.Trim()}'";
                    error = $"{OptionName} (configuration key {nameof(AppConfiguration.AddressGroups)}): {entry} in the group '{name}' is not an IP address or a CIDR subnet of a named group. Expected an address (192.168.1.50) or a subnet (192.168.1.0/24) under a group name.";
                    return false;
                }

                ranges.Add(new AddressGroupRange(name.Trim(), range));
            }
        }

        foreach (var value in optionValues)
        {
            if (TryParseArgument(value, out var range, out var argumentError))
            {
                ranges.Add(range);
                continue;
            }

            error = argumentError;
            return false;
        }

        var spellingByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var named = ranges.ConvertAll(range => range with { GroupName = spellingByName.TryAdd(range.GroupName, range.GroupName) ? range.GroupName : spellingByName[range.GroupName] });
        return AddressGroupDirectory.TryCreate(named, out directory, out error);
    }
}
