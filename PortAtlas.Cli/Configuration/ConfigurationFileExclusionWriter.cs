using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FileForge.IO;

namespace PortAtlas.Cli.Configuration;

/// <summary>
/// Adds exclusions to the lists of a <c>portatlas.json</c> file and leaves every other key as the operator wrote it.
/// </summary>
/// <param name="filePath">Path of the configuration file; it is created when missing.</param>
internal sealed class ConfigurationFileExclusionWriter(string filePath)
{
    // The relaxed encoder keeps accented names and characters such as '+' readable; the default one rewrites them
    // as \uXXXX sequences, which is only needed for JSON embedded in HTML.
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));

    /// <summary>
    /// Writes the exclusions that the file does not list yet.
    /// </summary>
    /// <remarks>
    /// The file is read again on every call: the effective configuration of the run holds expanded file name tokens
    /// and command-line overrides that must never reach the file, and the operator may have edited the file since
    /// the audit started.
    /// </remarks>
    /// <param name="exclusions">Exclusions added from the dashboard since the audit started.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="exclusions"/> is null.</exception>
    /// <exception cref="JsonException">When the file is not a valid configuration document; it is left untouched.</exception>
    /// <exception cref="IOException">When the file cannot be read or written.</exception>
    /// <exception cref="UnauthorizedAccessException">When the file is read-only or access is denied.</exception>
    public void Add(DashboardExclusions exclusions)
    {
        ArgumentNullException.ThrowIfNull(exclusions);

        var document = ReadDocument();
        var builtIn = new AppConfiguration();

        AddMissingValues(document, nameof(AppConfiguration.ExcludedLocalPorts), builtIn.ExcludedLocalPorts, exclusions.LocalPorts, EqualityComparer<int>.Default, port => JsonValue.Create(port));
        AddMissingValues(document, nameof(AppConfiguration.ExcludedRemotePorts), builtIn.ExcludedRemotePorts, exclusions.RemotePorts, EqualityComparer<int>.Default, port => JsonValue.Create(port));
        AddMissingValues(document, nameof(AppConfiguration.ExcludedRemoteIps), builtIn.ExcludedRemoteIps, exclusions.RemoteIps, StringComparer.OrdinalIgnoreCase, ip => JsonValue.Create(ip));
        AddMissingValues(document, nameof(AppConfiguration.ExcludedExactNames), builtIn.ExcludedExactNames, exclusions.ExactNames, StringComparer.OrdinalIgnoreCase, name => JsonValue.Create(name));

        AtomicFileWriter.WriteAllText(_filePath, document.ToJsonString(WriteOptions));
    }

    private JsonObject ReadDocument()
    {
        if (!File.Exists(_filePath))
        {
            // A save interrupted between its renames can leave the file missing and its complete new version in
            // <file>.pending; starting from an empty document would drop every other setting. When the file exists,
            // a leftover .pending is older than the file (a refused save, perhaps followed by an edit by hand) and is
            // simply overwritten by this save, which carries every exclusion of the run again.
            AtomicFileWriter.RecoverInterruptedWrite(_filePath);
        }

        if (!File.Exists(_filePath))
        {
            return new JsonObject();
        }

        if (JsonNode.Parse(File.ReadAllText(_filePath)) is not JsonObject document)
        {
            throw new JsonException($"{_filePath} does not hold a JSON object.");
        }

        try
        {
            // JsonObject builds its key index on first access and only then reports a key written twice.
            _ = document.Count;
        }
        catch (ArgumentException ex)
        {
            throw new JsonException($"{_filePath} holds the same key twice.", ex);
        }

        return document;
    }

    private static void AddMissingValues<T>(
        JsonObject document,
        string listName,
        IReadOnlyList<T> builtInValues,
        IReadOnlyList<T> addedValues,
        IEqualityComparer<T> comparer,
        Func<T, JsonNode?> toNode)
    {
        if (addedValues.Count == 0)
        {
            return;
        }

        if (!document.TryGetPropertyValue(listName, out var listNode))
        {
            // A list written to the file replaces the built-in one at the next start, so a file without the key
            // must receive the built-in values along with the added ones.
            listNode = new JsonArray([.. builtInValues.Select(toNode)]);
            document[listName] = listNode;
        }

        if (listNode is not JsonArray list)
        {
            throw new JsonException($"{listName} is not a list.");
        }

        var listedValues = list.Deserialize<List<T>>() ?? [];
        foreach (var value in addedValues.Where(value => !listedValues.Contains(value, comparer)))
        {
            list.Add(toNode(value));
            listedValues.Add(value);
        }
    }
}
