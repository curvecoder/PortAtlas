using AwesomeAssertions;
using FileForge.IO;

namespace PortAtlas.Tests.Documentation;

/// <summary>
/// Runs the example scenarios of <c>docs/examples/homelab/scenarios.json</c> and checks that the files they write, and
/// the tables the pages quote from them, are those of the current version: an example cannot show an output that
/// PortAtlas no longer writes.
/// </summary>
/// <remarks>
/// With the environment variable <c>PORTATLAS_UPDATE_DOC_EXAMPLES=1</c> (<c>scripts/Update-DocExamples.ps1</c>), the
/// scenario test replaces the committed outputs with those of the run, for a reviewed behaviour change.
/// </remarks>
[Collection(ConsoleOutputCollection.Name)]
public sealed class DocumentationExampleOutputTests
{
    private const string UpdateVariable = "PORTATLAS_UPDATE_DOC_EXAMPLES";

    public static TheoryData<string> Scenarios() => new(ExampleScenarioCatalog.Load().Select(scenario => scenario.Name));

    public static TheoryData<string, int> Excerpts()
    {
        var data = new TheoryData<string, int>();
        foreach (var (location, markdown) in DocumentationPageProvider.CurrentPages())
        {
            foreach (var excerpt in MarkdownExampleExtractor.ExtractOutputExcerpts(markdown))
            {
                data.Add(location, excerpt.LineNumber);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Run_WhenGivenAnExampleScenario_WritesTheCommittedOutputFiles(string name)
    {
        // Arrange
        var scenario = ExampleScenarioCatalog.Find(name);
        scenario.Should().NotBeNull();
        var runDirectory = Directory.CreateTempSubdirectory("portatlas-example-").FullName;
        var committedDirectory = Path.Combine(ExampleScenarioCatalog.OutputDirectory, name);

        try
        {
            // Act
            var (exitCode, console) = await ExampleScenarioRunner.RunAsync(scenario!, runDirectory);

            // Assert
            exitCode.Should().Be(0, console);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                ReplaceCommittedOutputs(runDirectory, committedDirectory);
                return;
            }

            FileNames(runDirectory).Should().Equal(FileNames(committedDirectory), $"scenario '{name}' writes these files; run scripts/Update-DocExamples.ps1 after a reviewed change");
            foreach (var fileName in FileNames(runDirectory))
            {
                ReadNormalized(Path.Combine(runDirectory, fileName)).Should().Be(
                    ReadNormalized(Path.Combine(committedDirectory, fileName)),
                    $"scenario '{name}' writes {fileName}; run scripts/Update-DocExamples.ps1 after a reviewed change");
            }
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Excerpts))]
    public void Excerpt_WhenQuotedInAPage_MatchesTheCommittedOutputOfItsScenario(string location, int lineNumber)
    {
        // Arrange
        var markdown = File.ReadAllText(Path.Combine(DocumentationPageProvider.RepositoryRoot, location));
        var excerpt = MarkdownExampleExtractor.ExtractOutputExcerpts(markdown).Single(candidate => candidate.LineNumber == lineNumber);
        var scenario = ExampleScenarioCatalog.Find(excerpt.Scenario);
        var where = $"{location}:{lineNumber}";

        // Act
        var file = ReadTable(Path.Combine(ExampleScenarioCatalog.OutputDirectory, excerpt.Scenario, excerpt.FileName));

        // Assert
        scenario.Should().NotBeNull($"{where} quotes scenario '{excerpt.Scenario}'");
        excerpt.Rows.Should().NotBeEmpty($"{where} must be followed by a Markdown table");
        MarkdownExampleExtractor.ExtractCommandLines(markdown).Select(line => line.Arguments)
            .Should().Contain(scenario!.Command, $"{where} quotes the output of a command that the page must show");
        file.Columns.Should().Contain(excerpt.Columns, $"{where} names columns of {excerpt.FileName}");

        var quoted = excerpt.Rows.Select(row => string.Join(" | ", row)).ToList();
        var written = file.Rows.Select(row => string.Join(" | ", excerpt.Columns.Select(column => row[file.Columns.IndexOf(column)]))).ToList();
        if (excerpt.IsComplete)
        {
            quoted.Should().Equal(written, $"{where} quotes every row of {excerpt.FileName}");
        }
        else
        {
            written.Should().Contain(quoted, $"{where} quotes rows of {excerpt.FileName}");
        }
    }

    [Fact]
    public void OutputDirectory_WhenListed_HoldsOneFolderPerScenario()
    {
        // Arrange
        var names = ExampleScenarioCatalog.Load().Select(scenario => scenario.Name).Order(StringComparer.Ordinal);

        // Act
        var folders = Directory.EnumerateDirectories(ExampleScenarioCatalog.OutputDirectory).Select(Path.GetFileName).Order(StringComparer.Ordinal);

        // Assert
        folders.Should().Equal(names);
    }

    private static List<string> FileNames(string directory) =>
        Directory.Exists(directory)
            ? [.. Directory.EnumerateFiles(directory).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal)]
            : [];

    // Git checks the outputs out with the line ends of the platform, while PortAtlas writes those of the machine.
    private static string ReadNormalized(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static (List<string> Columns, List<IReadOnlyList<string>> Rows) ReadTable(string path)
    {
        using var reader = new StreamReader(path);
        var records = DelimitedRecordReader.ReadRecords(reader).Select(record => record.Fields).ToList();
        return records.Count == 0 ? ([], []) : ([.. records[0]], [.. records.Skip(1)]);
    }

    private static void ReplaceCommittedOutputs(string runDirectory, string committedDirectory)
    {
        if (Directory.Exists(committedDirectory))
        {
            Directory.Delete(committedDirectory, recursive: true);
        }

        Directory.CreateDirectory(committedDirectory);
        foreach (var path in Directory.EnumerateFiles(runDirectory))
        {
            File.Copy(path, Path.Combine(committedDirectory, Path.GetFileName(path)));
        }
    }
}
