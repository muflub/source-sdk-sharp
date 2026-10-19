namespace Debug.Api.Server.Interfaces;

/// <summary>
/// What native's exit recorders saw (server/native/exit_watch.h), as data.
/// Times are milliseconds since the recorders were armed, on the clock
/// <see cref="NowMs"/> was read from; a count of zero leaves its fields zero.
/// </summary>
/// <param name="Armed">The recorders are installed.</param>
/// <param name="SignalsArmed">The SIGTERM / SIGINT / SIGHUP recorders are installed.</param>
/// <param name="SdlArmed">The SDL quit / window-close watch is installed (no SDL, no watch).</param>
/// <param name="PrevTerm">SIGTERM's disposition before the arm: 0 default, 1 ignored, 2 a handler, 3 an SA_SIGINFO handler.</param>
public sealed record DebugExitFacts(
    bool Armed, bool SignalsArmed, bool SdlArmed, int NowMs,
    int Signals, int FirstSigno, int FirstPid, int FirstUid, int FirstAtMs,
    int LastSigno, int LastPid, int LastUid, int LastAtMs,
    int SdlQuits, int SdlQuitAtMs, int WindowCloses, int WindowCloseAtMs,
    int PrevTerm, int PrevInt, int PrevHup);

/// <summary>
/// The process-level facts the dev API's exit record is built from. Native
/// records; the dev API decides what they mean.
/// </summary>
public interface IDebugProcess
{
    /// <summary>Installs the recorders (once per process) and names the file a fatal stop writes.</summary>
    void Arm(string recordPath);

    /// <summary>What the recorders have seen. Any thread.</summary>
    DebugExitFacts Read();

    /// <summary>
    /// Takes the recorders back out (the shutdown half of Arm). Call it once
    /// the exit record is written: the facts keep their meaning, but a
    /// recorder left installed would be code this assembly no longer owns
    /// once the module is unmapped.
    /// </summary>
    void Disarm();
}
