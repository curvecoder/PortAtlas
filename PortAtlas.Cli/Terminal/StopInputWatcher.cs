namespace PortAtlas.Cli.Terminal;

/// <summary>
/// Reads an input until it ends and turns that end into a stop request, so that a launcher that redirects the input
/// of the process stops the command by closing it, as Ctrl+C would.
/// </summary>
/// <remarks>
/// A command that saves its work at the stop claims the end of the input (<see cref="TerminationSignalListener"/>);
/// without a claim, the end runs the action given at construction, which ends the process like the default Ctrl+C
/// handler. The input is read by one watcher only for the whole process, so that two readers never share it.
/// </remarks>
public sealed class StopInputWatcher
{
    /// <summary>
    /// Name of the environment variable that a launcher sets to <c>1</c> to stop the command by closing its input.
    /// </summary>
    /// <remarks>
    /// The end of the input is not a stop request by default: a service manager (systemd, NSSM) or a scheduled task
    /// can start the command with an input that is empty from the start, and the audit would stop at once.
    /// </remarks>
    public const string StopOnInputEndVariable = "PORTATLAS_STOP_ON_INPUT_END";

    private readonly object _gate = new();
    private readonly Action _unclaimedEnd;
    private Action? _claimedEnd;
    private bool _ended;

    /// <summary>
    /// Starts reading <paramref name="input"/> on a background thread; its lines are read and ignored.
    /// </summary>
    /// <param name="input">Input whose end is a stop request.</param>
    /// <param name="unclaimedEnd">Action run when the input ends while no command claims the end.</param>
    /// <exception cref="ArgumentNullException">When an argument is null.</exception>
    public StopInputWatcher(TextReader input, Action unclaimedEnd)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(unclaimedEnd);
        _unclaimedEnd = unclaimedEnd;

        // Console.In blocks in ReadLine even through ReadLineAsync, and the read cannot be cancelled: a background
        // thread waits for the end of the input and is left blocked when the command ends first.
        var reader = new Thread(() => WatchUntilEnd(input))
        {
            IsBackground = true,
            Name = "PortAtlas stop input"
        };
        reader.Start();
    }

    /// <summary>
    /// Gets the watcher of the process input, or null when the launcher did not ask for a stop by the input.
    /// </summary>
    public static StopInputWatcher? Current { get; private set; }

    /// <summary>
    /// Starts watching the process input when it is redirected and <see cref="StopOnInputEndVariable"/> is <c>1</c>.
    /// </summary>
    /// <param name="unclaimedEnd">Action run when the input ends while no command claims the end.</param>
    /// <exception cref="ArgumentNullException">When <paramref name="unclaimedEnd"/> is null.</exception>
    public static void StartIfRequested(Action unclaimedEnd)
    {
        ArgumentNullException.ThrowIfNull(unclaimedEnd);
        if (Current is not null
            || !Console.IsInputRedirected
            || !string.Equals(Environment.GetEnvironmentVariable(StopOnInputEndVariable), "1", StringComparison.Ordinal))
        {
            return;
        }

        Current = new StopInputWatcher(Console.In, unclaimedEnd);
    }

    /// <summary>
    /// Runs <paramref name="onEnd"/> instead of the unclaimed action when the input ends, until the claim is disposed.
    /// </summary>
    /// <param name="onEnd">Action run when the input ends; run at once when the input has already ended.</param>
    /// <returns>The claim, to dispose when the command no longer handles the stop.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="onEnd"/> is null.</exception>
    /// <exception cref="InvalidOperationException">When another claim is active.</exception>
    public IDisposable Claim(Action onEnd)
    {
        ArgumentNullException.ThrowIfNull(onEnd);
        lock (_gate)
        {
            if (_claimedEnd is not null)
            {
                throw new InvalidOperationException("The end of the input is already claimed by another command.");
            }

            if (!_ended)
            {
                _claimedEnd = onEnd;
                return new EndClaim(this, onEnd);
            }
        }

        onEnd();
        return new EndClaim(this, onEnd);
    }

    private void Release(Action onEnd)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_claimedEnd, onEnd))
            {
                _claimedEnd = null;
            }
        }
    }

    private void WatchUntilEnd(TextReader input)
    {
        try
        {
            while (input.ReadLine() is not null)
            {
                // The launcher stops the command by closing the input: what it writes before is not a request.
            }
        }
        catch (IOException)
        {
            // An input that can no longer be read means the launcher is gone: the command stops as if the input had
            // been closed, so that its work is saved instead of running unattended.
        }

        Action onEnd;
        lock (_gate)
        {
            _ended = true;
            onEnd = _claimedEnd ?? _unclaimedEnd;
        }

        onEnd();
    }

    private sealed class EndClaim(StopInputWatcher owner, Action onEnd) : IDisposable
    {
        public void Dispose() => owner.Release(onEnd);
    }
}
