using System.Reflection;
using System.Text;

namespace Debug.Api;

/// <summary>
/// Follows the game's own console log — the fallback feed.
///
/// With a side's console-display binding attached (docs/todo.md's
/// console-capture item, closed 2026-10-09) the engine's prints reach the
/// ring in process and <see cref="DebugApiServer"/> disposes its tail:
/// this file is for the sides that have no display func — tests, no-engine
/// runs, and a game whose managed areas never loaded — plus the lines the
/// engine prints before area acquire.
///
/// The file still earns its keep there: -condebug writes the whole console
/// as it happens, reading it costs nothing on the game thread, needs no
/// engine call this build does not have, and works for native and managed
/// output alike.
///
/// Deliberately a separate feed from the managed ring rather than merged
/// into it. Managed lines reach the console too, so merging would show
/// each of them twice; guessing which console line was which managed line
/// is the kind of heuristic that is wrong exactly when the log matters
/// most.
/// </summary>
public sealed class ConsoleTail : IDisposable
{
    /// <summary>What -condebug calls the file it writes.</summary>
    public const string FileName = "console.log";

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// How often the file is re-read: <see cref="DefaultPollInterval"/> in the
    /// game. A test that waits for a poll to notice a write sets it short, so it
    /// waits for the same poll sooner.
    /// </summary>
    internal TimeSpan PollInterval { get; init; } = DefaultPollInterval;

    private readonly LogRing _ring;
    private readonly string _path;
    private readonly CancellationTokenSource _stop = new();

    private long _position;
    private Thread? _thread;

    /// <summary>Where it is reading from. Worth reporting: the usual failure is looking in the wrong place.</summary>
    public string Path => _path;

    /// <summary>
    /// Whether there is actually a file being followed.
    ///
    /// Answered honestly, and re-answered as it changes: -condebug creates the
    /// log when the game first writes to it, and it can be deleted underneath
    /// this. The page shows an empty feed either way, and the only thing that
    /// tells the two apart — nothing has been logged, or nothing is being read —
    /// is this flag.
    /// </summary>
    public bool IsFollowing { get; private set; }

    /// <summary>Follows the log where <paramref name="layout"/> says the mod keeps it.</summary>
    internal ConsoleTail(LogRing ring, ModLayout layout, Action<string> warn, string? conLogFile = null)
        : this(ring, DefaultPath(layout, conLogFile), warn)
    {
    }

    /// <summary>Follows the log at <paramref name="path"/>, named outright.</summary>
    /// <param name="warn">Where the tail reports its own trouble: the server's console (IDebugLog).</param>
    public ConsoleTail(LogRing ring, string path, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);
        _ring = ring;
        _path = path;
        _warn = warn;
    }

    private readonly Action<string> _warn;

    /// <summary>
    /// The mod's console log. The assembly sits in &lt;mod&gt;/bin/managed and
    /// the engine writes the log to the mod directory, so it is two levels up --
    /// not the working directory, which is the Steam install.
    /// </summary>
    /// <summary>Names the console log outright, for a layout this cannot deduce.</summary>
    public const string PathVariable = "SOURCESHARP_CONSOLE_LOG";

    /// <summary>
    /// The file: the environment's, else the one <c>+con_logfile</c> moved
    /// the engine's console log to (relative to the mod, as the engine opens
    /// it), else <c>console.log</c> in the mod.
    ///
    /// <para>
    /// <c>+con_logfile</c> REPLACES console.log for the rest of the run
    /// (measured under make headless), and a probe that runs beside a gate
    /// moves its log there on purpose so the two games do not share one file
    /// (tools/3f-gatebisect). Following console.log then reads the OTHER game.
    /// </para>
    /// </summary>
    internal static string DefaultPath(ModLayout layout, string? conLogFile = null)
    {
        string? configured = Environment.GetEnvironmentVariable(PathVariable);

        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string mod = layout.ModDirectory;
        string file = string.IsNullOrWhiteSpace(conLogFile) ? FileName : conLogFile;

        return System.IO.Path.IsPathRooted(file) || mod.Length == 0 ? file : System.IO.Path.Combine(mod, file);
    }

    /// <summary>
    /// Starts following, from the end of the file.
    ///
    /// From the end rather than the start because the log survives across runs:
    /// beginning at the top would replay a previous session as though it were
    /// happening now, which is worse than missing the startup spew that had
    /// already been written before this got a chance to look.
    /// </summary>
    public void Start()
    {
        try
        {
            IsFollowing = File.Exists(_path);
            _position = IsFollowing ? new FileInfo(_path).Length : 0;
        }
        catch
        {
            // Unreadable rather than absent, which is the same to a reader: the
            // loop below picks it up if that changes.
            IsFollowing = false;
            _position = 0;
        }

        _thread = new Thread(Follow)
        {
            IsBackground = true,
            Name = "SourceSharp DebugApi console tail",
        };

        _thread.Start();
    }

    /// <summary>
    /// Stops following, and WAITS for the thread to actually stop.
    ///
    /// <para>
    /// The join is the whole point and it used to be missing. A running thread
    /// keeps every assembly on its stack alive, so a background thread of an
    /// unloading side pins that side's collectible load context — and the
    /// symptom is not an error: it is a hot reload that silently leaks the old
    /// code and keeps it in memory. That is exactly what
    /// <c>Entry.UnloadModule</c>'s "did NOT unload" warning was reporting on the
    /// server, and only on the server, because the client has no dev API.
    /// </para>
    ///
    /// <para>
    /// Bounded rather than indefinite, because the game thread is what calls
    /// this and a shutdown that hangs is worse than a leak. The wait handle is
    /// signalled by the cancellation, so the loop notices within one iteration
    /// and this returns in microseconds; the second is for the case where it
    /// does not, and it says so rather than carrying on quietly.
    /// </para>
    /// </summary>
    /// <summary>
    /// Reads the file now rather than at the next poll: a caller waiting for a
    /// line it knows was just written (a command capture's end marker) need not
    /// wait out the interval.
    /// </summary>
    public void Poke()
    {
        try { _poke.Set(); }
        catch (ObjectDisposedException) { }
    }

    private readonly AutoResetEvent _poke = new(false);

    public void Dispose()
    {
        _stop.Cancel();
        IsFollowing = false;

        var thread = _thread;
        _thread = null;

        if (thread is not null && !thread.Join(TimeSpan.FromSeconds(1)))
        {
            _warn("[SourceSharp] the console tail thread did not stop; this side's " +
                        "load context cannot unload and a reload will leak it\n");
        }
    }

    /// <summary>
    /// One last read, on the caller's thread, for what reached the file after
    /// the thread's final poll -- the exit record's look for a typed quit.
    /// Only after <see cref="Dispose"/>, when no other thread reads.
    /// </summary>
    public void DrainAfterDispose()
    {
        if (_thread is not null)
            return;

        try { ReadNew(); }
        catch { /* the same race the loop shrugs off; the record goes without it */ }
    }

    private void Follow()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                ReadNew();
            }
            catch
            {
                // The engine holds this file open and writes to it constantly. A
                // read that loses a race is normal and the next poll gets it.
            }

            WaitHandle.WaitAny([_stop.Token.WaitHandle, _poke], PollInterval);
        }
    }

    private void ReadNew()
    {
        if (!File.Exists(_path))
        {
            // No file yet is not a failure: -condebug creates it when the game
            // first writes to it, and this notices when it appears.
            IsFollowing = false;
            return;
        }

        IsFollowing = true;

        // Opened shared for everything, because the engine has it open for
        // writing and an exclusive open would simply fail forever.
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        // A shorter file than last time means it was replaced -- a new run of the
        // game, most likely. Seeking to the old position would land in the middle
        // of a line, or past the end.
        if (stream.Length < _position)
            _position = 0;

        if (stream.Length == _position)
            return;

        stream.Seek(_position, SeekOrigin.Begin);

        var buffer = new byte[stream.Length - _position];
        int read = stream.Read(buffer, 0, buffer.Length);

        if (read <= 0)
            return;

        string text = Encoding.UTF8.GetString(buffer, 0, read);

        // A trailing partial line is left for next time. Emitting it now would
        // split one console line across two entries, and the half of a warning
        // that arrived first is not a warning.
        int lastBreak = text.LastIndexOf('\n');

        if (lastBreak < 0)
            return;

        foreach (string line in text[..lastBreak].Split('\n'))
            _ring.Add(line, IsWarningLine(line));

        _position += Encoding.UTF8.GetByteCount(text[..(lastBreak + 1)]);
    }

    /// <summary>
    /// Whether a console line reads as a problem.
    ///
    /// The file records no severity -- Warning and Msg both end up as plain text
    /// -- so this is a guess from the words the engine uses. It is only used for
    /// colour, and a missed warning is still there to read.
    /// </summary>
    internal static bool IsWarningLine(string line)
        => line.Contains("Warning", StringComparison.OrdinalIgnoreCase)
        || line.Contains("Error", StringComparison.OrdinalIgnoreCase)
        || line.Contains("ERROR:", StringComparison.Ordinal)
        || line.Contains("failed", StringComparison.OrdinalIgnoreCase);
}
