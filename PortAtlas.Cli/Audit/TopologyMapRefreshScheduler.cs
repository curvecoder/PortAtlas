namespace PortAtlas.Cli.Audit;

/// <summary>
/// Runs the topology map generations of a continuous audit one at a time and reports their file errors, so that
/// two generations never write the same map file together and a failed write is shown instead of lost.
/// </summary>
/// <param name="reportFailure">Receives the <see cref="IOException"/> or <see cref="UnauthorizedAccessException"/> of a failed generation.</param>
internal sealed class TopologyMapRefreshScheduler(Action<Exception> reportFailure) : IDisposable
{
    private readonly SemaphoreSlim _generationGate = new(1, 1);
    private readonly object _refreshSync = new();
    private Func<CancellationToken, Task>? _pendingRefresh;
    private bool _refreshLoopRunning;
    private Task _refreshLoop = Task.CompletedTask;

    /// <summary>
    /// Queues a map generation without waiting for it. While a generation runs, later requests are merged into a
    /// single one that runs after it: the map is built from the flows known when it starts, so only the last request matters.
    /// </summary>
    /// <param name="generateMap">Builds and writes the map.</param>
    /// <param name="cancellationToken">Stops the queued generations when the audit stops.</param>
    public void RequestRefresh(Func<CancellationToken, Task> generateMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generateMap);

        lock (_refreshSync)
        {
            _pendingRefresh = generateMap;
            if (_refreshLoopRunning)
            {
                return;
            }

            _refreshLoopRunning = true;

            // The scan cycle raises the request: the generation runs on the thread pool so the next scan is not delayed.
            _refreshLoop = Task.Run(() => RunPendingRefreshesAsync(cancellationToken), CancellationToken.None);
        }
    }

    /// <summary>
    /// Runs a map generation once any running generation has finished, and reports its file error instead of throwing it.
    /// </summary>
    /// <param name="generateMap">Builds and writes the map.</param>
    /// <param name="cancellationToken">Cancels the wait and the generation.</param>
    /// <returns>A task that completes when the generation has finished or failed with a reported error.</returns>
    /// <exception cref="OperationCanceledException">The audit stopped before or during the generation.</exception>
    public async Task GenerateExclusivelyAsync(Func<CancellationToken, Task> generateMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generateMap);

        await _generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await generateMap(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A locked or read-only map file must not stop the audit nor the keyboard loop: the operator sees the
            // error and the previous map stays on disk until the next generation succeeds.
            reportFailure(ex);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    /// <summary>
    /// Waits for the queued map generations to finish, so that none still writes when the audit returns.
    /// </summary>
    /// <returns>A task that completes when no queued generation is left.</returns>
    public async Task WaitForRefreshesAsync()
    {
        Task refreshLoop;
        lock (_refreshSync)
        {
            refreshLoop = _refreshLoop;
        }

        try
        {
            await refreshLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The queued generations only receive the audit token: a cancellation here is the audit stopping, which
            // the caller is already handling. A generation cut short leaves the previous map on disk.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _generationGate.Dispose();

    private async Task RunPendingRefreshesAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Func<CancellationToken, Task>? nextRefresh;
            lock (_refreshSync)
            {
                nextRefresh = _pendingRefresh;
                _pendingRefresh = null;
                if (nextRefresh is null)
                {
                    _refreshLoopRunning = false;
                    return;
                }
            }

            await GenerateExclusivelyAsync(nextRefresh, cancellationToken).ConfigureAwait(false);
        }
    }
}
