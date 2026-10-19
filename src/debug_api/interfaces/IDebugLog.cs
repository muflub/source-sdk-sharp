namespace Debug.Api.Server.Interfaces;

/// <summary>
/// The server's console, as its dev API reaches it (group G9).
/// </summary>
public interface IDebugLog
{
    /// <summary>Prints <paramref name="message"/> to the server's console.</summary>
    void Msg(string message);

    /// <summary>Prints <paramref name="message"/> to the server's console as a warning.</summary>
    void Warning(string message);

    /// <summary>
    /// Calls <paramref name="line"/> with every managed line logged from now
    /// on (the text, and whether it was a warning), from whichever thread
    /// logged it, until the result is disposed. A subscription, never a
    /// replaced sink: two subscribers both see every line.
    /// </summary>
    IDisposable Subscribe(Action<string, bool> line);

    /// <summary>
    /// Whether this side's engine hands its console prints to managed code
    /// itself (the display-func feed is attached), so the dev API can tell
    /// the in-process ring from the -condebug file tail behind it.
    /// </summary>
    bool EngineFeedAttached { get; }

    /// <summary>
    /// Runs <paramref name="action"/> as a console capture (console-batch):
    /// the engine lines buffered before it are handed to every subscriber
    /// first, and the ones it printed before this returns, so a reader that
    /// marks its own feed inside the action and again after this call holds
    /// exactly the lines numbered inside the range -- from any thread.
    /// Answers the captured text.
    /// </summary>
    string Capture(Action action);

    /// <summary>
    /// How many engine console lines the native line buffer could not hold
    /// (console-batch). Zero in a healthy run.
    /// </summary>
    long ConsoleLinesLost { get; }
}
