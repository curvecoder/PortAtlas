using System.ComponentModel;
using PortAtlas.Core.Deduplication;
using PortAtlas.Core.Export;
using PortAtlas.Core.Filtering;
using PortAtlas.Core.Models;
using PortAtlas.Core.Providers;
using PortAtlas.Core.Resolution;
using ProcessForge.Filtering;
using SocketForge.Contracts;
using SocketForge.Models;

namespace PortAtlas.Core.Engine;

/// <summary>
/// Orchestrates SocketForge socket sampling, ProcessForge filtering, flow filtering, DNS resolution, and the audit logs.
/// </summary>
/// <remarks>
/// The logs are rewritten whole from the flows held in memory, every <c>logRewriteInterval</c> and when the audit
/// stops, so that each flow has one line carrying its latest observation count, last-seen date and name.
/// </remarks>
public sealed class FlowAuditEngine(
    ISocketScanner socketScanner,
    ProcessExclusionFilter exclusionFilter,
    INetworkFlowFilter networkFlowFilter,
    ConnectionDeduplicationRegister deduplicationRegister,
    IServerHostProvider hostProvider,
    AuditLogRewriter logRewriter,
    IHostNameResolver hostNameResolver,
    TimeSpan scanInterval,
    TimeSpan logRewriteInterval,
    TimeProvider? timeProvider = null)
{
    /// <summary>
    /// Period between two rewrites of the logs used by the overloads that do not take one.
    /// </summary>
    public static readonly TimeSpan DefaultLogRewriteInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Number of scan cycles failing in a row that stops the audit.
    /// </summary>
    public const int ConsecutiveScanFailureLimit = 10;

    private readonly IHostNameResolver _hostNameResolver = InitializeHostNameResolver(
        socketScanner,
        exclusionFilter,
        networkFlowFilter,
        deduplicationRegister,
        hostProvider,
        logRewriter,
        hostNameResolver);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _lastLogRewriteUtc = (timeProvider ?? TimeProvider.System).GetUtcNow();

    /// <summary>
    /// Overload initializing the engine with explicit flow filter and default no-op host name resolver.
    /// </summary>
    public FlowAuditEngine(
        ISocketScanner socketScanner,
        ProcessExclusionFilter exclusionFilter,
        INetworkFlowFilter networkFlowFilter,
        ConnectionDeduplicationRegister deduplicationRegister,
        IServerHostProvider hostProvider,
        AuditLogRewriter logRewriter,
        TimeSpan scanInterval)
        : this(
            socketScanner,
            exclusionFilter,
            networkFlowFilter,
            deduplicationRegister,
            hostProvider,
            logRewriter,
            new NoOpHostNameResolver(),
            scanInterval,
            DefaultLogRewriteInterval)
    {
    }

    /// <summary>
    /// Compatibility overload without explicit flow filter (uses default flow filter options).
    /// </summary>
    public FlowAuditEngine(
        ISocketScanner socketScanner,
        ProcessExclusionFilter exclusionFilter,
        ConnectionDeduplicationRegister deduplicationRegister,
        IServerHostProvider hostProvider,
        AuditLogRewriter logRewriter,
        TimeSpan scanInterval)
        : this(
            socketScanner,
            exclusionFilter,
            new NetworkFlowFilter(new NetworkFlowFilterOptions()),
            deduplicationRegister,
            hostProvider,
            logRewriter,
            new NoOpHostNameResolver(),
            scanInterval,
            DefaultLogRewriteInterval)
    {
    }

    /// <summary>
    /// Event raised at the end of each scan cycle with metric and discovery summary.
    /// </summary>
    public event EventHandler<FlowAuditCycleEventArgs>? ScanCycleCompleted;

    /// <summary>
    /// Event raised when a scan cycle fails and the audit keeps running.
    /// </summary>
    public event EventHandler<FlowAuditCycleFailedEventArgs>? ScanCycleFailed;

    /// <summary>
    /// Executes the continuous audit loop until cancellation is requested, then writes the logs.
    /// </summary>
    /// <exception cref="IOException">When the logs cannot be written at the stop; the message names the log.</exception>
    /// <exception cref="UnauthorizedAccessException">When the folder of a log cannot be written at the stop.</exception>
    public async Task RunContinuousAuditAsync(CancellationToken cancellationToken = default)
    {
        var stoppedByFailure = true;
        try
        {
            using var timer = new PeriodicTimer(scanInterval);
            var consecutiveFailures = 0;

            do
            {
                var activeSockets = await ScanSocketsToleratingFailureAsync(consecutiveFailures + 1, cancellationToken).ConfigureAwait(false);
                if (activeSockets is null)
                {
                    consecutiveFailures++;
                    continue;
                }

                consecutiveFailures = 0;
                await ProcessScannedSocketsAsync(activeSockets, cancellationToken).ConfigureAwait(false);
            }
            while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));

            stoppedByFailure = false;
        }
        catch (OperationCanceledException)
        {
            stoppedByFailure = false;
            throw;
        }
        finally
        {
            // The observations made since the last periodic rewrite are written when the audit stops, which is
            // why this write does not take the token that stopped it. When a scan failed, a log that cannot be
            // written must not hide that failure, which is what stopped the audit.
            if (stoppedByFailure)
            {
                await RewriteLogKeepingFlowsOnFailureAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                await RewriteLogAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes a single scan, filter and deduplication cycle, and rewrites the logs when their period has elapsed.
    /// </summary>
    public async Task<int> ExecuteScanCycleAsync(CancellationToken cancellationToken = default)
    {
        var activeSockets = await socketScanner.ScanSocketsAsync(cancellationToken).ConfigureAwait(false);
        return await ProcessScannedSocketsAsync(activeSockets, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes every flow held in memory to the logs, replacing their previous content.
    /// </summary>
    /// <param name="cancellationToken">Cooperative cancellation token.</param>
    /// <returns>A task that ends when the logs hold the flows.</returns>
    /// <exception cref="IOException">When a log was not replaced; the message names it.</exception>
    /// <exception cref="UnauthorizedAccessException">When the folder of a log cannot be written.</exception>
    public Task RewriteLogAsync(CancellationToken cancellationToken = default)
    {
        _lastLogRewriteUtc = _timeProvider.GetUtcNow();
        return logRewriter.RewriteAsync(deduplicationRegister.GetAllEntries(), cancellationToken);
    }

    private async Task<int> ProcessScannedSocketsAsync(IReadOnlyList<ActiveSocketConnection> activeSockets, CancellationToken cancellationToken)
    {
        var host = hostProvider.GetCurrentHostMetadata();
        var localAddresses = hostProvider.GetLocalIpAddresses();
        var now = _timeProvider.GetUtcNow();
        var newlyDiscovered = new List<NetworkAuditEntry>();
        var excludedCount = 0;

        for (var i = 0; i < activeSockets.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = activeSockets[i];

            if (exclusionFilter.ShouldExclude(socket.Process) || networkFlowFilter.ShouldExclude(socket, localAddresses))
            {
                excludedCount++;
                continue;
            }

            var entry = new NetworkAuditEntry(now, now, 1, host, socket);

            if (_hostNameResolver.TryGetCachedHostName(socket.RemoteEndpoint.Address, out var cachedHost) && !string.IsNullOrWhiteSpace(cachedHost))
            {
                entry = entry with { RemoteHostName = cachedHost };
            }
            else
            {
                _hostNameResolver.QueueResolution(socket.RemoteEndpoint.Address);
            }

            if (deduplicationRegister.TryRegisterOrUpdate(entry, out var registered))
            {
                newlyDiscovered.Add(registered);
            }
        }

        if (now - _lastLogRewriteUtc >= logRewriteInterval)
        {
            await RewriteLogKeepingFlowsOnFailureAsync(cancellationToken).ConfigureAwait(false);
        }

        var result = new FlowAuditCycleResult(
            TimestampUtc: now,
            TotalSocketsScanned: activeSockets.Count,
            ExcludedSocketsCount: excludedCount,
            NewlyDiscoveredCount: newlyDiscovered.Count,
            TotalUniqueFlowsTracked: deduplicationRegister.UniqueCount,
            NewlyDiscoveredEntries: newlyDiscovered,
            ActiveSockets: activeSockets);

        ScanCycleCompleted?.Invoke(this, new FlowAuditCycleEventArgs(result));

        return newlyDiscovered.Count;
    }

    private async Task<IReadOnlyList<ActiveSocketConnection>?> ScanSocketsToleratingFailureAsync(int failures, CancellationToken cancellationToken)
    {
        try
        {
            return await socketScanner.ScanSocketsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && IsTransientScanFailure(ex))
        {
            // Stopping the audit can dispose what the scanner was using before it saw the token: the audit stops
            // as cancelled, which is what was asked, and writes its logs.
            throw new OperationCanceledException("The audit stopped during a scan.", ex, cancellationToken);
        }
        catch (Exception ex) when (IsTransientScanFailure(ex))
        {
            if (failures >= ConsecutiveScanFailureLimit)
            {
                throw new ConsecutiveScanFailuresException($"The audit stopped after {failures} failed scans in a row: {ex.Message}", ex);
            }

            ScanCycleFailed?.Invoke(this, new FlowAuditCycleFailedEventArgs(ex, failures, ConsecutiveScanFailureLimit));
            return null;
        }
    }

    // An audit runs unattended for weeks: a socket table that cannot be read for one cycle (SocketForge throws
    // Win32Exception, or InvalidOperationException when the table keeps growing between its size and read calls) or a
    // file held for a moment must not end it. Only failures that repeat cycle after cycle stop it. Any other exception
    // is a defect and stops the audit at once, as does any failure after the scan (log, dashboard, map): those are not
    // scan failures.
    private static bool IsTransientScanFailure(Exception ex) =>
        ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException;

    private async Task RewriteLogKeepingFlowsOnFailureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RewriteLogAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A log held open by another program (a spreadsheet, a backup) must not stop the audit: the flows stay
            // in memory and the next period, or the stop, writes them all again. A complete version left in
            // <log>.pending is applied when the next audit starts.
        }
    }

    private static IHostNameResolver InitializeHostNameResolver(
        ISocketScanner socketScanner,
        ProcessExclusionFilter exclusionFilter,
        INetworkFlowFilter networkFlowFilter,
        ConnectionDeduplicationRegister deduplicationRegister,
        IServerHostProvider hostProvider,
        AuditLogRewriter logRewriter,
        IHostNameResolver hostNameResolver)
    {
        ArgumentNullException.ThrowIfNull(socketScanner);
        ArgumentNullException.ThrowIfNull(exclusionFilter);
        ArgumentNullException.ThrowIfNull(networkFlowFilter);
        ArgumentNullException.ThrowIfNull(deduplicationRegister);
        ArgumentNullException.ThrowIfNull(hostProvider);
        ArgumentNullException.ThrowIfNull(logRewriter);
        ArgumentNullException.ThrowIfNull(hostNameResolver);

        hostNameResolver.HostNameResolved += (_, e) =>
        {
            deduplicationRegister.UpdateRemoteHostName(e.Address, e.HostName);
        };

        return hostNameResolver;
    }
}
