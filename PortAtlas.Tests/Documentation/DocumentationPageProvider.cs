namespace PortAtlas.Tests.Documentation;

/// <summary>
/// Finds the repository and the documentation pages that describe the current version.
/// </summary>
internal static class DocumentationPageProvider
{
    /// <summary>
    /// Folder that holds <c>PortAtlas.slnx</c>, found from the test binaries upwards.
    /// </summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>
    /// Folder of the example data set read by the documented scenarios.
    /// </summary>
    public static string HomelabExampleDirectory { get; } = Path.Combine(RepositoryRoot, "docs", "examples", "homelab");

    /// <summary>
    /// Reads the pages of the current version, with their path relative to the repository.
    /// </summary>
    /// <returns>Each page as its relative path, with forward slashes, and its text.</returns>
    // The upgrade guides describe the options of earlier versions on purpose, so only the current pages are read.
    public static IEnumerable<(string Location, string Markdown)> CurrentPages()
    {
        var docs = Path.Combine(RepositoryRoot, "docs");
        var pages = new[] { "how-to", "reference", "explanation" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(docs, folder), "*.md"))
            .Concat(Directory.EnumerateFiles(Path.Combine(docs, "examples"), "*.md", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(docs, "getting-started*.md"))
            .Concat(Directory.EnumerateFiles(RepositoryRoot, "README*.md"))
            .Where(path => !Path.GetFileName(path).StartsWith("upgrade-to-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        return pages.Select(path => (Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/'), File.ReadAllText(path)));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PortAtlas.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No PortAtlas.slnx above {AppContext.BaseDirectory}.");
    }
}
