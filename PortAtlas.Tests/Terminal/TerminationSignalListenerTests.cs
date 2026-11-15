using AwesomeAssertions;
using PortAtlas.Cli.Terminal;
using Xunit;

namespace PortAtlas.Tests.Terminal;

public sealed class TerminationSignalListenerTests
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Constructor_WhenStopInputReachesItsEnd_CancelsTheSource()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var stopInput = new ScriptedTextReader();
        var watcher = new StopInputWatcher(stopInput, () => { });

        // Act
        using var listener = new TerminationSignalListener(cancellation, watcher);
        stopInput.End();

        // Assert
        (await WaitForCancellationAsync(cancellation.Token)).Should().BeTrue();
    }

    [Fact]
    public async Task Constructor_WhenStopInputReceivesLinesWithoutEnding_DoesNotCancelTheSource()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var stopInput = new ScriptedTextReader();
        var watcher = new StopInputWatcher(stopInput, () => { });

        // Act
        using var listener = new TerminationSignalListener(cancellation, watcher);
        stopInput.Send("homelab");
        await stopInput.WaitUntilLinesAreReadAsync(StopTimeout);

        // Assert
        cancellation.IsCancellationRequested.Should().BeFalse();
        stopInput.End();
        (await WaitForCancellationAsync(cancellation.Token)).Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_WhenStopInputEndsAfterwards_LeavesTheSourceUncancelled()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        using var stopInput = new ScriptedTextReader();
        var unclaimedEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var listener = new TerminationSignalListener(cancellation, new StopInputWatcher(stopInput, () => unclaimedEnd.TrySetResult()));

        // Act
        listener.Dispose();
        stopInput.End();
        var endHandled = await Task.WhenAny(unclaimedEnd.Task, Task.Delay(StopTimeout, CancellationToken.None)) == unclaimedEnd.Task;

        // Assert
        endHandled.Should().BeTrue();
        cancellation.IsCancellationRequested.Should().BeFalse();
    }

    private static async Task<bool> WaitForCancellationAsync(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = token.Register(() => cancelled.TrySetResult());
        var completed = await Task.WhenAny(cancelled.Task, Task.Delay(StopTimeout, CancellationToken.None));
        return completed == cancelled.Task;
    }
}
