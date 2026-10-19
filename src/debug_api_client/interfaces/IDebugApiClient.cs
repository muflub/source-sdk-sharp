namespace Debug.Api.Client.Interfaces;

/// <summary>
/// The client's half of the dev API, as the client module sees it
/// (plan_side_isolation §2.6). The module loads its assembly by name only when
/// the process was started with the client's debug flag, hands it the
/// invitation the server sends, and disposes it at every level shutdown.
/// </summary>
public interface IDebugApiClient : IDisposable
{
    /// <summary>
    /// Reads an invitation the server sent as it arrived, whole, and dials the
    /// server's dev API on its port in the background. A malformed one is
    /// refused here, with the reason logged: the game only moves the bytes.
    /// </summary>
    void Attach(ReadOnlySpan<byte> invitation);

    /// <summary>The client's reload-store section <see cref="SaveResume"/>'s bytes travel in.</summary>
    const string ResumeReloadSection = "debug-api.debug-channel.resume";

    /// <summary>
    /// The bytes to carry across this client's managed reload so the reloaded
    /// module can redial without an invitation, or null when there is nothing
    /// to resume. Opaque to the game: it stores them and hands them back.
    /// </summary>
    byte[]? SaveResume();

    /// <summary>
    /// Redials with bytes <see cref="SaveResume"/> produced before a reload. A
    /// fresh invitation (<see cref="Attach"/>) replaces whatever this started.
    /// </summary>
    void Resume(ReadOnlySpan<byte> carried);

    /// <summary>
    /// Answers the questions waiting for the game thread: the client module
    /// calls this once per frame, on the game thread, so a question that reads
    /// the game's state never reads it from the socket's thread.
    /// </summary>
    void Pump();
}
