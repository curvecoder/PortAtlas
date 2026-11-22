using System.Text.Json;
using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using Xunit;

namespace PortAtlas.Tests.Configuration;

public sealed class ShippedConfigurationSampleTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("portatlas-sample-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void ShippedSample_WhenComparedWithConfigInit_ListsEveryConfigurationKey()
    {
        // Arrange
        var shipped = Path.Combine(AppContext.BaseDirectory, "portatlas.json");
        var generated = ConfigurationTemplateGenerator.GenerateDefaultConfigFile(Path.Combine(_directory.FullName, "portatlas.json"));

        // Act
        var shippedKeys = TopLevelKeysOf(shipped);
        var generatedKeys = TopLevelKeysOf(generated);

        // Assert
        shippedKeys.Should().BeEquivalentTo(generatedKeys);
    }

    private static List<string> TopLevelKeysOf(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return [.. document.RootElement.EnumerateObject().Select(property => property.Name)];
    }
}
