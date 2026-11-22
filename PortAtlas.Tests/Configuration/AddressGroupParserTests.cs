using AwesomeAssertions;
using PortAtlas.Cli.Configuration;
using TopologyForge.Filtering;
using Xunit;

namespace PortAtlas.Tests.Configuration;

public sealed class AddressGroupParserTests
{
    [Fact]
    public void TryParseArgument_WhenSpacesSurroundTheNameAndTheRange_ReadsBoth()
    {
        // Act
        var parsed = AddressGroupParser.TryParseArgument("lab = 192.168.10.0/24", out var range, out var error);

        // Assert
        parsed.Should().BeTrue(error);
        range!.GroupName.Should().Be("lab");
        range.Range.Should().Be(IpAddressRange.Parse("192.168.10.0/24"));
    }

    [Fact]
    public void TryBuild_WhenARangeOfTheFileHasSurroundingSpaces_ReadsIt()
    {
        // Arrange
        var configured = new Dictionary<string, List<string>> { [" lab "] = [" 192.168.10.0/24 "] };

        // Act
        var built = AddressGroupParser.TryBuild(configured, [], out var directory, out var error);

        // Assert
        built.Should().BeTrue(error);
        directory!.FindGroup("192.168.10.5").Should().Be("lab");
    }

    [Fact]
    public void TryBuild_WhenTheFileAndTheOptionNameOneGroupInAnotherCase_JoinsTheirRangesUnderTheFileSpelling()
    {
        // Arrange
        var configured = new Dictionary<string, List<string>> { ["Lab"] = ["192.168.10.0/24"] };

        // Act
        var built = AddressGroupParser.TryBuild(configured, ["LAB=192.168.20.0/24", "servers=192.168.1.0/24"], out var directory, out var error);

        // Assert
        built.Should().BeTrue(error);
        directory!.FindGroup("192.168.10.5").Should().Be("Lab");
        directory.FindGroup("192.168.20.5").Should().Be("Lab");
        directory.FindGroup("192.168.1.50").Should().Be("servers");
    }
}
