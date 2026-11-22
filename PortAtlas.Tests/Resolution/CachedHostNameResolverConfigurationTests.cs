using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

/// <summary>
/// Host aliases and DNS exclusions come from the configuration: removing them from the configuration must stop them
/// from applying, so the cache file may not carry them from one run to the next.
/// </summary>
public sealed class CachedHostNameResolverConfigurationTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("192.168.1.40");
    private readonly string _directory;
    private readonly string _cachePath;

    public CachedHostNameResolverConfigurationTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_DnsConfig_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _cachePath = Path.Combine(_directory, "dns_cache.json");
    }

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

    [Fact]
    public async Task ResolveHostNameAsync_WhenAliasWasRemovedFromConfiguration_ReturnsTheDnsName()
    {
        // Arrange
        var aliases = new Dictionary<string, string> { [Server.ToString()] = "old-alias.home.arpa" };
        await using (CreateResolver(new FixedReverseDnsLookup("nas.home.arpa"), aliases: aliases))
        {
            // The first run only loads the alias and saves the cache on disposal.
        }

        await using var resolver = CreateResolver(new FixedReverseDnsLookup("nas.home.arpa"));

        // Act
        var name = await resolver.ResolveHostNameAsync(Server);

        // Assert
        name.Should().Be("nas.home.arpa");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenExclusionWasRemovedFromConfiguration_ReturnsTheDnsName()
    {
        // Arrange
        var exclusion = new DnsAddressExclusionEvaluator(["192.168.1.0/24"]);
        await using (var excluding = CreateResolver(new FixedReverseDnsLookup("nas.home.arpa"), exclusion))
        {
            (await excluding.ResolveHostNameAsync(Server)).Should().BeEmpty();
        }

        await using var resolver = CreateResolver(new FixedReverseDnsLookup("nas.home.arpa"));

        // Act
        var name = await resolver.ResolveHostNameAsync(Server);

        // Assert
        name.Should().Be("nas.home.arpa");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenCacheFileFromOlderVersionHoldsAliasAndExclusion_IgnoresBoth()
    {
        // Arrange
        var excludedServer = IPAddress.Parse("192.168.1.41");
        await File.WriteAllTextAsync(
            _cachePath,
            "{\"192.168.1.40\": {\"HostName\": \"old-alias\", \"Source\": \"Custom\", \"ResolvedAtUtc\": \"2026-11-01T08:00:00Z\"}," +
            " \"192.168.1.41\": {\"HostName\": \"\", \"Source\": \"Excluded\", \"ResolvedAtUtc\": \"2026-11-01T08:00:00Z\"}}");
        await using var resolver = CreateResolver(new FixedReverseDnsLookup("nas.home.arpa"));

        // Act
        var aliasedName = await resolver.ResolveHostNameAsync(Server);
        var excludedName = await resolver.ResolveHostNameAsync(excludedServer);

        // Assert
        aliasedName.Should().Be("nas.home.arpa");
        excludedName.Should().Be("nas.home.arpa");
    }

    [Fact]
    public async Task DisposeAsync_WhenAliasAndExclusionApplied_DoesNotWriteThemToTheCacheFile()
    {
        // Arrange
        var aliases = new Dictionary<string, string> { [Server.ToString()] = "old-alias.home.arpa" };
        var exclusion = new DnsAddressExclusionEvaluator(["10.0.0.0/8"]);
        var resolver = CreateResolver(new FixedReverseDnsLookup("pihole.home.arpa"), exclusion, aliases);
        await resolver.ResolveHostNameAsync(IPAddress.Parse("10.1.2.3"));
        await resolver.ResolveHostNameAsync(Server);
        await resolver.ResolveHostNameAsync(IPAddress.Parse("192.168.1.53"));

        // Act
        await resolver.DisposeAsync();

        // Assert
        new PersistentDnsCacheStore(_cachePath).Load().Keys.Should().Equal("192.168.1.53");
    }

    [Fact]
    public void TryGetCachedEntry_WhenAliasConfigured_ReturnsCustomEntry()
    {
        // Arrange
        var aliases = new Dictionary<string, string> { [Server.ToString()] = "nas.home.arpa" };
        using var resolver = CreateResolver(new FixedReverseDnsLookup("unused"), aliases: aliases);

        // Act
        var found = resolver.TryGetCachedEntry(Server, out var entry);

        // Assert
        found.Should().BeTrue();
        entry.Should().NotBeNull();
        entry!.HostName.Should().Be("nas.home.arpa");
        entry.Source.Should().Be("Custom");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenAnAliasNamesTheSubnetOfTheAddress_ReturnsTheAliasWithoutQueryingDns()
    {
        // Arrange
        var aliases = new Dictionary<string, string> { ["192.168.1.0/24"] = "lab.home.arpa" };
        await using var resolver = CreateResolver(new FixedReverseDnsLookup("nas.home.arpa"), aliases: aliases);

        // Act
        var name = await resolver.ResolveHostNameAsync(Server);
        var found = resolver.TryGetCachedEntry(Server, out var entry);

        // Assert
        name.Should().Be("lab.home.arpa");
        found.Should().BeTrue();
        entry!.Source.Should().Be("Custom");
        resolver.IsQueued(Server).Should().BeFalse();
    }

    private CachedHostNameResolver CreateResolver(
        IReverseDnsLookup dns,
        DnsAddressExclusionEvaluator? exclusion = null,
        IReadOnlyDictionary<string, string>? aliases = null) =>
        new(TimeSpan.FromSeconds(2), exclusion, new PersistentDnsCacheStore(_cachePath), customHostAliases: aliases, reverseDnsLookup: dns);

    private sealed class FixedReverseDnsLookup(string hostName) : IReverseDnsLookup
    {
        public Task<string> LookupAsync(IPAddress address, CancellationToken cancellationToken) =>
            string.IsNullOrEmpty(hostName)
                ? Task.FromException<string>(new SocketException((int)SocketError.HostNotFound))
                : Task.FromResult(hostName);
    }
}
