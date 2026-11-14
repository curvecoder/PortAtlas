using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Providers;
using Xunit;

namespace PortAtlas.Tests.Providers;

public sealed class PrimaryAddressSelectorTests
{
    [Fact]
    public void SelectPrimaryAddress_WhenAVirtualSwitchWithoutGatewayComesFirst_ReturnsTheAddressOfTheGatewayInterface()
    {
        // Arrange
        HostInterfaceSnapshot[] interfaces =
        [
            new(IsUp: true, IsLoopback: false, [IPAddress.Parse("192.168.56.1")], HasIPv4Gateway: false),
            new(IsUp: true, IsLoopback: false, [IPAddress.Parse("192.168.1.20")], HasIPv4Gateway: true),
        ];

        // Act
        var primary = PrimaryAddressSelector.SelectPrimaryAddress(interfaces);

        // Assert
        primary.Should().Be("192.168.1.20");
    }

    [Fact]
    public void SelectPrimaryAddress_WhenNoInterfaceHasAGateway_ReturnsTheFirstIPv4AddressOfAnOperationalInterface()
    {
        // Arrange
        HostInterfaceSnapshot[] interfaces =
        [
            new(IsUp: true, IsLoopback: true, [IPAddress.Loopback], HasIPv4Gateway: false),
            new(IsUp: false, IsLoopback: false, [IPAddress.Parse("192.168.1.21")], HasIPv4Gateway: false),
            new(IsUp: true, IsLoopback: false, [IPAddress.Parse("fe80::1"), IPAddress.Parse("192.168.1.20")], HasIPv4Gateway: false),
        ];

        // Act
        var primary = PrimaryAddressSelector.SelectPrimaryAddress(interfaces);

        // Assert
        primary.Should().Be("192.168.1.20");
    }

    [Fact]
    public void SelectPrimaryAddress_WhenTheGatewayInterfaceHasNoIPv4Address_ReturnsTheFirstIPv4AddressOfAnotherInterface()
    {
        // Arrange
        HostInterfaceSnapshot[] interfaces =
        [
            new(IsUp: true, IsLoopback: false, [IPAddress.Parse("192.168.1.20")], HasIPv4Gateway: false),
            new(IsUp: false, IsLoopback: false, [IPAddress.Parse("192.168.1.21")], HasIPv4Gateway: true),
        ];

        // Act
        var primary = PrimaryAddressSelector.SelectPrimaryAddress(interfaces);

        // Assert
        primary.Should().Be("192.168.1.20");
    }

    [Fact]
    public void SelectPrimaryAddress_WhenNoInterfaceHasAnIPv4Address_ReturnsLoopback()
    {
        // Arrange
        HostInterfaceSnapshot[] interfaces =
        [
            new(IsUp: true, IsLoopback: false, [IPAddress.Parse("fe80::1")], HasIPv4Gateway: false),
        ];

        // Act
        var primary = PrimaryAddressSelector.SelectPrimaryAddress(interfaces);

        // Assert
        primary.Should().Be("127.0.0.1");
    }
}
