using System.CommandLine.Parsing;
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Cli.CommandLine;
using PortAtlas.Cli.Configuration;

namespace PortAtlas.Tests.Documentation;

/// <summary>
/// Runs the command lines and the configuration examples of the current documentation through the real parser and
/// the real configuration loader, so that a renamed option or key fails here instead of in a reader's terminal.
/// </summary>
public sealed class DocumentationExampleTests
{
    public static TheoryData<string, string> CommandLines()
    {
        var data = new TheoryData<string, string>();
        foreach (var (location, markdown) in CurrentPages())
        {
            foreach (var commandLine in MarkdownExampleExtractor.ExtractCommandLines(markdown))
            {
                data.Add($"{location}:{commandLine.LineNumber}", commandLine.Arguments);
            }
        }

        return data;
    }

    public static TheoryData<string, string> ConfigurationExamples()
    {
        var keys = typeof(AppConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);
        var data = new TheoryData<string, string>();
        foreach (var (location, markdown) in CurrentPages())
        {
            foreach (var block in MarkdownExampleExtractor.ExtractJsonBlocks(markdown).Where(block => NamesAConfigurationKey(block.Json, keys)))
            {
                data.Add($"{location}:{block.LineNumber}", block.Json);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(CommandLines))]
    public void Parse_WhenGivenADocumentedCommandLine_ReportsNoError(string location, string arguments)
    {
        // Arrange
        var args = CommandLineParser.SplitCommandLine(arguments).ToArray();

        // Act
        var parseResult = PortAtlasCommandLine.CreateRootCommand().Parse(args);

        // Assert
        parseResult.Errors.Select(error => error.Message).Should().BeEmpty($"{location} documents `PortAtlas {arguments}`");
    }

    [Theory]
    [MemberData(nameof(ConfigurationExamples))]
    public void Load_WhenGivenADocumentedConfigurationExample_AcceptsEveryKey(string location, string json)
    {
        // Arrange
        var path = Path.Combine(Directory.CreateTempSubdirectory("portatlas-doc-").FullName, "portatlas.json");
        File.WriteAllText(path, json);

        try
        {
            // Act
            var load = () => AppConfigurationFile.Load(path);

            // Assert
            load.Should().NotThrow($"{location} documents this configuration");
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path) ?? path, recursive: true);
        }
    }

    // A JSON block of the documentation is a configuration example when one of its keys is a configuration key; the
    // DNS cache and NDJSON examples are keyed by addresses and field names, which are not.
    private static bool NamesAConfigurationKey(string json, HashSet<string> keys)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(property => keys.Contains(property.Name));
        }
        catch (JsonException)
        {
            // A block that is not a complete JSON document, such as an excerpt with an ellipsis, cannot be loaded
            // as a configuration file; it is not checked.
            return false;
        }
    }

    // The upgrade guides describe the options of earlier versions on purpose, so only the current pages are read.
    private static IEnumerable<(string Location, string Markdown)> CurrentPages()
    {
        var root = FindRepositoryRoot();
        var docs = Path.Combine(root, "docs");
        var pages = new[] { "how-to", "reference", "explanation" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(docs, folder), "*.md"))
            .Concat(Directory.EnumerateFiles(docs, "getting-started*.md"))
            .Concat(Directory.EnumerateFiles(root, "README*.md"))
            .Where(path => !Path.GetFileName(path).StartsWith("upgrade-to-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        return pages.Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)));
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
