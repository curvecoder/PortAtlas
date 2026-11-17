using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using Xunit;

namespace PortAtlas.Tests.Configuration;

/// <summary>
/// A setting that takes one value of a closed set refuses any other value, as its command line option does, instead of
/// drawing the map with the default in its place.
/// </summary>
public sealed class ConfigurationClosedValueTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_ClosedValue_" + Guid.NewGuid().ToString("N"));

    public ConfigurationClosedValueTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A file still held by the OS must not fail the test run; the folder lives in the temp directory.
        }
    }

    [Theory]
    [InlineData("MapDirection", "both", "all, in, out")]
    [InlineData("MapDirection", " out", "all, in, out")]
    [InlineData("MapLayoutMode", "spiral", "layered, tree, horizontal-tree, organic, circular, grid")]
    [InlineData("MapLayoutMode", "", "layered, tree, horizontal-tree, organic, circular, grid")]
    [InlineData("MapSortMethod", "random", "barycentric, tiers, subnet, degree, alpha, ip")]
    [InlineData("MapRoutingMode", "curved", "bezier, orthogonal, straight, bus")]
    [InlineData("MapSpacingProfile", "huge", "compact, balanced, spacious")]
    [InlineData("MapEdgeLabels", "names", "ports, none")]
    [InlineData("DrawioEdgeMode", "merge", "adaptive, direction, pair, none")]
    [InlineData("DrawioSubnets", "yes", "on, off")]
    [InlineData("DrawioTheme", "blue", "dark, light")]
    public void Load_WhenClosedSettingHoldsAnUnknownValue_ThrowsConfigurationFileExceptionListingTheAcceptedValues(string key, string value, string acceptedValues)
    {
        // Arrange
        var path = WriteConfiguration($$"""{ "{{key}}": "{{value}}" }""");

        // Act
        var act = () => AppConfigurationFile.Load(path);

        // Assert
        act.Should().Throw<ConfigurationFileException>()
            .Which.Message.Should().StartWith(path)
            .And.Contain($"\"{key}\" is \"{value}\"")
            .And.EndWith($"Accepted values: {acceptedValues}.");
    }

    [Theory]
    [InlineData("MapDirection", "In")]
    [InlineData("MapLayoutMode", "layered")]
    [InlineData("MapLayoutMode", "Horizontal-Tree")]
    [InlineData("MapLayoutMode", "force")]
    [InlineData("MapSortMethod", "barycentric")]
    [InlineData("MapSortMethod", "cluster")]
    [InlineData("MapRoutingMode", "bezier")]
    [InlineData("MapSpacingProfile", "balanced")]
    [InlineData("MapEdgeLabels", "NONE")]
    [InlineData("DrawioEdgeMode", "adaptive")]
    [InlineData("DrawioSubnets", "Off")]
    [InlineData("DrawioSubnets", "off ")]
    [InlineData("DrawioTheme", "light")]
    public void Load_WhenClosedSettingHoldsAnAcceptedValue_ReadsIt(string key, string value)
    {
        // Arrange
        var path = WriteConfiguration($$"""{ "{{key}}": "{{value}}" }""");

        // Act
        var configuration = AppConfigurationFile.Load(path).Configuration;

        // Assert
        typeof(AppConfiguration).GetProperty(key)?.GetValue(configuration).Should().Be(value);
    }

    private string WriteConfiguration(string json)
    {
        var path = Path.Combine(_directory, "portatlas.json");
        File.WriteAllText(path, json);
        return path;
    }
}
