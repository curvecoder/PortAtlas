using AwesomeAssertions;

namespace PortAtlas.Tests.Documentation;

[Collection(ConsoleOutputCollection.Name)]
public sealed class ExampleScenarioRunnerTests
{
    [Fact]
    public async Task RunAsync_WhenTheCommandWritesANamedOutput_ReportsThatFile()
    {
        // Arrange
        var scenario = new ExampleScenario("test", "export rules -i logs -o rules.csv");
        var runDirectory = Directory.CreateTempSubdirectory("portatlas-runner-").FullName;

        try
        {
            // Act
            var run = await ExampleScenarioRunner.RunAsync(scenario, runDirectory);

            // Assert
            run.ExitCode.Should().Be(0, run.Console);
            run.WrittenFiles.Should().Equal("rules.csv");
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenTheCommandWritesNextToItsInputAndToADefaultPath_ReportsThoseFilesInTheRunFolderOnly()
    {
        // Arrange
        var scenario = new ExampleScenario("test", "resolve -i logs/pihole.csv --cache-only");
        var runDirectory = Directory.CreateTempSubdirectory("portatlas-runner-").FullName;

        try
        {
            // Act
            var run = await ExampleScenarioRunner.RunAsync(scenario, runDirectory);

            // Assert
            run.ExitCode.Should().Be(0, run.Console);
            run.WrittenFiles.Should().Equal("logs/pihole_enriched.csv", "portatlas_dns_cache.json");
            File.Exists(Path.Combine(runDirectory, "logs", "pihole_enriched.csv")).Should().BeTrue();
            File.Exists(Path.Combine(DocumentationPageProvider.HomelabExampleDirectory, "logs", "pihole_enriched.csv")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(runDirectory, recursive: true);
        }
    }
}
