using SourceSharp.Abi;
using SourceSharp.Server.Interface;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IDebugLog"/> over the server's own console, and a multiplexer
/// over its side's <see cref="ConsoleLines.Tap"/> (plan_devapi_interfaces §5.8, Q3).
///
/// <para>
/// <b>The tap is the side's console's</b> (statics to zero: it was the static
/// Log's process-wide sink), and this does not own it: the first subscription installs this instance's tap in front of whatever was
/// there, the tap passes every line on to that previous sink, and the last
/// unsubscription puts it back, unless something else has been installed
/// since. Two instances over two compositions each chain the other's tap, so
/// neither loses lines and neither can take the other's down.
/// </para>
/// </summary>
public sealed class DebugLog : IDebugLog
{
    private readonly ILog _console;
    private readonly ConsoleLines _lines;
    private readonly object _gate = new();
    private readonly Action<string, bool> _tap;
    private Action<string, bool>[] _subscribers = [];
    private Action<string, bool>? _previous;

    /// <param name="console">This composition's log, which the dev API's own lines print through.</param>
    /// <param name="lines">
    /// The lines this side's console prints, which a subscription taps
    /// (statics to zero: it was the static Log's tap).
    /// </param>
    public DebugLog(ILog console, ConsoleLines lines)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(lines);
        _console = console;
        _lines = lines;
        _tap = Dispatch;
    }

    public void Msg(string message) => _console.Msg(message);

    public void Warning(string message) => _console.Warning(message);

    /// <inheritdoc />
    public bool EngineFeedAttached => _lines.EngineFeedAttached;

    /// <inheritdoc />
    public string Capture(Action action) => _lines.Capture(action);

    /// <inheritdoc />
    public long ConsoleLinesLost => _lines.LostLines;

    /// <summary>How many subscriptions are live (for the tests).</summary>
    internal int Subscribers => Volatile.Read(ref _subscribers).Length;

    public IDisposable Subscribe(Action<string, bool> line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_gate)
        {
            if (_subscribers.Length == 0)
            {
                _previous = _lines.Tap;
                _lines.Tap = _tap;
            }

            Volatile.Write(ref _subscribers, [.. _subscribers, line]);
        }

        return new Subscription(this, line);
    }

    private void Unsubscribe(Action<string, bool> line)
    {
        lock (_gate)
        {
            int at = Array.IndexOf(_subscribers, line);
            if (at < 0)
                return;

            var left = _subscribers.ToList();
            left.RemoveAt(at);
            Volatile.Write(ref _subscribers, left.ToArray());

            if (left.Count > 0)
                return;

            if (_lines.Tap == _tap)
                _lines.Tap = _previous;

            _previous = null;
        }
    }

    private void Dispatch(string text, bool isWarning)
    {
        Volatile.Read(ref _previous)?.Invoke(text, isWarning);

        foreach (var line in Volatile.Read(ref _subscribers))
            line(text, isWarning);
    }

    private sealed class Subscription(DebugLog owner, Action<string, bool> line) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Unsubscribe(line);
        }
    }
}
