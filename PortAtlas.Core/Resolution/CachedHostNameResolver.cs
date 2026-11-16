using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Asynchronous non-blocking reverse DNS resolver with memory caching, subnet exclusion, and RDAP organization fallback.
/// </summary>
public sealed class CachedHostNameResolver(
    TimeSpan resolutionTimeout,
    DnsAddressExclusionEvaluator? exclusionEvaluator = null,
    PersistentDnsCacheStore? cacheStore = null,
    IIpOrganizationResolver? organizationResolver = null,
    IReadOnlyDictionary<string, string>? customHostAliases = null,
    IReverseDnsLookup? reverseDnsLookup = null,
    TimeProvider? timeProvider = null) : IHostNameResolver, IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Time after which an address without reverse DNS record is asked again.
    /// </summary>
    public static readonly TimeSpan NegativeResultRetryDelay = TimeSpan.FromHours(24);

    /// <summary>
    /// Time during which an address whose DNS query timed out is not queued again, so that a slow DNS server is not
    /// asked at every scan.
    /// </summary>
    public static readonly TimeSpan TransientFailureRetryDelay = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Time after which an address whose owner lookup (RDAP) got no answer is asked again. Longer than
    /// <see cref="TransientFailureRetryDelay"/> so that a server cut off from the Internet does not query its DNS
    /// server for every unnamed address each minute.
    /// </summary>
    public static readonly TimeSpan RdapUnavailableRetryDelay = TimeSpan.FromMinutes(15);

    // A reverse DNS call already handed to the OS does not stop on cancellation: disposal waits for the queued
    // resolutions only this long, so that the end of an audit is not held by a DNS server that does not answer.
    internal static readonly TimeSpan QueueShutdownWaitLimit = TimeSpan.FromSeconds(2);

    private const string CustomSource = "Custom";
    private const string ExcludedSource = "Excluded";

    private readonly TimeSpan _resolutionTimeout = resolutionTimeout;
    private readonly IReverseDnsLookup _reverseDnsLookup = reverseDnsLookup ?? new SystemReverseDnsLookup();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly DnsAddressExclusionEvaluator _exclusionEvaluator = exclusionEvaluator ?? new DnsAddressExclusionEvaluator([]);
    private readonly PersistentDnsCacheStore? _cacheStore = cacheStore;
    private readonly IIpOrganizationResolver? _organizationResolver = organizationResolver;
    private readonly IReadOnlyDictionary<IPAddress, string> _customAliases = HostAliasParser.Parse(customHostAliases);
    private readonly ConcurrentDictionary<IPAddress, DnsCacheEntry> _cache = LoadCache(cacheStore);
    private readonly ConcurrentDictionary<IPAddress, byte> _queuedAddresses = new();
    // Addresses whose last resolution got no answer (DNS timeout or RDAP unavailable), with the time before which they
    // are not queued again. An address present here may also have a cache entry: its DNS name is kept while the owner
    // lookup is pending.
    private readonly ConcurrentDictionary<IPAddress, DateTimeOffset> _retryNotBefore = new();
    private readonly Channel<IPAddress> _channel = Channel.CreateUnbounded<IPAddress>();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _concurrencySemaphore = new(10, 10);
    private Task? _backgroundWorker;
    private int _workerStarted;
    private int _pendingSaves;

    /// <inheritdoc />
    public event EventHandler<HostNameResolvedEventArgs>? HostNameResolved;

    /// <summary>
    /// Initializes a new instance with a 3-second reverse DNS timeout.
    /// </summary>
    public CachedHostNameResolver() : this(TimeSpan.FromSeconds(3))
    {
    }

    // Aliases and exclusions are read from the configuration at each start and never cached: an entry written by an
    // older version with one of these sources would keep applying after its removal from the configuration.
    // The resolver saves the cache it loads: a file that cannot be read is refused (DnsCacheUnreadableException) rather
    // than read as empty and then replaced by the names of this run only.
    private static ConcurrentDictionary<IPAddress, DnsCacheEntry> LoadCache(PersistentDnsCacheStore? cacheStore)
    {
        var cache = new ConcurrentDictionary<IPAddress, DnsCacheEntry>();
        if (cacheStore is null)
        {
            return cache;
        }

        foreach (var (key, entry) in cacheStore.LoadOrThrow())
        {
            if (IPAddress.TryParse(key, out var address) && !IsConfigurationSource(entry.Source))
            {
                cache.TryAdd(address, entry);
            }
        }

        return cache;
    }

    private static bool IsConfigurationSource(string source) =>
        string.Equals(source, CustomSource, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, ExcludedSource, StringComparison.OrdinalIgnoreCase);

    private void EnsureWorkerStarted()
    {
        if (Interlocked.CompareExchange(ref _workerStarted, 1, 0) == 0)
        {
            _backgroundWorker = Task.Run(() => ProcessQueueAsync(_cts.Token), _cts.Token);
        }
    }

    /// <inheritdoc />
    public bool TryGetCachedHostName(IPAddress address, out string hostName)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IsFastTrackAddress(address, out hostName))
        {
            return true;
        }

        if (TryGetCurrentEntry(address, out var cached))
        {
            hostName = cached.HostName;
            return true;
        }

        hostName = string.Empty;
        return false;
    }

    internal bool IsQueued(IPAddress address) => _queuedAddresses.ContainsKey(address);

    // A "no name" entry is kept only for NegativeResultRetryDelay, so that a record added later to the DNS zone, or an
    // entry written by an older version after a timeout, is eventually asked again.
    private bool TryGetCurrentEntry(IPAddress address, out DnsCacheEntry entry)
    {
        if (!_cache.TryGetValue(address, out var cached))
        {
            entry = new DnsCacheEntry();
            return false;
        }

        entry = cached;
        var isExpiredFailure = string.Equals(cached.Source, "None", StringComparison.OrdinalIgnoreCase) &&
                               _timeProvider.GetUtcNow() - cached.ResolvedAtUtc >= NegativeResultRetryDelay;
        return !isExpiredFailure;
    }

    /// <summary>
    /// Attempts to retrieve the full enriched cache entry for the specified IP address.
    /// </summary>
    /// <param name="address">Target IP address to query.</param>
    /// <param name="entry">Enriched cache entry or null if absent.</param>
    /// <returns>True if present in cache; otherwise false.</returns>
    public bool TryGetCachedEntry(IPAddress address, out DnsCacheEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_customAliases.TryGetValue(address, out var alias))
        {
            entry = new DnsCacheEntry { HostName = alias, Source = CustomSource, ResolvedAtUtc = _timeProvider.GetUtcNow() };
            return true;
        }

        if (_cache.TryGetValue(address, out var cached))
        {
            entry = cached;
            return true;
        }

        if (IsFastTrackAddress(address, out var fastHost))
        {
            var source = IPAddress.IsLoopback(address) ? "Local" : ExcludedSource;
            entry = new DnsCacheEntry { HostName = fastHost, Source = source };
            return true;
        }

        entry = null;
        return false;
    }

    /// <inheritdoc />
    public void QueueResolution(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IsFastTrackAddress(address, out _))
        {
            return;
        }

        if (!IsResolutionDue(address))
        {
            return;
        }

        EnsureWorkerStarted();

        if (_queuedAddresses.TryAdd(address, 0))
        {
            if (!_channel.Writer.TryWrite(address))
            {
                _queuedAddresses.TryRemove(address, out _);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string> ResolveHostNameAsync(IPAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (IsFastTrackAddress(address, out var fastHostName))
        {
            return fastHostName;
        }

        if (TryGetCurrentEntry(address, out var cachedHostName))
        {
            return cachedHostName.HostName;
        }

        await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCurrentEntry(address, out var doubleCheck))
            {
                return doubleCheck.HostName;
            }

            return await ResolveAndCacheAsync(address, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private bool IsResolutionDue(IPAddress address)
    {
        if (_retryNotBefore.TryGetValue(address, out var notBefore))
        {
            return _timeProvider.GetUtcNow() >= notBefore;
        }

        return !TryGetCurrentEntry(address, out _);
    }

    private bool IsFastTrackAddress(IPAddress address, out string hostName)
    {
        if (_customAliases.TryGetValue(address, out var alias))
        {
            hostName = alias;
            return true;
        }

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            hostName = string.Empty;
            return true;
        }

        if (IPAddress.IsLoopback(address))
        {
            hostName = "localhost";
            return true;
        }

        if (_exclusionEvaluator.ShouldExclude(address))
        {
            hostName = string.Empty;
            return true;
        }

        hostName = string.Empty;
        return false;
    }

    private async Task<string> ResolveAndCacheAsync(IPAddress address, CancellationToken cancellationToken)
    {
        string? dnsHostName = null;

        // 1. Attempt reverse DNS (PTR) lookup
        try
        {
            using var dnsTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsTimeoutCts.CancelAfter(_resolutionTimeout);

            var lookedUpName = await _reverseDnsLookup.LookupAsync(address, dnsTimeoutCts.Token).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(lookedUpName))
            {
                dnsHostName = lookedUpName;
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The DNS server did not answer within the timeout: a slow server is not a missing record, so nothing is
            // cached and the address is asked again after TransientFailureRetryDelay.
            _retryNotBefore[address] = _timeProvider.GetUtcNow() + TransientFailureRetryDelay;
            return string.Empty;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TryAgain)
        {
            // TryAgain is the resolver's own timeout or a temporary server failure: treated like the timeout above.
            _retryNotBefore[address] = _timeProvider.GetUtcNow() + TransientFailureRetryDelay;
            return string.Empty;
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
        {
            // The DNS servers answered that no PTR record exists (or the address cannot be queried): the "no name"
            // result below is cached and asked again after NegativeResultRetryDelay.
        }

        var (ownerDetails, isOwnerLookupPending) = await LookUpOwnerAsync(address, dnsHostName, cancellationToken).ConfigureAwait(false);
        if (!isOwnerLookupPending)
        {
            _retryNotBefore.TryRemove(address, out _);
        }
        else
        {
            // No RDAP answer is not "no owner": an address without name is not cached at all, and a DNS name is cached
            // without organization; both are asked again after RdapUnavailableRetryDelay.
            _retryNotBefore[address] = _timeProvider.GetUtcNow() + RdapUnavailableRetryDelay;
            if (string.IsNullOrWhiteSpace(dnsHostName))
            {
                return string.Empty;
            }
        }

        string hostName;
        string source;

        if (!string.IsNullOrWhiteSpace(dnsHostName))
        {
            hostName = dnsHostName;
            source = "DNS";
        }
        else if (!string.IsNullOrWhiteSpace(ownerDetails?.Organization))
        {
            hostName = ownerDetails.Organization;
            source = "RDAP";
        }
        else
        {
            hostName = string.Empty;
            source = "None";
        }

        var cacheEntry = new DnsCacheEntry
        {
            HostName = hostName,
            Organization = ownerDetails?.Organization,
            Country = ownerDetails?.Country,
            NetworkRange = ownerDetails?.NetworkRange,
            Source = source,
            ResolvedAtUtc = _timeProvider.GetUtcNow()
        };

        _cache[address] = cacheEntry;
        ScheduleCacheSave();
        return hostName;
    }

    // RDAP is asked when no PTR name was found, or for a public address whose owner is shown next to its DNS name.
    private async Task<(IpOwnerMetadata? Owner, bool IsPending)> LookUpOwnerAsync(
        IPAddress address,
        string? dnsHostName,
        CancellationToken cancellationToken)
    {
        var organizationResolver = _organizationResolver;
        if (organizationResolver is null ||
            (!string.IsNullOrWhiteSpace(dnsHostName) && !RdapIpOrganizationResolver.IsPublicRoutable(address)))
        {
            return (null, false);
        }

        try
        {
            var owner = await organizationResolver.ResolveOwnerDetailsAsync(address, cancellationToken).ConfigureAwait(false);
            return (owner, false);
        }
        catch (Exception ex) when (ex is IpOwnerLookupUnavailableException or HttpRequestException or SocketException or TimeoutException ||
                                   (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The registry did not answer (offline server, rate limit, error status, suspended queries): the caller
            // keeps the outcome open instead of recording "no owner".
            return (null, true);
        }
    }

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        // Resolutions started by this loop; only the loop touches the list. DisposeAsync awaits this method, so it
        // disposes the semaphore only once every resolution has released it.
        var inFlight = new List<Task>();
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var address))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (!IsResolutionDue(address))
                    {
                        // Named meanwhile by a direct call: the queued flag must be cleared, otherwise QueueResolution
                        // would ignore this address for the rest of the run.
                        _queuedAddresses.TryRemove(address, out _);
                        continue;
                    }

                    await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                    inFlight.RemoveAll(task => task.IsCompleted);
                    inFlight.Add(Task.Run(async () =>
                    {
                        try
                        {
                            string resolved;
                            try
                            {
                                resolved = await ResolveAndCacheAsync(address, cancellationToken).ConfigureAwait(false);
                            }
                            finally
                            {
                                // Cleared even when the resolution throws: a flag left set would make QueueResolution
                                // ignore this address for the rest of the run.
                                _queuedAddresses.TryRemove(address, out _);
                            }

                            if (!string.IsNullOrWhiteSpace(resolved))
                            {
                                HostNameResolved?.Invoke(this, new HostNameResolvedEventArgs(address, resolved));
                            }
                        }
                        finally
                        {
                            _concurrencySemaphore.Release();
                        }
                    }, cancellationToken));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cooperative queue termination
        }
        finally
        {
            // Waits for completion without rethrowing: a resolution reports its outcome through the cache and
            // HostNameResolved, never to the code disposing the resolver, and a cancelled one ends with an
            // OperationCanceledException that is expected here. A failure of a resolution or of a HostNameResolved
            // subscriber is not reported to the disposer either, as before these tasks were awaited.
            await Task.WhenAll(inFlight).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private void ScheduleCacheSave()
    {
        if (_cacheStore is null)
        {
            return;
        }

        if (Interlocked.Increment(ref _pendingSaves) == 1)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Exchange(ref _pendingSaves, 0);

                var snapshot = new Dictionary<string, DnsCacheEntry>(_cache.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in _cache)
                {
                    snapshot[kvp.Key.ToString()] = kvp.Value;
                }

                await _cacheStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            }, CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        _cts.Dispose();

        // Resolutions started by the queue may still be running and would release a disposed semaphore: it is left to
        // the garbage collector here, which is safe since its wait handle is never created. DisposeAsync waits for them
        // and disposes it.

        if (_cacheStore is not null)
        {
            var snapshot = new Dictionary<string, DnsCacheEntry>(_cache.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _cache)
            {
                snapshot[kvp.Key.ToString()] = kvp.Value;
            }

            _cacheStore.Save(snapshot);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _cts.CancelAsync().ConfigureAwait(false);

        var resolutionsFinished = true;
        if (_backgroundWorker is not null)
        {
            try
            {
                // Not _cts.Token: it is already cancelled here and would end the wait at once.
                await _backgroundWorker.WaitAsync(QueueShutdownWaitLimit, _timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Tolerated on service shutdown
            }
            catch (TimeoutException)
            {
                // A resolution blocked in the OS outlived the wait: it may still write the cache or raise
                // HostNameResolved after this call returns, and it releases the semaphore, which is therefore left to
                // the garbage collector as in Dispose.
                resolutionsFinished = false;
            }
        }

        _cts.Dispose();
        if (resolutionsFinished)
        {
            _concurrencySemaphore.Dispose();
        }

        if (_cacheStore is not null)
        {
            var snapshot = new Dictionary<string, DnsCacheEntry>(_cache.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _cache)
            {
                snapshot[kvp.Key.ToString()] = kvp.Value;
            }

            await _cacheStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
        }
    }
}
