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
                var json = File.ReadAllText(_filePath);
                using var doc = JsonDocument.Parse(json);
                var result = new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return result;
                }

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
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // Tolerated: On file corruption or transient locking, start with a clean in-memory cache
                return new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
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
