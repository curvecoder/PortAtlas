using System.Collections;
using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Checks the keys of a <c>portatlas.json</c> document against the properties of <see cref="AppConfiguration"/>.
/// </summary>
/// <remarks>
/// The deserializer skips a key it does not know and stores a <c>null</c> it is given. A misspelled
/// <c>ResolveIpOwners</c> set to <see langword="false"/> would then leave the RDAP queries on, and a list set to
/// <c>null</c> would lose its built-in exclusions, both without a word to the operator.
/// </remarks>
internal static class ConfigurationKeyValidator
{
    private static readonly FrozenDictionary<string, KeyRule> RuleByKey = ReadKeysOfAppConfiguration();

    /// <summary>
    /// Refuses a document holding an unknown key, a <c>null</c> where the setting needs a value, or a <c>null</c>
    /// entry in a list or in the host aliases.
    /// </summary>
    /// <param name="root">Root element of the configuration document.</param>
    /// <param name="configPath">Path of the file, placed at the start of the message.</param>
    /// <exception cref="ConfigurationFileException">When a key breaks one of these rules; the message names it.</exception>
    public static void Validate(JsonElement root, string configPath)
    {
        // A document that is not an object has no key to check; the deserializer reports it.
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // AppConfigurationFile has already ruled on the removed key, which is tolerated when it is false.
        foreach (var key in root.EnumerateObject().Where(key => !key.NameEquals(AppConfigurationFile.RemovedIsolatedModeKey)))
        {
            RejectFaultyKey(key, configPath);
        }
    }

    private static void RejectFaultyKey(JsonProperty key, string configPath)
    {
        if (!RuleByKey.TryGetValue(key.Name, out var rule))
        {
            throw new ConfigurationFileException($"{configPath}: {DescribeUnknownKey(key.Name)}");
        }

        if (key.Value.ValueKind == JsonValueKind.Null && !rule.AcceptsNull)
        {
            throw new ConfigurationFileException(
                $"{configPath}: \"{key.Name}\" is null. Give it a value, or remove the key to keep its default.");
        }

        // A key that takes a single value is left to the deserializer, which reports an array or an object given
        // in its place as a value of the wrong type.
        if (rule.HoldsEntries && HoldsNullEntry(key.Value))
        {
            throw new ConfigurationFileException(
                $"{configPath}: \"{key.Name}\" holds a null entry. Give the entry a value or remove it.");
        }
    }

    private static string DescribeUnknownKey(string name)
    {
        // Only the exact spelling of the removed key is ruled on by AppConfigurationFile; in another case it would be
        // read as a plain typing mistake, and the operator would not learn which setting replaces it.
        if (string.Equals(name, AppConfigurationFile.RemovedIsolatedModeKey, StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{name}\" is not a configuration key: {AppConfigurationFile.RemovedIsolatedModeKey} was removed in 2.0.0. " +
                "Set \"ResolveIpOwners\": false to keep RDAP queries off, then remove the key.";
        }

        var sameNameInAnotherCase = RuleByKey.Keys
            .FirstOrDefault(known => string.Equals(known, name, StringComparison.OrdinalIgnoreCase));

        return sameNameInAnotherCase is null
            ? $"\"{name}\" is not a configuration key. Correct its name or remove it."
            : $"\"{name}\" is not a configuration key: names are case-sensitive, write \"{sameNameInAnotherCase}\".";
    }

    // No list and no alias of AppConfiguration takes a null entry, so the check is the same for all of them.
    private static bool HoldsNullEntry(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.EnumerateArray().Any(entry => entry.ValueKind == JsonValueKind.Null),
        JsonValueKind.Object => value.EnumerateObject().Any(entry => entry.Value.ValueKind == JsonValueKind.Null),
        _ => false
    };

    // Read from the type itself, so that a setting added to AppConfiguration is accepted without a second list to
    // keep in step, and a nullable one (ReloadHistoryLimit today) keeps accepting null.
    private static FrozenDictionary<string, KeyRule> ReadKeysOfAppConfiguration()
    {
        var nullability = new NullabilityInfoContext();
        return typeof(AppConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToFrozenDictionary(
                property => property.Name,
                property => new KeyRule(
                    AcceptsNull: nullability.Create(property).WriteState == NullabilityState.Nullable,
                    HoldsEntries: property.PropertyType != typeof(string) &&
                        typeof(IEnumerable).IsAssignableFrom(property.PropertyType)),
                StringComparer.Ordinal);
    }

    private sealed record KeyRule(bool AcceptsNull, bool HoldsEntries);
}
