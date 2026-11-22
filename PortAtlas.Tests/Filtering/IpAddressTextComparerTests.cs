using AwesomeAssertions;
using PortAtlas.Core.Filtering;
using Xunit;

namespace PortAtlas.Tests.Filtering;

public sealed class IpAddressTextComparerTests
{
    [Fact]
    public void Compare_WhenTwoIpv4AddressesDifferInTheLastByte_SortsThemByValue()
    {
        // Arrange
        string[] addresses = ["192.168.1.10", "192.168.1.2", "192.168.1.100", "10.0.0.5"];

        // Act
        var sorted = addresses.Order(IpAddressTextComparer.OrdinalIgnoreCase).ToList();

        // Assert
        sorted.Should().Equal("10.0.0.5", "192.168.1.2", "192.168.1.10", "192.168.1.100");
    }

    [Fact]
    public void Compare_WhenIpv4AndIpv6AreMixed_PutsIpv4First()
    {
        // Arrange
        string[] addresses = ["fd00::10", "192.168.1.2", "::1", "fd00::9"];

        // Act
        var sorted = addresses.Order(IpAddressTextComparer.OrdinalIgnoreCase).ToList();

        // Assert
        sorted.Should().Equal("192.168.1.2", "::1", "fd00::9", "fd00::10");
    }

    [Fact]
    public void Compare_WhenAddressesAndNamesAreMixed_PutsAddressesFirstThenNamesAsText()
    {
        // Arrange
        string[] values = ["servers", "192.168.1.10", "Any", "lab", "192.168.1.9"];

        // Act
        var sorted = values.Order(IpAddressTextComparer.Ordinal).ToList();

        // Assert
        sorted.Should().Equal("192.168.1.9", "192.168.1.10", "Any", "lab", "servers");
    }

    [Theory]
    [InlineData("10")]
    [InlineData("10.0.1")]
    [InlineData("10.0.0.0/8")]
    public void Compare_WhenANameIsReadAsAnAddressOnlyByTheLenientParser_SortsItAsAName(string name)
    {
        // Arrange
        string[] values = [name, "192.168.1.2"];

        // Act
        var sorted = values.Order(IpAddressTextComparer.Ordinal).ToList();

        // Assert
        sorted.Should().Equal("192.168.1.2", name);
    }

    [Fact]
    public void Compare_WhenTheSameHostIsWrittenInTwoForms_OrdersThemAsText()
    {
        // Arrange
        var comparer = IpAddressTextComparer.OrdinalIgnoreCase;

        // Act
        var mappedAgainstIpv4 = comparer.Compare("::ffff:192.168.1.2", "192.168.1.2");
        var mappedAgainstNext = comparer.Compare("::ffff:192.168.1.2", "192.168.1.3");
        var sameAddressOtherCase = comparer.Compare("FD00::A", "fd00::a");

        // Assert
        mappedAgainstIpv4.Should().BeGreaterThan(0, "the two forms name one host, so the text decides and ':' sorts after '1'");
        mappedAgainstNext.Should().BeLessThan(0, "an IPv4-mapped address sorts as its IPv4 form");
        sameAddressOtherCase.Should().Be(0);
    }

    [Fact]
    public void Compare_WhenAValueIsNull_SortsItFirst()
    {
        // Arrange
        var comparer = IpAddressTextComparer.Ordinal;

        // Act & Assert
        comparer.Compare(null, "192.168.1.2").Should().BeLessThan(0);
        comparer.Compare("lab", null).Should().BeGreaterThan(0);
        comparer.Compare(null, null).Should().Be(0);
    }
}
