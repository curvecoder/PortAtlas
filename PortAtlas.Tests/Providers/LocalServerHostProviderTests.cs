using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Providers;
using Xunit;

namespace PortAtlas.Tests.Providers;

public sealed class LocalServerHostProviderTests
{
    private static readonly HostInterfaceSnapshot BeforeLeaseRenewal =
        new(IsUp: true, IsLoopback: false, [IPAddress.Parse("192.168.1.20")], HasIPv4Gateway: true);

    private static readonly HostInterfaceSnapshot AfterLeaseRenewal =
        new(IsUp: true, IsLoopback: false, [IPAddress.Parse("192.168.1.45")], HasIPv4Gateway: true);

    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero));
    private readonly SwitchableInterfaceReader _reader = new([BeforeLeaseRenewal]);

    [Fact]
    public void GetLocalIpAddresses_WhenTheAddressChangedAMinuteAgo_ReturnsTheNewAddress()
    {
        // Arrange
        var provider = new LocalServerHostProvider(_reader, _clock);
        provider.GetLocalIpAddresses();
        _reader.Interfaces = [AfterLeaseRenewal];
        _clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var addresses = provider.GetLocalIpAddresses();

        // Assert
        addresses.Should().Contain(IPAddress.Parse("192.168.1.45")).And.NotContain(IPAddress.Parse("192.168.1.20"));
    }

    [Fact]
    public void GetCurrentHostMetadata_WhenTheAddressChangedAMinuteAgo_ReturnsTheNewPrimaryAddress()
    {
        // Arrange
        var provider = new LocalServerHostProvider(_reader, _clock);
        provider.GetCurrentHostMetadata();
        _reader.Interfaces = [AfterLeaseRenewal];
        _clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var metadata = provider.GetCurrentHostMetadata();

        // Assert
        metadata.PrimaryIpAddress.Should().Be("192.168.1.45");
    }

    [Fact]
    public void GetLocalIpAddresses_WithinAMinuteOfTheLastReading_DoesNotReadTheInterfacesAgain()
    {
        // Arrange
        var provider = new LocalServerHostProvider(_reader, _clock);
        provider.GetLocalIpAddresses();
        _clock.Advance(TimeSpan.FromSeconds(59));

        // Act
        provider.GetLocalIpAddresses();
        provider.GetCurrentHostMetadata();

        // Assert
        _reader.ReadCount.Should().Be(1);
    }

    [Fact]
    public void GetLocalIpAddresses_WhenARereadFindsNoInterface_KeepsTheLastAddressesUntilTheNextReading()
    {
        // Arrange
        var provider = new LocalServerHostProvider(_reader, _clock);
        provider.GetLocalIpAddresses();
        _reader.Interfaces = [];
        _clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        var addresses = provider.GetLocalIpAddresses();
        var metadata = provider.GetCurrentHostMetadata();

        // Assert
        addresses.Should().Contain(IPAddress.Parse("192.168.1.20"));
        metadata.PrimaryIpAddress.Should().Be("192.168.1.20");
        _reader.ReadCount.Should().Be(2);
    }

    [Fact]
    public void GetLocalIpAddresses_WhenInterfacesAreRead_IncludesLoopbackAndOnlyOperationalInterfaces()
    {
        // Arrange
        _reader.Interfaces =
        [
            BeforeLeaseRenewal,
            new(IsUp: false, IsLoopback: false, [IPAddress.Parse("192.168.1.21")], HasIPv4Gateway: false),
        ];
        var provider = new LocalServerHostProvider(_reader, _clock);

        // Act
        var addresses = provider.GetLocalIpAddresses();

        // Assert
        addresses.Should().BeEquivalentTo([IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("192.168.1.20")]);
    }

    private sealed class SwitchableInterfaceReader(IReadOnlyList<HostInterfaceSnapshot> interfaces) : IHostInterfaceReader
    {
        public IReadOnlyList<HostInterfaceSnapshot> Interfaces { get; set; } = interfaces;

        public int ReadCount { get; private set; }

        public IReadOnlyList<HostInterfaceSnapshot> ReadInterfaces()
        {
            ReadCount++;
            return Interfaces;
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delay) => _now += delay;
    }
}
