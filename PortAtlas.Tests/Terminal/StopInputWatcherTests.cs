using AwesomeAssertions;
using PortAtlas.Cli.Terminal;
using Xunit;

namespace PortAtlas.Tests.Terminal;

public sealed class StopInputWatcherTests
{
    private static readonly TimeSpan EndTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Constructor_WhenInputEndsWithoutClaim_RunsTheUnclaimedEndAction()
    {
        // Arrange
        using var input = new ScriptedTextReader();
        var unclaimedEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = new StopInputWatcher(input, () => unclaimedEnd.TrySetResult());

        // Act
        input.End();

        // Assert
        (await CompletesInTimeAsync(unclaimedEnd.Task)).Should().BeTrue();
    }

    [Fact]
    public async Task Claim_WhenInputEnds_RunsTheClaimInsteadOfTheUnclaimedEndAction()
    {
        // Arrange
        using var input = new ScriptedTextReader();
        var unclaimedEndRan = false;
        var claimedEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new StopInputWatcher(input, () => unclaimedEndRan = true);
        using var claim = watcher.Claim(() => claimedEnd.TrySetResult());

        // Act
        input.End();

        // Assert
        (await CompletesInTimeAsync(claimedEnd.Task)).Should().BeTrue();
        unclaimedEndRan.Should().BeFalse();
    }

    [Fact]
    public async Task Claim_WhenDisposedBeforeTheInputEnds_LetsTheUnclaimedEndActionRun()
    {
        // Arrange
        using var input = new ScriptedTextReader();
        var claimedEndRan = false;
        var unclaimedEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new StopInputWatcher(input, () => unclaimedEnd.TrySetResult());
        var claim = watcher.Claim(() => claimedEndRan = true);

        // Act
        claim.Dispose();
        input.End();

        // Assert
        (await CompletesInTimeAsync(unclaimedEnd.Task)).Should().BeTrue();
        claimedEndRan.Should().BeFalse();
    }

    [Fact]
    public async Task Claim_WhenInputHasAlreadyEnded_RunsTheClaimAtOnce()
    {
        // Arrange
        using var input = new ScriptedTextReader();
        var unclaimedEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new StopInputWatcher(input, () => unclaimedEnd.TrySetResult());
        input.End();
        (await CompletesInTimeAsync(unclaimedEnd.Task)).Should().BeTrue();
        var claimedEndRan = false;

        // Act
        using var claim = watcher.Claim(() => claimedEndRan = true);

        // Assert
        claimedEndRan.Should().BeTrue();
    }

    [Fact]
    public void Claim_WhenAnotherClaimIsActive_ThrowsInvalidOperationException()
    {
        // Arrange
        using var input = new ScriptedTextReader();
        var watcher = new StopInputWatcher(input, () => { });
        using var claim = watcher.Claim(() => { });

        // Act
        var act = () => watcher.Claim(() => { });

        // Assert
        act.Should().Throw<InvalidOperationException>();
        input.End();
    }

    private static async Task<bool> CompletesInTimeAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Task.Delay(EndTimeout, CancellationToken.None));
        return completed == task;
    }
}
