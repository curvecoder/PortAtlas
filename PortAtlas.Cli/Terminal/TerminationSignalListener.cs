using System.Runtime.InteropServices;

namespace PortAtlas.Cli.Terminal;

/// <summary>
/// Turns Ctrl+C (and SIGINT and SIGTERM on Linux and macOS) into a cancellation, so that a command saves its work
/// before it stops instead of being ended at once by the default handler.
/// </summary>
/// <remarks>
/// <c>Program</c> turns off the termination handling of System.CommandLine (<c>ProcessTerminationTimeout = null</c>),
/// so a command that has work to save listens for these signals itself, for as long as the listener is not disposed.
/// </remarks>
public sealed class TerminationSignalListener : IDisposable
{
    private readonly CancellationTokenSource _cancellation;
    private readonly PosixSignalRegistration? _sigInt;
    private readonly PosixSignalRegistration? _sigTerm;

    /// <summary>
    /// Starts listening.
    /// </summary>
    /// <param name="cancellation">Source cancelled when a termination signal arrives; it must outlive the listener.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="cancellation"/> is null.</exception>
    public TerminationSignalListener(CancellationTokenSource cancellation)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        _cancellation = cancellation;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            _sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
            _sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
        }

        Console.CancelKeyPress += OnCancelKeyPress;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _sigInt?.Dispose();
        _sigTerm?.Dispose();
    }

    private void OnPosixSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        _ = _cancellation.CancelAsync();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        _ = _cancellation.CancelAsync();
    }
}
