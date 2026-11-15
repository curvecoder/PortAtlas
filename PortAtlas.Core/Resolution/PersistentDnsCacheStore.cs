using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FileForge.IO;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Persistence manager storing reverse DNS and IP organization cache records into a JSON file.
/// </summary>
/// <param name="filePath">Target JSON cache file path.</param>
public sealed class PersistentDnsCacheStore(string filePath)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    private readonly object _syncLock = new();

    /// <summary>
    /// Loads persistent cache entries from the JSON file with backward compatibility for legacy flat structures.
    /// </summary>
    /// <returns>Dictionary of known IP-to-entry mappings, or an empty dictionary if missing or unreadable.</returns>
    public Dictionary<string, DnsCacheEntry> Load()
    {
        lock (_syncLock)
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return ReadEntries(File.ReadAllText(_filePath)) ?? new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Tolerated: On file corruption or transient locking, start with a clean in-memory cache
                return new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Loads the cache entries of a file that the caller is about to rewrite, refusing a file that cannot be read.
    /// </summary>
    /// <returns>Dictionary of known IP-to-entry mappings, or an empty dictionary if the file does not exist.</returns>
    /// <exception cref="DnsCacheUnreadableException">When the file exists but cannot be read, is not valid JSON, or its
    /// root is not a JSON object; the message names the file and the error.</exception>
    public Dictionary<string, DnsCacheEntry> LoadOrThrow()
    {
        lock (_syncLock)
        {
            if (!File.Exists(_filePath))
            {
                return new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                return ReadEntries(File.ReadAllText(_filePath))
                    ?? throw new DnsCacheUnreadableException($"The DNS cache '{_filePath}' cannot be read: its root is not a JSON object.");
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                throw new DnsCacheUnreadableException($"The DNS cache '{_filePath}' cannot be read: {ex.Message}", ex);
            }
        }
    }

    // Returns null when the root is not an object: the file holds no cache that this version can read.
    private static Dictionary<string, DnsCacheEntry>? ReadEntries(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var ipKey = prop.Name;

            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                // Backward-compatible flat format: { "IP": "HostName" }
                var name = prop.Value.GetString() ?? string.Empty;
                result[ipKey] = new DnsCacheEntry
                {
                    HostName = name,
                    Organization = name.Contains(' ') ? name : null,
                    Source = name.Contains('.') ? "DNS" : (!string.IsNullOrWhiteSpace(name) ? "RDAP" : "None"),
                    ResolvedAtUtc = DateTimeOffset.UtcNow
                };
            }
            else if (prop.Value.ValueKind == JsonValueKind.Object &&
                     TryReadStructuredEntry(prop.Value, out var entry))
            {
                result[ipKey] = entry;
            }
        }

        return result;
    }

    // An entry edited by hand or written by another version can hold a value of the wrong type: only that entry is
    // dropped, and its address is resolved again, instead of losing the whole cache. A JSON null is accepted by the
    // deserializer for the non-nullable HostName and Source, so it is rejected here.
    private static bool TryReadStructuredEntry(JsonElement element, [NotNullWhen(true)] out DnsCacheEntry? entry)
    {
        try
        {
            var read = element.Deserialize<DnsCacheEntry>(JsonOptions);
            if (read is { HostName: not null, Source: not null })
            {
                entry = read;
                return true;
            }
        }
        catch (JsonException)
        {
            // A mistyped value invalidates only this entry: Load leaves it out and the address is resolved again at
            // the next lookup.
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Saves cache entries to disk asynchronously.
    /// </summary>
    /// <param name="cache">Dictionary of IP mappings to persist.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    public async Task SaveAsync(IReadOnlyDictionary<string, DnsCacheEntry> cache, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);

        string json;
        lock (_syncLock)
        {
            json = JsonSerializer.Serialize(cache, JsonOptions);
        }

        try
        {
            await AtomicFileWriter.WriteAllTextAsync(_filePath, json, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // A file held open by another process must not stop the run: the cache on disk keeps its previous
            // version, and the next save writes every entry again.
        }
    }

    /// <summary>
    /// Backward-compatible overload saving a simple IP-to-hostname dictionary.
    /// </summary>
    /// <param name="cache">Simple IP mappings dictionary to persist.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    public Task SaveAsync(IReadOnlyDictionary<string, string> cache, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cache);

        var converted = cache.ToDictionary(
            kvp => kvp.Key,
            kvp => new DnsCacheEntry
            {
                HostName = kvp.Value,
                Source = kvp.Value.Contains('.') ? "DNS" : (!string.IsNullOrWhiteSpace(kvp.Value) ? "RDAP" : "None")
            },
            StringComparer.OrdinalIgnoreCase);

        return SaveAsync(converted, cancellationToken);
    }

    /// <summary>
    /// Saves cache entries to disk synchronously (e.g. during application termination).
    /// </summary>
    /// <param name="cache">Dictionary of IP mappings to persist.</param>
    public void Save(IReadOnlyDictionary<string, DnsCacheEntry> cache)
    {
        ArgumentNullException.ThrowIfNull(cache);

        string json;
        lock (_syncLock)
        {
            json = JsonSerializer.Serialize(cache, JsonOptions);
        }

        try
        {
            AtomicFileWriter.WriteAllText(_filePath, json);
        }
        catch (IOException)
        {
            // Same tolerance as SaveAsync: the cache on disk keeps its previous version.
        }
    }

    /// <summary>
    /// Backward-compatible synchronous overload saving a simple IP-to-hostname dictionary.
    /// </summary>
    /// <param name="cache">Simple IP mappings dictionary to persist.</param>
    public void Save(IReadOnlyDictionary<string, string> cache)
    {
        ArgumentNullException.ThrowIfNull(cache);

        var converted = cache.ToDictionary(
            kvp => kvp.Key,
            kvp => new DnsCacheEntry
            {
                HostName = kvp.Value,
                Source = kvp.Value.Contains('.') ? "DNS" : (!string.IsNullOrWhiteSpace(kvp.Value) ? "RDAP" : "None")
            },
            StringComparer.OrdinalIgnoreCase);

        Save(converted);
    }
}
