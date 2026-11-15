using System.Diagnostics;
using System.Text;
using PortAtlas.Cli.Terminal;

namespace PortAtlas.Gui.Core.Execution;

/// <summary>
/// Runs the PortAtlas executable as a child process, without a shell.
/// </summary>
/// <param name="locator">Finds the executable.</param>
/// <param name="stopGracePeriod">Time a cancelled command is given to stop by itself before its process tree is killed.</param>
public sealed class PortAtlasProcessRunner(IPortAtlasExecutableLocator locator, TimeSpan stopGracePeriod) : IPortAtlasRunner
{
    /// <summary>
    /// Runs the executable found by <paramref name="locator"/>, with the default time to stop.
    /// </summary>
    /// <param name="locator">Finds the executable.</param>
    public PortAtlasProcessRunner(IPortAtlasExecutableLocator locator)
        : this(locator, DefaultStopGracePeriod)
    {
    }

    /// <summary>
    /// Time a cancelled command is given by default to write its logs and caches before its process tree is killed.
    /// </summary>
    public static readonly TimeSpan DefaultStopGracePeriod = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task<RunResult> RunAsync(IReadOnlyList<string> arguments, IProgress<RunOutputLine> output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var startInfo = new ProcessStartInfo(locator.Locate())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment[StopInputWatcher.StopOnInputEndVariable] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var readers = Task.WhenAll(
            ForwardLinesAsync(process.StandardOutput, RunOutputStream.Standard, output),
            ForwardLinesAsync(process.StandardError, RunOutputStream.Error, output));

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process).ConfigureAwait(false);
            await readers.ConfigureAwait(false);
            throw;
        }

        await readers.ConfigureAwait(false);
        return new RunResult(process.ExitCode);
    }

    // Closing the input stops the command as Ctrl+C would, so that it writes its logs and caches before it exits. A
    // command still running after the grace period (stuck, or an executable that ignores its input) is killed with
    // the processes it started, and its unsaved work is lost.
    private async Task StopAsync(Process process)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // The command has already exited and its input pipe is gone: the wait below returns at once.
        }

        using var grace = new CancellationTokenSource(stopGracePeriod);
        try
        {
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            return;
        }
        catch (OperationCanceledException) when (grace.IsCancellationRequested)
        {
            // The grace period is over and the command is still running: it is killed below.
        }

        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task ForwardLinesAsync(StreamReader reader, RunOutputStream stream, IProgress<RunOutputLine> output)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            output.Report(new RunOutputLine(stream, line));
        }
    }
}
