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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Tolerated without a message: this load serves caches that are only read (resolve --cache), so a
                // corrupt, locked or access-denied file silently adds no names and the run goes on, with the other
                // caches and online lookups. Caches about to be rewritten, and the lookup diagnostic, use LoadOrThrow.
                return new Dictionary<string, DnsCacheEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Loads the cache entries of a file that the caller is about to rewrite, refusing a file that cannot be read.
    /// </summary>
    /// <returns>Dictionary of known IP-to-entry mappings, or an empty dictionary if the file does not exist.</returns>
    /// <exception cref="DnsCacheUnreadableException">When the file exists but cannot be read (I/O error or access
    /// denied), is not valid JSON, or its root is not a JSON object; the message names the file and the error.</exception>
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
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
    /// <returns><see langword="true"/> when the file was written; <see langword="false"/> when it could not be replaced,
    /// in which case it keeps its previous version.</returns>
    public async Task<bool> SaveAsync(IReadOnlyDictionary<string, DnsCacheEntry> cache, CancellationToken cancellationToken = default)
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
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file held open by another process, or a folder the account cannot write to, must not stop the run:
            // the cache on disk keeps its previous version, the caller is told so, and the next save writes every
            // entry again. The resolver saves from a timer, where an escaping exception would go unobserved.
            return false;
        }
    }

    /// <summary>
    /// Backward-compatible overload saving a simple IP-to-hostname dictionary.
    /// </summary>
    /// <param name="cache">Simple IP mappings dictionary to persist.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns><see langword="true"/> when the file was written; <see langword="false"/> when it kept its previous version.</returns>
    public Task<bool> SaveAsync(IReadOnlyDictionary<string, string> cache, CancellationToken cancellationToken = default)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same tolerance as SaveAsync: the cache on disk keeps its previous version. This save runs from Dispose,
            // which must not throw.
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
