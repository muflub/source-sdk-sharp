using SourceSharp.Abi;

namespace SourceSharp.Client;

/// <summary>
/// What the client module has counted: frames this level, user commands since
/// load, and the stage notifications the engine has raised.
/// </summary>
public interface IClientFrameStats
{
    /// <summary>Frames since the last level init.</summary>
    int FrameCount { get; }

    /// <summary>User commands C# has processed since load.</summary>
    int CommandCount { get; }

    /// <summary>
    /// The stage the engine last told the module it had reached, or
    /// <see cref="ClientFrameStage.Undefined"/> before the first.
    /// </summary>
    ClientFrameStage CurrentStage { get; }

    /// <summary>How many times one stage has been raised.</summary>
    long StageCount(ClientFrameStage stage);
}

/// <summary>
/// The client module's counters, registered in its container.
///
/// <para>
/// They were statics on <c>ClientModule</c> with static accessors, so that a
/// command class could read them without a reference to the module, and so
/// every test that drove <c>OnStage</c> shared one set and ran serially. A
/// command takes this through its constructor now, and each module (each
/// test's) has its own.
/// </para>
/// </summary>
public sealed class ClientFrameStats : IClientFrameStats
{
    /// <inheritdoc/>
    public int FrameCount { get; private set; }

    /// <inheritdoc/>
    public int CommandCount { get; private set; }

    /// <summary>Whether the first +jump has been logged.</summary>
    public bool LoggedFirstJump { get; set; }

    /// <inheritdoc/>
    public ClientFrameStage CurrentStage { get; private set; } = ClientFrameStage.Undefined;

    /// <summary>
    /// How many stage notifications have arrived since load, per stage.
    /// Indexed by <c>(int)stage</c>, so <see cref="ClientFrameStage.Undefined"/>
    /// has no cell — the engine never raises it as a notification, and a
    /// negative index would be the one bug this array could have.
    /// </summary>
    private readonly long[] _stageCounts = new long[7];

    /// <inheritdoc/>
    public long StageCount(ClientFrameStage stage)
    {
        int i = (int)stage;
        return (uint)i < (uint)_stageCounts.Length ? _stageCounts[i] : 0;
    }

    /// <summary>One more frame.</summary>
    public void CountFrame() => FrameCount++;

    /// <summary>Frames start again at a level init.</summary>
    public void ResetFrames() => FrameCount = 0;

    /// <summary>One more user command; answers the new count.</summary>
    public int CountCommand() => ++CommandCount;

    /// <summary>The engine has reached <paramref name="stage"/>.</summary>
    public void NoteStage(ClientFrameStage stage)
    {
        CurrentStage = stage;

        int i = (int)stage;
        if ((uint)i < (uint)_stageCounts.Length)
            _stageCounts[i]++;
    }

    /// <summary>Forgets every stage count and the current stage.</summary>
    public void ResetStages()
    {
        Array.Clear(_stageCounts);
        CurrentStage = ClientFrameStage.Undefined;
    }
}
