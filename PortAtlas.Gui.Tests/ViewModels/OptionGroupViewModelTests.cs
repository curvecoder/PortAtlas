using AwesomeAssertions;
using PortAtlas.Gui.Core.Catalog;
using PortAtlas.Gui.Core.ViewModels;

namespace PortAtlas.Gui.Tests.ViewModels;

public sealed class OptionGroupViewModelTests
{
    [Theory]
    [InlineData(OptionGroup.Inputs, "Inputs")]
    [InlineData(OptionGroup.Filters, "Filters")]
    [InlineData(OptionGroup.MapRendering, "Map rendering")]
    [InlineData(OptionGroup.Outputs, "Outputs")]
    [InlineData(OptionGroup.Resolution, "Resolution")]
    [InlineData(OptionGroup.Other, "Other options")]
    public void Title_ForEachGroup_ReturnsTheHeaderShownToTheUser(OptionGroup group, string expected)
    {
        // Arrange
        var vm = new OptionGroupViewModel(group, []);

        // Act
        var title = vm.Title;

        // Assert
        title.Should().Be(expected);
    }
}
