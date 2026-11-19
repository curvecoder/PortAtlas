using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Models;
using PortAtlas.Core.Resolution;
using ProcessForge.Models;
using SocketForge.Models;
using Xunit;

namespace PortAtlas.Tests.Resolution;

public sealed class ReloadedFlowNameResolverTests
{
    private static readonly ServerHostMetadata Server = new("proxmox", "Debian 12", "192.168.1.20");

    [Fact]
    public void ResolveFromCache_WhenAReloadedFlowHasNoNameAndTheCacheHasOne_GivesItTheCachedName()
    {
        // Arrange
        var resolver = new ReloadedFlowNameResolver(new CacheOnlyResolver(new Dictionary<IPAddress, string>
        {
            [IPAddress.Parse("192.168.1.30")] = "pbs.home.arpa",
        }));
        NetworkAuditEntry[] reloaded = [CreateFlow("192.168.1.30", 8007, "")];

        // Act
        var named = resolver.ResolveFromCache(reloaded);

        // Assert
        named.Should().ContainSingle().Which.RemoteHostName.Should().Be("pbs.home.arpa");
    }

    [Fact]
    public void ResolveFromCache_WhenFlowsAreMixed_KeepsExistingNamesAndOrderAndLeavesUncachedFlowsUnnamed()
    {
        // Arrange
        var resolver = new ReloadedFlowNameResolver(new CacheOnlyResolver(new Dictionary<IPAddress, string>
        {
            [IPAddress.Parse("192.168.1.2")] = "pihole.home.arpa",
            [IPAddress.Parse("192.168.1.30")] = "pbs.home.arpa",
        }));
        NetworkAuditEntry[] reloaded =
        [
            CreateFlow("192.168.1.2", 53, "dns.home.arpa"),
            CreateFlow("192.168.1.40", 445, ""),
            CreateFlow("192.168.1.30", 8007, "   "),
        ];

        // Act
        var named = resolver.ResolveFromCache(reloaded);

        // Assert
        named.Select(flow => flow.RemoteHostName).Should().Equal("dns.home.arpa", "", "pbs.home.arpa");
    }

    [Fact]
    public void QueueUnnamed_WhenFlowsAreMixed_QueuesEachUnnamedRemoteAddressOnce()
    {
        // Arrange
        var hostNameResolver = new QueueRecordingResolver();
        var resolver = new ReloadedFlowNameResolver(hostNameResolver);
        NetworkAuditEntry[] reloaded =
        [
            CreateFlow("192.168.1.2", 53, "pihole.home.arpa"),
            CreateFlow("192.168.1.40", 445, ""),
            CreateFlow("192.168.1.40", 139, "  "),
            CreateFlow("192.168.1.50", 8006, ""),
        ];

        // Act
        resolver.QueueUnnamed(reloaded);

        // Assert
        hostNameResolver.Queued.Should().Equal(IPAddress.Parse("192.168.1.40"), IPAddress.Parse("192.168.1.50"));
    }

    private static NetworkAuditEntry CreateFlow(string remoteIp, int remotePort, string remoteHostName)
    {
        var seenUtc = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        return new NetworkAuditEntry(
            seenUtc,
            seenUtc,
            1,
            Server,
            new ActiveSocketConnection(
                SocketTransportProtocol.Tcp,
                SocketFlowDirection.Outbound,
                new SocketEndpoint(IPAddress.Parse("192.168.1.20"), 51000),
                new SocketEndpoint(IPAddress.Parse(remoteIp), remotePort),
                "Established",
                new ProcessMetadata(300, "pveproxy", "/usr/bin/pveproxy", "", "", "www-data")),
            remoteHostName);
    }

    private sealed class CacheOnlyResolver(IReadOnlyDictionary<IPAddress, string> cache) : IHostNameResolver
    {
        public event EventHandler<HostNameResolvedEventArgs>? HostNameResolved
        {
            add { }
            remove { }
        }

        public bool TryGetCachedHostName(IPAddress address, out string hostName)
        {
            hostName = cache.GetValueOrDefault(address, string.Empty);
            return hostName.Length > 0;
        }

        public void QueueResolution(IPAddress address) =>
            throw new InvalidOperationException("Reloading must not request a resolution.");

        public Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reloading must not request a resolution.");
    }

    private sealed class QueueRecordingResolver : IHostNameResolver
    {
        public event EventHandler<HostNameResolvedEventArgs>? HostNameResolved
        {
            add { }
            remove { }
        }

        public List<IPAddress> Queued { get; } = [];

        public bool TryGetCachedHostName(IPAddress address, out string hostName)
        {
            hostName = string.Empty;
            return false;
        }

        public void QueueResolution(IPAddress address) => Queued.Add(address);

        public Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Queuing must not wait for a resolution.");
    }
}
