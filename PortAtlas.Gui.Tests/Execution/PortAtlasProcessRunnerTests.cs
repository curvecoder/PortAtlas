using AwesomeAssertions;
using PortAtlas.Gui.Core.Execution;

namespace PortAtlas.Gui.Tests.Execution;

public sealed class PortAtlasProcessRunnerTests
{
    private static readonly string ShellPath = OperatingSystem.IsWindows() ? "cmd.exe" : "sh";

    private static readonly TimeSpan ShortStopGracePeriod = TimeSpan.FromSeconds(2);

    private static PortAtlasProcessRunner ShellRunner() => new(new FixedExecutableLocator(ShellPath), ShortStopGracePeriod);

    private static PortAtlasProcessRunner PortAtlasRunner() =>
        new(new PortAtlasExecutableLocator(AppContext.BaseDirectory, _ => null));

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private static string[] ShellCommand(string windows, string unix) =>
        OperatingSystem.IsWindows() ? ["/c", windows] : ["-c", unix];

    [Fact]
    public async Task RunAsync_WhenProcessWritesToStdoutAndStderr_StreamsBothLines()
    {
        // Arrange
        var lines = new CollectedOutput();

        // Act
        var result = await ShellRunner().RunAsync(
            ShellCommand("echo out& echo err>&2", "echo out; echo err >&2"), lines, CancellationToken.None);

        // Assert
        result.ExitCode.Should().Be(0);
        lines.Lines.Should().Contain(new RunOutputLine(RunOutputStream.Standard, "out"));
        lines.Lines.Should().Contain(new RunOutputLine(RunOutputStream.Error, "err"));
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_TerminatesProcessAndThrowsOperationCanceledException()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var started = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var act = () => ShellRunner().RunAsync(
            ShellCommand("ping -n 60 127.0.0.1 >nul", "sleep 60"), new CollectedOutput(), cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_ClosesTheInputSoTheCommandCanFinishItsWork()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var lines = new CollectedOutput();

        // Act
        var act = () => ShellRunner().RunAsync(
            ShellCommand("more >nul& echo stopped", "cat >/dev/null; echo stopped"), lines, cancellation.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        lines.Lines.Should().Contain(new RunOutputLine(RunOutputStream.Standard, "stopped"));
    }

    [Fact]
    public async Task RunAsync_WhenAuditIsCancelled_LetsTheAuditShutDownGracefully()
    {
        // Arrange
        var directory = Directory.CreateTempSubdirectory("portatlas-gui-stop-");
        try
        {
            using var cancellation = new CancellationTokenSource();
            var lines = new CollectedOutput();
            string[] arguments =
                ["audit", "--no-tui", "--no-dns", "--interval", "1", "-o", Path.Combine(directory.FullName, "audit.csv")];

            // Act
            var run = PortAtlasRunner().RunAsync(arguments, lines, cancellation.Token);
            var started = await lines.WaitForLineAsync(line => line.Text.StartsWith("Starting continuous audit", StringComparison.Ordinal), StartTimeout);

            // The banner comes just before the audit listens for the stop; a stop before that ends the process at once,
            // as Ctrl+C would, and nothing is saved.
            await Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None);
            await cancellation.CancelAsync();
            var act = () => run;

            // Assert
            started.Should().BeTrue();
            await act.Should().ThrowAsync<OperationCanceledException>();
            lines.Lines.Should().Contain(new RunOutputLine(RunOutputStream.Standard, "Graceful shutdown of network flow audit."));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_WhenCommandFails_ReturnsExitCodeOne()
    {
        // Arrange
        var lines = new CollectedOutput();

        // Act
        var result = await PortAtlasRunner().RunAsync(["map", "--no-such-option"], lines, CancellationToken.None);

        // Assert
        result.ExitCode.Should().Be(1);
        lines.Lines.Should().Contain(line => line.Stream == RunOutputStream.Error);
    }

    private sealed class FixedExecutableLocator(string path) : IPortAtlasExecutableLocator
    {
        public string? ConfiguredPath { get; set; }

        public bool TryLocate(out string? located)
        {
            located = path;
            return true;
        }

        public string Locate() => path;
    }

    private sealed class CollectedOutput : IProgress<RunOutputLine>
    {
        private readonly List<RunOutputLine> _lines = [];
        private readonly List<(Func<RunOutputLine, bool> Match, TaskCompletionSource Seen)> _awaited = [];

        public IReadOnlyList<RunOutputLine> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        // Progress<T> would post to the thread pool and the lines could arrive after RunAsync returns.
        public void Report(RunOutputLine value)
        {
            lock (_lines)
            {
                _lines.Add(value);
                foreach (var (match, seen) in _awaited.Where(awaited => awaited.Match(value)))
                {
                    seen.TrySetResult();
                }
            }
        }

        public async Task<bool> WaitForLineAsync(Func<RunOutputLine, bool> match, TimeSpan timeout)
        {
            var seen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lines)
            {
                if (_lines.Exists(line => match(line)))
                {
                    return true;
                }

                _awaited.Add((match, seen));
            }

            var completed = await Task.WhenAny(seen.Task, Task.Delay(timeout, CancellationToken.None));
            return completed == seen.Task;
        }
    }
}
