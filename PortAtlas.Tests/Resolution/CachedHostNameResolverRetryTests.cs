using System.Net;
using System.Net.Sockets;
using AwesomeAssertions;
using PortAtlas.Core.Resolution;
using Xunit;

namespace PortAtlas.Tests.Resolution;

/// <summary>
/// A slow DNS server must not leave an address unnamed for good: timeouts are retried, and an address without PTR
/// record is asked again once the retry delay has passed, including entries read from an older cache file.
/// </summary>
public sealed class CachedHostNameResolverRetryTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("192.0.2.79");
    private static readonly IPAddress PublicServer = IPAddress.Parse("52.123.242.181");
    private readonly ManualTimeProvider _clock = new(new DateTimeOffset(2026, 10, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly string _directory;

    public CachedHostNameResolverRetryTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PortAtlas_DnsRetry_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
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
    public async Task ResolveHostNameAsync_WhenDnsAnswersWithinTimeout_ReturnsNameFromSlowServer()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.After(TimeSpan.FromMilliseconds(300), "SVLVBDD67"));
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(3));

        // Act
        var name = await resolver.ResolveHostNameAsync(Server);

        // Assert
        name.Should().Be("SVLVBDD67");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenDnsExceedsTimeout_DoesNotCacheTheFailure()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(call => call == 1 ? DnsAnswer.Never() : DnsAnswer.Now("SVLVBDD67"));
        await using var resolver = CreateResolver(dns, TimeSpan.FromMilliseconds(50));

        // Act
        var first = await resolver.ResolveHostNameAsync(Server);
        var cachedAfterTimeout = resolver.TryGetCachedEntry(Server, out _);
        var second = await resolver.ResolveHostNameAsync(Server);

        // Assert
        first.Should().BeEmpty();
        cachedAfterTimeout.Should().BeFalse();
        second.Should().Be("SVLVBDD67");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenNoPtrRecord_RetriesOnlyAfterRetryDelay()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(call => call == 1 ? DnsAnswer.NotFound() : DnsAnswer.Now("SVLVBDD67"));
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1));

        // Act
        var first = await resolver.ResolveHostNameAsync(Server);
        _clock.Advance(TimeSpan.FromHours(1));
        var withinDelay = await resolver.ResolveHostNameAsync(Server);
        _clock.Advance(CachedHostNameResolver.NegativeResultRetryDelay);
        var afterDelay = await resolver.ResolveHostNameAsync(Server);

        // Assert
        first.Should().BeEmpty();
        withinDelay.Should().BeEmpty();
        afterDelay.Should().Be("SVLVBDD67");
        dns.Calls.Should().Be(2);
    }

    [Fact]
    public async Task QueueResolution_WhenCacheFileHoldsOldFailedEntry_ResolvesTheAddressAgain()
    {
        // Arrange
        var cachePath = Path.Combine(_directory, "dns_cache.json");
        await File.WriteAllTextAsync(cachePath, "{\"192.0.2.79\": {\"HostName\": \"\", \"Source\": \"None\", \"ResolvedAtUtc\": \"2026-10-25T07:05:52Z\"}}");
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.Now("SVLVBDD67"));
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), new PersistentDnsCacheStore(cachePath));
        var resolved = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        resolver.HostNameResolved += (_, e) => resolved.TrySetResult(e.HostName);

        // Act
        resolver.QueueResolution(Server);
        var name = await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Assert
        name.Should().Be("SVLVBDD67");
    }

    [Fact]
    public async Task QueueResolution_WhenDnsTimedOut_WaitsTheTransientDelayBeforeAskingAgain()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.Never());
        await using var resolver = CreateResolver(dns, TimeSpan.FromMilliseconds(50));
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => dns.Calls == 1 && !resolver.IsQueued(Server));

        // Act
        resolver.QueueResolution(Server);
        await Task.Delay(300);
        var callsWithinDelay = dns.Calls;
        _clock.Advance(CachedHostNameResolver.TransientFailureRetryDelay);
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => dns.Calls == 2);

        // Assert
        callsWithinDelay.Should().Be(1);
        dns.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenNoPtrAndOwnerLookupUnavailable_DoesNotCacheTheAddress()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.NotFound());
        var owners = new ScriptedOwnerResolver(_ => OwnerAnswer.Unavailable());
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);

        // Act
        var name = await resolver.ResolveHostNameAsync(Server);

        // Assert
        name.Should().BeEmpty();
        resolver.TryGetCachedEntry(Server, out _).Should().BeFalse();
    }

    [Fact]
    public async Task QueueResolution_WhenResolutionThrows_ClearsTheQueuedFlagSoTheAddressCanBeQueuedAgain()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.NotFound());
        var owners = new ScriptedOwnerResolver(call => call == 1
            ? OwnerAnswer.Throws()
            : OwnerAnswer.Found(new IpOwnerMetadata("Example Networks", null, null, null)));
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);

        // Act
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => owners.Calls == 1 && !resolver.IsQueued(Server));
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => owners.Calls == 2);

        // Assert
        owners.Calls.Should().Be(2);
    }

    [Fact]
    public async Task DisposeAsync_WhenQueuedResolutionIsRunning_WaitsForItToFinish()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.NotFound());
        var owners = new ScriptedOwnerResolver(_ => OwnerAnswer.SlowToStop(
            TimeSpan.FromMilliseconds(300),
            new IpOwnerMetadata("Example Networks", null, null, null)));
        var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => owners.Calls == 1);

        // Act
        await resolver.DisposeAsync();

        // Assert
        owners.Completed.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenQueuedResolutionIgnoresCancellation_ReturnsAfterTheWaitLimit()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.NotFound());
        var owners = new ScriptedOwnerResolver(_ => OwnerAnswer.SlowToStop(
            TimeSpan.FromSeconds(20),
            new IpOwnerMetadata("Example Networks", null, null, null)));
        var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => owners.Calls == 1);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        await resolver.DisposeAsync();

        // Assert
        stopwatch.Elapsed.Should().BeLessThan(CachedHostNameResolver.QueueShutdownWaitLimit + TimeSpan.FromSeconds(3));
        owners.Completed.Should().Be(0);
    }

    [Fact]
    public async Task QueueResolution_WhenOwnerLookupUnavailable_WaitsTheRdapDelayBeforeAskingAgain()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.NotFound());
        var owners = new ScriptedOwnerResolver(_ => OwnerAnswer.Unavailable());
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => owners.Calls == 1 && !resolver.IsQueued(Server));

        // Act
        _clock.Advance(CachedHostNameResolver.TransientFailureRetryDelay);
        resolver.QueueResolution(Server);
        await Task.Delay(300);
        var callsWithinDelay = owners.Calls;
        _clock.Advance(CachedHostNameResolver.RdapUnavailableRetryDelay);
        resolver.QueueResolution(Server);
        await WaitUntilAsync(() => owners.Calls == 2);

        // Assert
        callsWithinDelay.Should().Be(1);
        owners.Calls.Should().Be(2);
    }

    [Fact]
    public async Task QueueResolution_WhenDnsNamedButOwnerLookupUnavailable_KeepsNameAndCompletesOwnerAfterDelay()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.Now("edge.example.net"));
        var owners = new ScriptedOwnerResolver(call => call == 1
            ? OwnerAnswer.Unavailable()
            : OwnerAnswer.Found(new IpOwnerMetadata("Example Networks", "FR", "52.123.0.0 - 52.123.255.255", "EXAMPLE")));
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);
        resolver.QueueResolution(PublicServer);
        await WaitUntilAsync(() => owners.Calls == 1 && !resolver.IsQueued(PublicServer));
        resolver.TryGetCachedEntry(PublicServer, out var entryAfterFailure);

        // Act
        _clock.Advance(CachedHostNameResolver.RdapUnavailableRetryDelay);
        resolver.QueueResolution(PublicServer);
        await WaitUntilAsync(() => owners.Calls == 2 && !resolver.IsQueued(PublicServer));
        resolver.TryGetCachedEntry(PublicServer, out var entryAfterRetry);

        // Assert
        entryAfterFailure.Should().NotBeNull();
        entryAfterFailure!.HostName.Should().Be("edge.example.net");
        entryAfterFailure.Organization.Should().BeNull();
        entryAfterRetry.Should().NotBeNull();
        entryAfterRetry!.HostName.Should().Be("edge.example.net");
        entryAfterRetry.Organization.Should().Be("Example Networks");
    }

    [Fact]
    public async Task ResolveHostNameAsync_WhenCancelledDuringOwnerLookup_ThrowsAndCachesNothing()
    {
        // Arrange
        var dns = new ScriptedReverseDnsLookup(_ => DnsAnswer.NotFound());
        var owners = new ScriptedOwnerResolver(_ => OwnerAnswer.Never());
        await using var resolver = CreateResolver(dns, TimeSpan.FromSeconds(1), owners: owners);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var act = () => resolver.ResolveHostNameAsync(Server, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        resolver.TryGetCachedEntry(Server, out _).Should().BeFalse();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(deadline, "the background resolution should have progressed");
            await Task.Delay(20);
        }
    }

    private CachedHostNameResolver CreateResolver(
        IReverseDnsLookup dns,
        TimeSpan timeout,
        PersistentDnsCacheStore? store = null,
        IIpOrganizationResolver? owners = null) =>
        new(timeout, cacheStore: store, organizationResolver: owners, reverseDnsLookup: dns, timeProvider: _clock);

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan delay) => _now += delay;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed record DnsAnswer(TimeSpan Delay, string? HostName, bool Hangs)
    {
        public static DnsAnswer Now(string hostName) => new(TimeSpan.Zero, hostName, Hangs: false);

        public static DnsAnswer After(TimeSpan delay, string hostName) => new(delay, hostName, Hangs: false);

        public static DnsAnswer NotFound() => new(TimeSpan.Zero, null, Hangs: false);

        public static DnsAnswer Never() => new(TimeSpan.Zero, null, Hangs: true);
    }

    private sealed class ScriptedReverseDnsLookup(Func<int, DnsAnswer> script) : IReverseDnsLookup
    {
        private int _calls;

        public int Calls => _calls;

        public async Task<string> LookupAsync(IPAddress address, CancellationToken cancellationToken)
        {
            var answer = script(Interlocked.Increment(ref _calls));
            if (answer.Hangs)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            await Task.Delay(answer.Delay, cancellationToken);
            return answer.HostName ?? throw new SocketException((int)SocketError.HostNotFound);
        }
    }

    private sealed record OwnerAnswer(
        IpOwnerMetadata? Metadata,
        bool IsUnavailable,
        bool Hangs,
        bool Fails = false,
        TimeSpan DelayIgnoringCancellation = default)
    {
        public static OwnerAnswer Found(IpOwnerMetadata metadata) => new(metadata, IsUnavailable: false, Hangs: false);

        public static OwnerAnswer Unavailable() => new(null, IsUnavailable: true, Hangs: false);

        public static OwnerAnswer Never() => new(null, IsUnavailable: false, Hangs: true);

        // An exception the resolver does not expect, such as one thrown by a defect in the RDAP parser.
        public static OwnerAnswer Throws() => new(null, IsUnavailable: false, Hangs: false, Fails: true);

        // A registry call that is already under way and does not stop at once when the resolver is disposed.
        public static OwnerAnswer SlowToStop(TimeSpan delay, IpOwnerMetadata metadata) =>
            new(metadata, IsUnavailable: false, Hangs: false, DelayIgnoringCancellation: delay);
    }

    private sealed class ScriptedOwnerResolver(Func<int, OwnerAnswer> script) : IIpOrganizationResolver
    {
        private int _calls;
        private int _completed;

        public int Calls => _calls;

        public int Completed => _completed;

        public async Task<string?> ResolveOrganizationAsync(IPAddress address, CancellationToken cancellationToken = default) =>
            (await ResolveOwnerDetailsAsync(address, cancellationToken))?.Organization;

        public async Task<IpOwnerMetadata?> ResolveOwnerDetailsAsync(IPAddress address, CancellationToken cancellationToken = default)
        {
            var answer = script(Interlocked.Increment(ref _calls));
            if (answer.Hangs)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            if (answer.IsUnavailable)
            {
                throw new IpOwnerLookupUnavailableException("RDAP registry did not answer.");
            }

            if (answer.Fails)
            {
                throw new InvalidOperationException("Unexpected failure in the owner lookup.");
            }

            if (answer.DelayIgnoringCancellation > TimeSpan.Zero)
            {
                await Task.Delay(answer.DelayIgnoringCancellation, CancellationToken.None);
            }

            Interlocked.Increment(ref _completed);
            return answer.Metadata;
        }

        public void Dispose()
        {
            // Nothing to release: the script holds no resource.
        }
    }
}
