using System.Net;
using System.Net.Sockets;

namespace PortAtlas.Core.Resolution;

/// <summary>
/// Dedicated offline/batch DNS and RDAP resolver providing rate-limiting throttling and progress notifications during mass IP enrichment.
/// </summary>
/// <param name="organizationResolver">Optional IP organization / RDAP resolver.</param>
/// <param name="rdapThrottleDelay">Throttle delay enforced between successive external RDAP queries to protect API quotas.</param>
/// <param name="resolutionTimeout">Timeout per individual DNS PTR lookup.</param>
/// <param name="logThrottlingNotice">Optional notification callback invoked whenever throttling delay is triggered.</param>
/// <param name="reverseDnsLookup">Reverse DNS query; the resolver of the operating system when omitted.</param>
/// <param name="timeProvider">Clock stamping the resolution time; the system clock when omitted.</param>
public sealed class OfflineDnsResolver(
    IIpOrganizationResolver? organizationResolver = null,
    TimeSpan? rdapThrottleDelay = null,
    TimeSpan? resolutionTimeout = null,
    Action<string>? logThrottlingNotice = null,
    IReverseDnsLookup? reverseDnsLookup = null,
    TimeProvider? timeProvider = null) : IOfflineIpResolver
{
    private readonly IIpOrganizationResolver? _organizationResolver = organizationResolver;
    private readonly TimeSpan _rdapThrottleDelay = rdapThrottleDelay ?? TimeSpan.FromMilliseconds(200);
    private readonly TimeSpan _resolutionTimeout = resolutionTimeout ?? TimeSpan.FromSeconds(2);
    private readonly Action<string>? _logThrottlingNotice = logThrottlingNotice;
    private readonly IReverseDnsLookup _reverseDnsLookup = reverseDnsLookup ?? new SystemReverseDnsLookup();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <inheritdoc />
    /// <remarks>Reverse DNS PTR first, then a throttled RDAP lookup when no name was found or the address is public.</remarks>
    /// <param name="address">Target IP address to resolve.</param>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>
    /// The entry reflecting the resolution results, or <see langword="null"/> when no name was found and the DNS server
    /// or the RDAP registry did not answer.
    /// </returns>
    public async Task<DnsCacheEntry?> ResolveSingleAsync(
        IPAddress address,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.None))
        {
            return new DnsCacheEntry
            {
                HostName = string.Empty,
                Source = "Excluded",
                ResolvedAtUtc = _timeProvider.GetUtcNow()
            };
        }

        if (IPAddress.IsLoopback(address))
        {
            return new DnsCacheEntry
            {
                HostName = address.ToString(),
                Source = "Local",
                ResolvedAtUtc = _timeProvider.GetUtcNow()
            };
        }

        var (dnsHostName, dnsAnswered) = await LookUpPtrAsync(address, cancellationToken).ConfigureAwait(false);
        var (ownerDetails, ownerAnswered) = await LookUpOwnerAsync(address, dnsHostName, cancellationToken).ConfigureAwait(false);

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
        else if (!dnsAnswered || !ownerAnswered)
        {
            // Same rule as the audit (CachedHostNameResolver): a server that did not answer has not said that the
            // address has no name, so no "None" entry is returned and the caller keeps what it had.
            return null;
        }
        else
        {
            hostName = string.Empty;
            source = "None";
        }

        return new DnsCacheEntry
        {
            HostName = hostName,
            Organization = ownerDetails?.Organization,
            Country = ownerDetails?.Country,
            NetworkRange = ownerDetails?.NetworkRange,
            Source = source,
            ResolvedAtUtc = _timeProvider.GetUtcNow()
        };
    }

    private async Task<(string? HostName, bool Answered)> LookUpPtrAsync(IPAddress address, CancellationToken cancellationToken)
    {
        try
        {
            using var dnsTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsTimeoutCts.CancelAfter(_resolutionTimeout);

            var lookedUpName = await _reverseDnsLookup.LookupAsync(address, dnsTimeoutCts.Token).ConfigureAwait(false);
            var isRealName = !string.IsNullOrWhiteSpace(lookedUpName) &&
                             !string.Equals(lookedUpName, address.ToString(), StringComparison.OrdinalIgnoreCase);
            return (isRealName ? lookedUpName : null, true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The DNS server did not answer within the timeout: unknown, not "no name". The RDAP registry is still
            // asked, and without an owner the address is left unresolved.
            return (null, false);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TryAgain)
        {
            // TryAgain is the resolver's own timeout or a temporary server failure: treated like the timeout above.
            return (null, false);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException or ArgumentException)
        {
            // The DNS servers answered that no PTR record exists (or the address cannot be queried): a "no name"
            // answer, which the RDAP lookup may still complete.
            return (null, true);
        }
    }

    // RDAP is asked when no PTR name was found, or for a public address whose owner is shown next to its DNS name.
    private async Task<(IpOwnerMetadata? Owner, bool Answered)> LookUpOwnerAsync(
        IPAddress address,
        string? dnsHostName,
        CancellationToken cancellationToken)
    {
        var organizationResolver = _organizationResolver;
        var isPublic = RdapIpOrganizationResolver.IsPublicRoutable(address);
        if (organizationResolver is null || (!string.IsNullOrWhiteSpace(dnsHostName) && !isPublic))
        {
            return (null, true);
        }

        if (_rdapThrottleDelay > TimeSpan.Zero && isPublic)
        {
            _logThrottlingNotice?.Invoke(
                $"RDAP rate limiting active (throttling delay {_rdapThrottleDelay.TotalMilliseconds:0}ms to respect registry quotas)...");
            await Task.Delay(_rdapThrottleDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var owner = await organizationResolver.ResolveOwnerDetailsAsync(address, cancellationToken).ConfigureAwait(false);
            return (owner, true);
        }
        catch (Exception ex) when (ex is IpOwnerLookupUnavailableException or HttpRequestException or SocketException or TimeoutException ||
                                   (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The registry did not answer: unknown, not "no owner". A DNS name is still returned, without organization;
            // without one the address is left unresolved.
            return (null, false);
        }
    }
}
