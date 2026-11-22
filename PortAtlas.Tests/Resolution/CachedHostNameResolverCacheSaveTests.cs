using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

/// <summary>
/// The cache file is written by a delayed save after each resolution and by a final save on disposal: a delayed save
/// that runs after the final one could replace it with an older state.
/// </summary>
public sealed class CachedHostNameResolverCacheSaveTests : IDisposable
{
    private static readonly DateTimeOffset StartTime = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    // Longer than CacheSaveDelay in real time: a save timed by the system clock instead of the injected one, or a
    // save started by the clock, would have written the file by then.
    private static readonly TimeSpan RealTimeSaveWindow = TimeSpan.FromSeconds(1);

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
        var clock = new FakeTimeProvider(StartTime);
        var resolver = CreateResolver(clock);
        await resolver.ResolveHostNameAsync(IPAddress.Parse("192.168.1.40"));

        // Act
        await resolver.DisposeAsync();
        await File.WriteAllTextAsync(_cachePath, "written after disposal");
        clock.Advance(CachedHostNameResolver.CacheSaveDelay);
        // The delayed save starts on Advance but would write the file asynchronously.
        await Task.Delay(RealTimeSaveWindow);

        // Assert
        (await File.ReadAllTextAsync(_cachePath)).Should().Be("written after disposal");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenTheClockHasNotReachedTheSaveDelay_WritesNoCacheFile()
    {
        // Arrange
        var clock = new FakeTimeProvider(StartTime);
        await using var resolver = CreateResolver(clock);
        await resolver.ResolveHostNameAsync(IPAddress.Parse("192.168.1.40"));

        // Act
        clock.Advance(CachedHostNameResolver.CacheSaveDelay - TimeSpan.FromMilliseconds(1));
        await Task.Delay(RealTimeSaveWindow);

        // Assert
        File.Exists(_cachePath).Should().BeFalse("the save delay is measured on the injected clock");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenTheClockReachesTheSaveDelay_WritesTheResolvedName()
    {
        // Arrange
        var clock = new FakeTimeProvider(StartTime);
        await using var resolver = CreateResolver(clock);
        await resolver.ResolveHostNameAsync(IPAddress.Parse("192.168.1.40"));

        // Act
        clock.Advance(CachedHostNameResolver.CacheSaveDelay);
        var cacheText = await WaitForCacheFileAsync();

        // Assert
        cacheText.Should().Contain("nas.home.arpa");
    }

    // The save runs once the clock has advanced, but writes the file asynchronously.
    private async Task<string> WaitForCacheFileAsync()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!File.Exists(_cachePath) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        return File.Exists(_cachePath) ? await File.ReadAllTextAsync(_cachePath) : string.Empty;
    }

    private CachedHostNameResolver CreateResolver(TimeProvider clock) => new(
        TimeSpan.FromSeconds(2),
        cacheStore: new PersistentDnsCacheStore(_cachePath),
        reverseDnsLookup: new FixedReverseDnsLookup("nas.home.arpa"),
        timeProvider: clock);

    private sealed class FixedReverseDnsLookup(string hostName) : IReverseDnsLookup
    {
        public Task<string> LookupAsync(IPAddress address, CancellationToken cancellationToken) => Task.FromResult(hostName);
    }
}
