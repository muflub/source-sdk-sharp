namespace SourceSharp.Abi;

/// <summary>
/// Every line one engine side's console prints, offered to whoever is
/// listening (statics to zero: it was the static Log's tap and LogCapture's
/// thread-static buffer). Each <see cref="EngineSide"/> owns one; its
/// composition registers it, the dev API's log taps it and a hosting service's
/// console command captures through it.
///
/// <para>
/// <b>Two sources, one order</b> (console-batch, 2026-10-11). The engine's
/// prints arrive through the side's native console line buffer
/// (<see cref="NativeConsoleBuffer"/>), which this drains -- no crossing per
/// line -- on the side's frame (<see cref="Drain"/>), before each managed
/// print, at the end of a capture and after a suppressed echo. Managed prints
/// arrive through <see cref="Line"/>. Every line, from either source, takes
/// its number from the buffer's ONE counter, and a managed print takes its
/// number in the same lock hold that drains the buffer, so lines are handed
/// to <see cref="Tap"/> in number order: a reader that orders by arrival
/// orders by the number.
/// </para>
///
/// <para>
/// <b>Delivery is serialised.</b> <see cref="Tap"/> runs under this object's
/// lock (re-entrant: a tap that prints is queued behind the line it is
/// handling, never ahead of it), so a tap must not wait on another thread that
/// prints.
/// </para>
/// </summary>
public sealed class ConsoleLines
{
    private const int NotAManagedThread = 0;

    private readonly object _gate = new();
    private readonly Queue<Pending> _queue = new();
    private readonly List<ConsoleLineEntry> _drained = [];
    private readonly List<CaptureScope> _captures = [];
    private NativeConsoleBuffer? _buffer;
    private uint _localSequence;
    private bool _pumping;
    private Echo? _echo;
    private long _lost;

    /// <summary>
    /// The listener every line is handed to, with whether it was a warning.
    /// One slot: whoever sets it chains the previous one (DebugLog does).
    /// </summary>
    public Action<string, bool>? Tap { get; set; }

    /// <summary>
    /// Whether this side's engine prints reach <see cref="Tap"/> in process --
    /// set when the native forwarder hands over its line buffer
    /// (<see cref="AttachBuffer"/>). Consumers that would double-capture (the
    /// dev API's console tail) read it to pick one feed;
    /// <see cref="SuppressEcho"/> skips its bookkeeping while it is false.
    /// </summary>
    // Not under `_gate`: written once by AttachBuffer after it releases the
    // lock, and read unlocked by every managed thread that prints
    // (SuppressEcho, EngineSide.Msg) and by the dev API's thread. One word, so
    // `volatile` gives the cross-thread visibility; everything else is gated.
    private volatile bool _engineFeedAttached;

    public bool EngineFeedAttached
    {
        get => _engineFeedAttached;
        set => _engineFeedAttached = value;
    }

    /// <summary>
    /// The number the last line took: the buffer's counter once one is
    /// attached, this object's own before. Compare with
    /// <see cref="NativeConsoleBuffer.After"/>.
    /// </summary>
    public uint Sequence
    {
        get
        {
            lock (_gate)
                return CurrentSequence();
        }
    }

    /// <summary>
    /// How many engine lines the native buffer could not hold since this
    /// object was built. Zero in a healthy run; each loss is also said once as
    /// a warning line.
    /// </summary>
    public long LostLines => Interlocked.Read(ref _lost);

    /// <summary>
    /// The side's native console line buffer, handed over by the forwarder
    /// once per load (<c>ConsoleBufferAttach</c>). From here on engine prints
    /// arrive by <see cref="Drain"/>.
    /// </summary>
    public void AttachBuffer(NativeConsoleBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        lock (_gate)
            _buffer = buffer;
        EngineFeedAttached = true;
    }

    /// <summary>
    /// Hands every engine line buffered since the last drain to the listeners,
    /// in number order. Called on the side's frame, at unload, and wherever a
    /// reader needs the lines now.
    /// </summary>
    public void Drain()
    {
        lock (_gate)
        {
            DrainLocked(takeSequence: false);
            Pump();
        }
    }

    /// <summary>
    /// Runs <paramref name="forward"/> -- a print handed to the engine -- with
    /// the line delivered exactly once either way. With a display func
    /// attached the caller has already said the line (that is
    /// <c>EngineSide.Msg</c>'s shape), and the copy the engine prints back is
    /// consumed: the first line numbered after the caller's, before this call
    /// returns, with the same text -- an engine line, or a managed line from
    /// this thread. Another managed thread's line never matches, and a
    /// non-matching line stays in the stream. Without a display func nothing
    /// echoes back, so this delivers <paramref name="message"/> itself and
    /// forwards clean.
    /// </summary>
    public void SuppressEcho(string message, Action forward, bool isWarning = false)
    {
        ArgumentNullException.ThrowIfNull(forward);
        if (!EngineFeedAttached)
        {
            Line(message, isWarning);
            forward();
            return;
        }

        Echo echo;
        lock (_gate)
        {
            echo = new Echo(message, CurrentSequence(), Environment.CurrentManagedThreadId);
            _echo = echo;
        }

        try
        {
            // Not under the lock: the engine prints this synchronously into
            // the native buffer, and another thread's managed print must not
            // wait on the engine.
            forward();
        }
        finally
        {
            lock (_gate)
            {
                // The engine's copy is in the buffer now: drain it while the
                // echo is armed, then disarm.
                DrainLocked(takeSequence: false);
                Pump();
                if (ReferenceEquals(_echo, echo))
                    _echo = null;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> and answers what this console printed
    /// while it ran: every line numbered after the start and up to the end,
    /// drained before it answers. Nests: an outer capture also gets the
    /// inner one's lines.
    ///
    /// <para>
    /// <b>Scoped by number, not by thread</b> (console-batch ruling,
    /// 2026-10-11): a line another thread printed inside the range is in the
    /// answer. Engine lines carry no thread, so the old per-thread filter
    /// could not be kept for them.
    /// </para>
    /// </summary>
    public string Capture(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        CaptureScope scope;
        lock (_gate)
        {
            // Whatever was printed before the capture is handed on first, so
            // it can never be mistaken for the command's.
            DrainLocked(takeSequence: false);
            Pump();
            scope = new CaptureScope();
            _captures.Add(scope);
        }

        try
        {
            action();
        }
        finally
        {
            lock (_gate)
            {
                DrainLocked(takeSequence: false);
                Pump();
                _captures.Remove(scope);
            }
        }

        return scope.Text.ToString();
    }

    /// <summary>
    /// One line a managed print said. It takes
    /// the next number after every engine line buffered so far, which are
    /// delivered first.
    /// </summary>
    public void Line(string message, bool isWarning)
    {
        lock (_gate)
        {
            uint seq = DrainLocked(takeSequence: true);
            _queue.Enqueue(new Pending(new ConsoleLineEntry(seq, message, isWarning), Environment.CurrentManagedThreadId));
            Pump();
        }
    }

    private uint CurrentSequence() => _buffer?.Sequence ?? _localSequence;

    /// <summary>
    /// Queues every buffered engine line, then (when asked) the number a
    /// managed print takes in the same hold. Under <see cref="_gate"/>.
    /// </summary>
    private uint DrainLocked(bool takeSequence)
    {
        if (_buffer is null)
            return takeSequence ? ++_localSequence : 0;

        _drained.Clear();
        uint lost = _buffer.Drain(_drained, takeSequence, out uint seq);
        foreach (var entry in _drained)
            _queue.Enqueue(new Pending(entry, NotAManagedThread));
        _drained.Clear();

        if (lost > 0)
        {
            // Native only counts; saying so is this side's decision. The note
            // takes its own number, after the lines that did arrive.
            Interlocked.Add(ref _lost, lost);
            _queue.Enqueue(new Pending(new ConsoleLineEntry(_buffer.TakeSequence(),
                $"[SourceSharp] the console feed lost {lost} engine line(s): the native line buffer was full\n",
                IsWarning: true), NotAManagedThread));
        }

        return seq;
    }

    /// <summary>Delivers the queue in order. A re-entrant call leaves it to the outer loop.</summary>
    private void Pump()
    {
        if (_pumping)
            return;

        _pumping = true;
        try
        {
            while (_queue.TryDequeue(out var pending))
                Deliver(pending);
        }
        finally
        {
            _pumping = false;
        }
    }

    private void Deliver(Pending pending)
    {
        var entry = pending.Entry;

        // The echo bookkeeping first: the consumed copy must reach neither the
        // tap nor a capture (the managed print already reached both).
        if (_echo is { } echo &&
            NativeConsoleBuffer.After(entry.Sequence, echo.After) &&
            (pending.Thread == NotAManagedThread || pending.Thread == echo.Thread) &&
            Echoes(echo.Text, entry.Text))
        {
            _echo = null;
            return;
        }

        Tap?.Invoke(entry.Text, entry.IsWarning);

        // Every open capture takes the line: a capture opens and closes with a
        // drain under this lock, so what is delivered while it is open is
        // exactly what was numbered after its start and up to its end.
        foreach (var capture in _captures)
            capture.Text.Append(entry.Text);
    }

    /// <summary>
    /// Whether <paramref name="echo"/> is the line <paramref name="sent"/>
    /// produced: the same text, whether or not the display func kept the
    /// trailing newline.
    /// </summary>
    private static bool Echoes(string sent, string echo) =>
        string.Equals(sent.TrimEnd('\n'), echo.TrimEnd('\n'), StringComparison.Ordinal);

    private readonly record struct Pending(ConsoleLineEntry Entry, int Thread);

    private sealed record Echo(string Text, uint After, int Thread);

    private sealed class CaptureScope
    {
        public System.Text.StringBuilder Text { get; } = new();
    }
}
