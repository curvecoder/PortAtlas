using System.Runtime.InteropServices;

namespace PortAtlas.Cli.Terminal;

/// <summary>
/// Turns Ctrl+C (and SIGINT and SIGTERM on Linux and macOS) into a cancellation, so that a command saves its work
/// before it stops instead of being ended at once by the default handler. The end of an input watched by a
/// <see cref="StopInputWatcher"/> is handled the same way.
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
    private readonly IDisposable? _inputEndClaim;
    private readonly object _stopGate = new();
    private bool _disposed;

    /// <summary>
    /// Starts listening, including for the end of the process input when <see cref="StopInputWatcher.Current"/> watches it.
    /// </summary>
    /// <param name="cancellation">Source cancelled when a termination signal arrives; it must outlive the listener.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="cancellation"/> is null.</exception>
    public TerminationSignalListener(CancellationTokenSource cancellation)
        : this(cancellation, StopInputWatcher.Current)
    {
    }

    /// <summary>
    /// Starts listening, and also treats the end of the input watched by <paramref name="stopInput"/> as a termination signal.
    /// </summary>
    /// <param name="cancellation">Source cancelled when a termination signal arrives; it must outlive the listener.</param>
    /// <param name="stopInput">Watcher whose input end stops the command, or null to listen for the signals only.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="cancellation"/> is null.</exception>
    /// <exception cref="InvalidOperationException">When another listener already claims the end of the input.</exception>
    public TerminationSignalListener(CancellationTokenSource cancellation, StopInputWatcher? stopInput)
    {
        ArgumentNullException.ThrowIfNull(cancellation);
        _cancellation = cancellation;

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            _sigInt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnPosixSignal);
            _sigTerm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnPosixSignal);
        }

        Console.CancelKeyPress += OnCancelKeyPress;

        // Claimed last: a claim taken before a registration that throws would never be released.
        _inputEndClaim = stopInput?.Claim(RequestStop);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_stopGate)
        {
            _disposed = true;
        }

        _inputEndClaim?.Dispose();
        Console.CancelKeyPress -= OnCancelKeyPress;
        _sigInt?.Dispose();
        _sigTerm?.Dispose();
    }

    // The owner disposes the source once the listener is disposed: a stop that arrives afterwards is too late to
    // matter and must not reach a released source.
    private void RequestStop()
    {
        lock (_stopGate)
        {
            if (!_disposed)
            {
                _ = _cancellation.CancelAsync();
            }
        }
    }

    private void OnPosixSignal(PosixSignalContext context)
    {
        context.Cancel = true;
        RequestStop();
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;
        RequestStop();
    }
}
