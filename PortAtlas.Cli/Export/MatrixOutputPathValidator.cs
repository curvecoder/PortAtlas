namespace PortAtlas.Cli.Export;

/// <summary>
/// Checks the output paths of <c>export rules</c> before any log is read.
/// </summary>
public static class MatrixOutputPathValidator
{
    /// <summary>
    /// Finds the first output that would replace an audit log to read, or two outputs that name the same file.
    /// </summary>
    /// <param name="outputs">Outputs: the long name of the option and the path it gives, <see langword="null"/> when the
    /// output is not requested.</param>
    /// <param name="auditLogs">Full paths of the audit logs to read.</param>
    /// <returns>The message to show, or <see langword="null"/> when every output can be written.</returns>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public static string? FindConflict(IReadOnlyList<(string Option, string? Path)> outputs, IReadOnlyCollection<string> auditLogs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(auditLogs);

        var resolved = new List<(string Option, string Path)>();
        foreach (var (option, path) in outputs)
        {
            if (path is not null)
            {
                resolved.Add((option, Path.GetFullPath(path)));
            }
        }

        // The matrices are written once every log has been read: written over one of them, they would replace an
        // audit by a matrix, which no later command reads as flows.
        foreach (var (option, path) in resolved)
        {
            if (auditLogs.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                return $"The output '{path}' is also an audit log to read: choose another path for {option}.";
            }
        }

        // Two matrices written to one path would leave only the last one, with no sign that the other was lost.
        for (var i = 0; i < resolved.Count; i++)
        {
            for (var j = i + 1; j < resolved.Count; j++)
            {
                if (string.Equals(resolved[i].Path, resolved[j].Path, StringComparison.OrdinalIgnoreCase))
                {
                    return $"{resolved[i].Option} and {resolved[j].Option} name the same file '{resolved[i].Path}': choose two different paths.";
                }
            }
        }

        return null;
    }
}
