using System.Net;
using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

/// <summary>
/// The cache file is written by a delayed save after each resolution and by a final save on disposal: a delayed save
/// that runs after the final one could replace it with an older state.
/// </summary>
public sealed class CachedHostNameResolverCacheSaveTests : IDisposable
{
    // Longer than the delay of the save scheduled after a resolution.
    private static readonly TimeSpan ScheduledSaveWindow = TimeSpan.FromSeconds(1.5);

    private readonly string _directory;
    private readonly string _cachePath;

    public CachedHostNameResolverCacheSaveTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_DnsSave_" + Guid.NewGuid().ToString("N"));
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
    public async Task DisposeAsync_WhenASaveIsScheduled_WritesTheCacheNoMoreAfterReturning()
    {
        // Arrange
        var resolver = new CachedHostNameResolver(
            TimeSpan.FromSeconds(2),
            cacheStore: new PersistentDnsCacheStore(_cachePath),
            reverseDnsLookup: new FixedReverseDnsLookup("nas.home.arpa"));
        await resolver.ResolveHostNameAsync(IPAddress.Parse("192.168.1.40"));

        // Act
        await resolver.DisposeAsync();
        await File.WriteAllTextAsync(_cachePath, "written after disposal");
        await Task.Delay(ScheduledSaveWindow);

        // Assert
        (await File.ReadAllTextAsync(_cachePath)).Should().Be("written after disposal");
    }

    private sealed class FixedReverseDnsLookup(string hostName) : IReverseDnsLookup
    {
        public Task<string> LookupAsync(IPAddress address, CancellationToken cancellationToken) => Task.FromResult(hostName);
    }
}
