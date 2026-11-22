using AwesomeAssertions;
using PortAtlas.Core.Export;
using TopologyForge.Filtering;
using Xunit;

namespace PortAtlas.Tests.Export;

public sealed class AddressGroupDirectoryTests
{
    [Theory]
    [InlineData("192.168.10.20", "nas")]
    [InlineData("192.168.10.77", "lab")]
    [InlineData("192.168.30.5", "home")]
    [InlineData("::ffff:192.168.10.77", "lab")]
    public void FindGroup_WhenTheAddressIsInSeveralRanges_ReturnsTheGroupOfTheMostPreciseOne(string address, string expected)
    {
        // Arrange
        var directory = Create(("home", "192.168.0.0/16"), ("lab", "192.168.10.0/24"), ("nas", "192.168.10.20"));

        // Act
        var group = directory.FindGroup(address);

        // Assert
        group.Should().Be(expected);
    }

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("Any")]
    [InlineData("")]
    public void FindGroup_WhenTheAddressIsOutsideEveryGroupOrNotAnAddress_ReturnsNull(string address)
    {
        // Arrange
        var directory = Create(("lab", "192.168.10.0/24"));

        // Act
        var group = directory.FindGroup(address);

        // Assert
        group.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WhenTwoGroupsHoldTheSameRange_RefusesNamingTheRangeAndBothGroups()
    {
        // Arrange
        // 192.168.10.9/24 is 192.168.10.0/24 written with the address of one of its hosts.
        var ranges = Ranges(("lab", "192.168.10.0/24"), ("iot", "192.168.20.0/24"), ("vlan10", "192.168.10.9/24"));

        // Act
        var created = AddressGroupDirectory.TryCreate(ranges, out var directory, out var conflict);

        // Assert
        created.Should().BeFalse();
        directory.Should().BeNull();
        conflict.Should().Contain("192.168.10.0/24").And.Contain("'lab'").And.Contain("'vlan10'");
    }

    [Fact]
    public void TryCreate_WhenOneGroupHoldsTheSameRangeTwice_AcceptsIt()
    {
        // Arrange
        var ranges = Ranges(("lab", "192.168.10.0/24"), ("lab", "192.168.10.0/24"));

        // Act
        var created = AddressGroupDirectory.TryCreate(ranges, out var directory, out var conflict);

        // Assert
        created.Should().BeTrue();
        conflict.Should().BeNull();
        directory!.FindGroup("192.168.10.1").Should().Be("lab");
    }

    [Fact]
    public void Empty_WhenAsked_FindsNoGroup()
    {
        // Act
        var group = AddressGroupDirectory.Empty.FindGroup("192.168.10.1");

        // Assert
        group.Should().BeNull();
        AddressGroupDirectory.Empty.IsEmpty.Should().BeTrue();
    }

    internal static AddressGroupDirectory Create(params (string Group, string Range)[] ranges)
    {
        AddressGroupDirectory.TryCreate(Ranges(ranges), out var directory, out var conflict).Should().BeTrue(conflict);
        return directory!;
    }

    private static List<AddressGroupRange> Ranges(params (string Group, string Range)[] ranges) =>
        [.. ranges.Select(range => new AddressGroupRange(range.Group, IpAddressRange.Parse(range.Range)))];
}
