using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Filtering;
using Xunit;

namespace PortAtlas.Tests.Filtering;

public sealed class CidrRangeTests
{
    [Theory]
    [InlineData("192.168.1.10/24", "192.168.1.0/24")]
    [InlineData("192.168.1.0/24", "192.168.1.0/24")]
    [InlineData("10.20.30.40/8", "10.0.0.0/8")]
    [InlineData("192.168.1.77/26", "192.168.1.64/26")]
    [InlineData("203.0.113.10/0", "0.0.0.0/0")]
    [InlineData("2001:db8::1/64", "2001:db8::/64")]
    [InlineData("fd12:3456:789a::1/7", "fc00::/7")]
    public void Parse_WhenSubnetIsWrittenWithHostBits_KeepsTheNetworkOfThatHost(string text, string expected)
    {
        // Act
        var range = CidrRange.Parse(text);

        // Assert
        range.ToString().Should().Be(expected);
        range.IsSingleAddress.Should().BeFalse();
    }

    [Theory]
    [InlineData("192.168.1.50", "192.168.1.50")]
    [InlineData("  192.168.1.50  ", "192.168.1.50")]
    [InlineData("192.168.1.50/32", "192.168.1.50")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("2001:db8::1/128", "2001:db8::1")]
    public void Parse_WhenTextIsOneAddress_ReturnsASingleAddressRange(string text, string expected)
    {
        // Act
        var range = CidrRange.Parse(text);

        // Assert
        range.ToString().Should().Be(expected);
        range.IsSingleAddress.Should().BeTrue();
    }

    [Theory]
    [InlineData("nas")]
    [InlineData("nas.home.arpa/24")]
    [InlineData("192.168.1.0/33")]
    [InlineData("192.168.1.0/-1")]
    [InlineData("192.168.1.0/")]
    [InlineData("192.168.1.0/24/8")]
    [InlineData("192.168.1.0/+24")]
    [InlineData("192.168.1.0/abc")]
    [InlineData("/24")]
    [InlineData("2001:db8::/129")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WhenTextIsNotAnAddressNorASubnet_ThrowsFormatExceptionNamingTheText(string text)
    {
        // Act
        var act = () => CidrRange.Parse(text);

        // Assert
        act.Should().Throw<FormatException>().WithMessage($"'{text}'*");
    }

    // The address parser of .NET reads these as inet_aton does: "443" is 0.0.1.187 and "192.168.1/24" is the subnet
    // 192.168.0.0/24. A port typed in an address filter, or a subnet missing a byte, must not become another address.
    [Theory]
    [InlineData("443")]
    [InlineData("10.1")]
    [InlineData("192.168.1/24")]
    [InlineData("0x7f.0.0.1")]
    [InlineData("010.0.0.1")]
    [InlineData("192.168.1.1.")]
    public void Parse_WhenIPv4AddressIsNotWrittenAsFourDecimalBytes_ThrowsFormatException(string text)
    {
        // Act
        var act = () => CidrRange.Parse(text);

        // Assert
        act.Should().Throw<FormatException>().WithMessage($"'{text}'*");
    }

    [Theory]
    [InlineData("nas")]
    [InlineData("192.168.1.0/33")]
    [InlineData("443")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_WhenTextIsNotAnAddressNorASubnet_ReturnsFalseWithoutRange(string? text)
    {
        // Act
        var isRead = CidrRange.TryParse(text, out var range);

        // Assert
        isRead.Should().BeFalse();
        range.Should().BeNull();
    }

    // The zone of a link-local address (fe80::1%12) names an interface of the machine that wrote it, not the host.
    [Fact]
    public void Parse_WhenIPv6AddressCarriesAZone_KeepsTheAddressWithoutIt()
    {
        // Act
        var range = CidrRange.Parse("fe80::1%12");

        // Assert
        range.ToString().Should().Be("fe80::1");
        range.Contains(IPAddress.Parse("fe80::1%12")).Should().BeTrue();
    }

    // Both forms of an IPv4 host meet the same rule, so an IPv6 subnet that holds the IPv4-mapped block holds IPv4.
    [Theory]
    [InlineData("::/0", "192.168.1.50", true)]
    [InlineData("::ffff:0:0/96", "192.168.1.50", true)]
    [InlineData("2001:db8::/32", "192.168.1.50", false)]
    [InlineData("0.0.0.0/0", "2001:db8::1", false)]
    public void Contains_WhenIPv6SubnetIsTestedWithAnIPv4Address_FollowsTheIPv4MappedBlock(string text, string address, bool expected)
    {
        // Arrange
        var range = CidrRange.Parse(text);

        // Act
        var contains = range.Contains(IPAddress.Parse(address));

        // Assert
        contains.Should().Be(expected);
    }

    [Theory]
    [InlineData("192.168.1.10/24", "192.168.1.0", true)]
    [InlineData("192.168.1.10/24", "192.168.1.255", true)]
    [InlineData("192.168.1.10/24", "192.168.2.1", false)]
    [InlineData("192.168.1.50", "192.168.1.50", true)]
    [InlineData("192.168.1.50", "192.168.1.51", false)]
    [InlineData("2001:db8::/64", "2001:db8::abcd", true)]
    [InlineData("2001:db8::/64", "2001:db9::1", false)]
    [InlineData("192.168.1.0/24", "2001:db8::1", false)]
    public void Contains_WhenAddressIsTested_TellsWhetherItBelongsToTheRange(string text, string address, bool expected)
    {
        // Arrange
        var range = CidrRange.Parse(text);

        // Act
        var contains = range.Contains(IPAddress.Parse(address));

        // Assert
        contains.Should().Be(expected);
    }

    // A dual-stack socket reports an IPv4 peer as ::ffff:a.b.c.d: both forms name the same host.
    [Theory]
    [InlineData("192.168.1.0/24", "::ffff:192.168.1.50")]
    [InlineData("192.168.1.50", "::ffff:192.168.1.50")]
    [InlineData("::ffff:192.168.1.50", "192.168.1.50")]
    [InlineData("::ffff:192.168.1.0/120", "192.168.1.50")]
    public void Contains_WhenRuleAndAddressDifferOnlyByTheIPv4MappedForm_ReturnsTrue(string text, string address)
    {
        // Arrange
        var range = CidrRange.Parse(text);

        // Act
        var contains = range.Contains(IPAddress.Parse(address));

        // Assert
        contains.Should().BeTrue();
    }

    [Fact]
    public void Equals_WhenTwoTextsNameTheSameSubnet_ReturnsTrue()
    {
        // Act
        var first = CidrRange.Parse("192.168.1.10/24");
        var second = CidrRange.Parse("192.168.1.200/24");

        // Assert
        first.Should().Be(second);
    }

    [Fact]
    public void Parse_WhenTextIsNull_ThrowsArgumentNullException()
    {
        // Act
        var act = () => CidrRange.Parse(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
