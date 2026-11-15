namespace PortAtlas.Tests.Terminal;

// Console.In blocks in ReadLine until a line or the end arrives; this reader does the same, line by line. Disposing it
// ends the input, so that no reading thread is left blocked by a test.
internal sealed class ScriptedTextReader : TextReader
{
    private readonly object _gate = new();
    private readonly Queue<string> _lines = new();
    private bool _ended;
    private int _pendingReads;

    public void Send(string line)
    {
        lock (_gate)
        {
            _lines.Enqueue(line);
            _pendingReads++;
            Monitor.PulseAll(_gate);
        }
    }

    public void End()
    {
        lock (_gate)
        {
            _ended = true;
            _pendingReads++;
            Monitor.PulseAll(_gate);
        }
    }

    public async Task WaitUntilLinesAreReadAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Volatile.Read(ref _pendingReads) > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, CancellationToken.None);
        }

        // The reader acts on a line right after reading it: leave it the time to do so.
        await Task.Delay(100, CancellationToken.None);
    }

    public override string? ReadLine()
    {
        lock (_gate)
        {
            while (_lines.Count == 0 && !_ended)
            {
                Monitor.Wait(_gate);
            }

            _pendingReads = Math.Max(0, _pendingReads - 1);
            return _lines.Count > 0 ? _lines.Dequeue() : null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            End();
        }

        base.Dispose(disposing);
    }
}
