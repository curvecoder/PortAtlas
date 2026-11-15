namespace PortAtlas.Core.Export;

/// <summary>
/// Expands the audit inputs given on the command line (files, directories or wildcard patterns) into a list of files.
/// </summary>
public static class AuditInputPathResolver
{
    /// <summary>
    /// File extensions of audit logs readable as CSV or NDJSON.
    /// </summary>
    public static readonly IReadOnlyList<string> AuditLogExtensions = [".csv", ".ndjson", ".jsonl"];

    /// <summary>
    /// Resolves each input to the files it designates.
    /// </summary>
    /// <param name="pathsOrPatterns">
    /// File paths, directory paths (every file with one of <paramref name="directoryExtensions"/> in the directory, not recursive)
    /// or wildcard patterns on the file name (<c>audits/nas-*.csv</c>), which keep the same extensions as a directory.
    /// </param>
    /// <param name="directoryExtensions">Extensions kept when an input is a directory or a wildcard pattern.</param>
    /// <returns>Distinct full paths of the existing files, in input order; inputs matching nothing are skipped.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    /// <remarks>
    /// Every file an input designates is returned, whatever it contains; <see cref="AuditLogSelector"/> keeps the
    /// audit logs among them.
    /// </remarks>
    public static IReadOnlyList<string> Resolve(IEnumerable<string> pathsOrPatterns, IReadOnlyCollection<string> directoryExtensions)
        => ResolveWithOrigin(pathsOrPatterns, directoryExtensions).Select(input => input.FullPath).ToList();

    /// <summary>
    /// Resolves each input to the files it designates and tells, for each file, whether an input is its own path.
    /// </summary>
    /// <param name="pathsOrPatterns">File paths, directory paths or wildcard patterns, as for <see cref="Resolve"/>.</param>
    /// <param name="directoryExtensions">Extensions kept when an input is a directory.</param>
    /// <returns>Distinct full paths of the existing files, in input order.</returns>
    internal static IReadOnlyList<ResolvedInputFile> ResolveWithOrigin(IEnumerable<string> pathsOrPatterns, IReadOnlyCollection<string> directoryExtensions)
    {
        ArgumentNullException.ThrowIfNull(pathsOrPatterns);
        ArgumentNullException.ThrowIfNull(directoryExtensions);

        var order = new List<string>();
        var namedByPath = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in pathsOrPatterns.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            var isNamed = File.Exists(item);
            foreach (var fullPath in Expand(item, directoryExtensions).Select(Path.GetFullPath))
            {
                if (!namedByPath.TryGetValue(fullPath, out var alreadyNamed))
                {
                    order.Add(fullPath);
                }

                // A file found in a directory and also given by its own path counts as named, whatever the order.
                namedByPath[fullPath] = alreadyNamed || isNamed;
            }
        }

        return order.ConvertAll(fullPath => new ResolvedInputFile(fullPath, namedByPath[fullPath]));
    }

    private static IEnumerable<string> Expand(string item, IReadOnlyCollection<string> directoryExtensions)
    {
        if (File.Exists(item))
        {
            return [item];
        }

        if (Directory.Exists(item))
        {
            return directoryExtensions.SelectMany(ext => Directory.EnumerateFiles(item, $"*{ext}", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase));
        }

        var directory = Path.GetDirectoryName(item);
        var pattern = Path.GetFileName(item);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        if (!Directory.Exists(directory) || string.IsNullOrWhiteSpace(pattern))
        {
            return [];
        }

        // A pattern selects the kinds of file a directory does: `collected/*` must not bring an NDJSON log to a CSV reader.
        return Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly)
            .Where(file => directoryExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase);
    }
}
