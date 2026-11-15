using AwesomeAssertions;
using PortAtlas.Cli.Audit;
using Xunit;

namespace PortAtlas.Tests.CommandLine;

public sealed class TopologyMapRefreshSchedulerTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task RequestRefresh_WhenRequestedWhileAGenerationRuns_RunsOneGenerationAtATimeAndCoalescesTheWaitingRequests()
    {
        // Arrange
        var failures = new List<Exception>();
        using var scheduler = new TopologyMapRefreshScheduler(failures.Add);
        var firstGenerationStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstGeneration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maximumRunning = 0;
        var generations = 0;

        async Task GenerateMapAsync(CancellationToken cancellationToken)
        {
            var nowRunning = Interlocked.Increment(ref running);
            maximumRunning = Math.Max(maximumRunning, nowRunning);
            if (Interlocked.Increment(ref generations) == 1)
            {
                firstGenerationStarted.SetResult();
                await releaseFirstGeneration.Task.WaitAsync(TestTimeout, cancellationToken);
            }

            Interlocked.Decrement(ref running);
        }

        // Act
        scheduler.RequestRefresh(GenerateMapAsync);
        await firstGenerationStarted.Task.WaitAsync(TestTimeout);
        scheduler.RequestRefresh(GenerateMapAsync);
        scheduler.RequestRefresh(GenerateMapAsync);
        scheduler.RequestRefresh(GenerateMapAsync);
        releaseFirstGeneration.SetResult();
        await scheduler.WaitForRefreshesAsync().WaitAsync(TestTimeout);

        // Assert
        maximumRunning.Should().Be(1);
        generations.Should().Be(2, "the requests made during the first generation are served by a single later one");
        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task RequestRefresh_WhenTheGenerationThrowsIOException_ReportsTheFailure()
    {
        // Arrange
        var failures = new List<Exception>();
        using var scheduler = new TopologyMapRefreshScheduler(failures.Add);

        // Act
        scheduler.RequestRefresh(_ => Task.FromException(new IOException("map.html is locked")));
        await scheduler.WaitForRefreshesAsync().WaitAsync(TestTimeout);

        // Assert
        failures.Should().ContainSingle().Which.Message.Should().Be("map.html is locked");
    }

    [Fact]
    public async Task GenerateExclusivelyAsync_WhenTheGenerationThrowsUnauthorizedAccessException_ReportsTheFailureWithoutThrowing()
    {
        // Arrange
        var failures = new List<Exception>();
        using var scheduler = new TopologyMapRefreshScheduler(failures.Add);

        // Act
        var generation = () => scheduler.GenerateExclusivelyAsync(_ => throw new UnauthorizedAccessException("map.html is read-only"));

        // Assert
        await generation.Should().NotThrowAsync();
        failures.Should().ContainSingle().Which.Message.Should().Be("map.html is read-only");
    }

    [Fact]
    public async Task GenerateExclusivelyAsync_WhenACycleRefreshRuns_WaitsForItBeforeGenerating()
    {
        // Arrange
        using var scheduler = new TopologyMapRefreshScheduler(_ => { });
        var cycleRefreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseCycleRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();

        scheduler.RequestRefresh(async cancellationToken =>
        {
            cycleRefreshStarted.SetResult();
            await releaseCycleRefresh.Task.WaitAsync(TestTimeout, cancellationToken);
            lock (order)
            {
                order.Add("cycle");
            }
        });
        await cycleRefreshStarted.Task.WaitAsync(TestTimeout);

        // Act
        var keyGeneration = scheduler.GenerateExclusivelyAsync(_ =>
        {
            lock (order)
            {
                order.Add("key");
            }

            return Task.CompletedTask;
        });
        releaseCycleRefresh.SetResult();
        await keyGeneration.WaitAsync(TestTimeout);

        // Assert
        order.Should().Equal("cycle", "key");
    }
}
