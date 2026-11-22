using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

public sealed class HostAliasTableTests
{
    [Fact]
    public void TryFind_WhenTheKeyIsASubnet_NamesEveryAddressOfTheSubnet()
    {
        // Arrange
        var table = HostAliasTable.Parse(new Dictionary<string, string> { ["192.168.10.0/24"] = "lab.home.arpa" });

        // Act
        var found = table.TryFind(IPAddress.Parse("192.168.10.77"), out var alias);

        // Assert
        found.Should().BeTrue();
        alias.Should().NotBeNull();
        alias!.HostName.Should().Be("lab.home.arpa");
        alias.Range.ToString().Should().Be("192.168.10.0/24");
    }

    [Fact]
    public void TryFind_WhenTheAddressIsOutsideEveryRange_ReturnsFalse()
    {
        // Arrange
        var table = HostAliasTable.Parse(new Dictionary<string, string> { ["192.168.10.0/24"] = "lab.home.arpa" });

        // Act
        var found = table.TryFind(IPAddress.Parse("192.168.11.1"), out var alias);

        // Assert
        found.Should().BeFalse();
        alias.Should().BeNull();
    }

    [Theory]
    [InlineData("192.168.10.20", "nas.home.arpa")]
    [InlineData("192.168.10.200", "lab-dhcp.home.arpa")]
    [InlineData("192.168.10.5", "lab.home.arpa")]
    [InlineData("192.168.200.5", "home.arpa")]
    public void TryFind_WhenTheAddressIsInSeveralRanges_ReturnsTheMostPreciseOne(string address, string expected)
    {
        // Arrange
        // Written from the widest to the narrowest and back, so that the order of the keys cannot decide.
        var table = HostAliasTable.Parse(new Dictionary<string, string>
        {
            ["192.168.10.128/25"] = "lab-dhcp.home.arpa",
            ["192.168.0.0/16"] = "home.arpa",
            ["192.168.10.20"] = "nas.home.arpa",
            ["192.168.10.0/24"] = "lab.home.arpa"
        });

        // Act
        table.TryFind(IPAddress.Parse(address), out var alias);

        // Assert
        alias.Should().NotBeNull();
        alias!.HostName.Should().Be(expected);
    }

    [Fact]
    public void Parse_WhenTwoKeysNameTheSameRange_KeepsTheLastOne()
    {
        // Arrange
        // A subnet written with the address of one of its hosts is the subnet itself.
        var aliases = new Dictionary<string, string>
        {
            ["192.168.10.0/24"] = "first.home.arpa",
            ["192.168.10.9/24"] = "second.home.arpa"
        };

        // Act
        var table = HostAliasTable.Parse(aliases);

        // Assert
        table.TryFind(IPAddress.Parse("192.168.10.1"), out var alias).Should().BeTrue();
        alias!.HostName.Should().Be("second.home.arpa");
    }

    [Fact]
    public void Parse_WhenAKeyIsUnreadableOrANameIsEmpty_LeavesThemOutAndKeepsTheOthers()
    {
        // Arrange
        var aliases = new Dictionary<string, string>
        {
            ["nas"] = "nas.home.arpa",
            ["192.168.10.0/33"] = "lab.home.arpa",
            ["192.168.20.0/24"] = " ",
            ["192.168.30.0/24"] = "  iot.home.arpa  "
        };

        // Act
        var table = HostAliasTable.Parse(aliases);

        // Assert
        table.TryFind(IPAddress.Parse("192.168.10.1"), out _).Should().BeFalse();
        table.TryFind(IPAddress.Parse("192.168.20.1"), out _).Should().BeFalse();
        table.TryFind(IPAddress.Parse("192.168.30.1"), out var alias).Should().BeTrue();
        alias!.HostName.Should().Be("iot.home.arpa");
    }

    [Fact]
    public void TryFind_WhenThePeerIsReportedInItsIpv4MappedForm_FindsTheIpv4Alias()
    {
        // Arrange
        var table = HostAliasTable.Parse(new Dictionary<string, string>
        {
            ["192.168.10.20"] = "nas.home.arpa",
            ["192.168.20.0/24"] = "iot.home.arpa"
        });

        // Act
        var single = table.TryFind(IPAddress.Parse("::ffff:192.168.10.20"), out var singleAlias);
        var subnet = table.TryFind(IPAddress.Parse("::ffff:192.168.20.9"), out var subnetAlias);

        // Assert
        single.Should().BeTrue();
        singleAlias!.HostName.Should().Be("nas.home.arpa");
        subnet.Should().BeTrue();
        subnetAlias!.HostName.Should().Be("iot.home.arpa");
    }

    [Fact]
    public void TryFind_WhenTheKeyIsAnIpv6Subnet_NamesItsAddresses()
    {
        // Arrange
        var table = HostAliasTable.Parse(new Dictionary<string, string> { ["fd00:10::/64"] = "lab.home.arpa" });

        // Act
        var found = table.TryFind(IPAddress.Parse("fd00:10::25"), out var alias);

        // Assert
        found.Should().BeTrue();
        alias!.HostName.Should().Be("lab.home.arpa");
    }

    [Fact]
    public void Parse_WhenTheAliasesAreNull_ReturnsAnEmptyTable()
    {
        // Act
        var table = HostAliasTable.Parse(null);

        // Assert
        table.IsEmpty.Should().BeTrue();
        table.TryFind(IPAddress.Parse("192.168.10.1"), out _).Should().BeFalse();
    }
}
