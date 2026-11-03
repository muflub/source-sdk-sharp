namespace SourceSharp.Ai;

/// <summary>
/// Where a session's inputs and outputs go as they happen (plan_hl2mp_ai
/// §2.6): the recorder writes them, <see cref="NoRecording"/> drops them.
/// </summary>
public interface IAiRecordSink
{
    void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels);

    void BotRemoved(ActorId bot);

    void Event(in AiEvent e);

    void Snapshot(AiSnapshot snapshot);

    void Intent(in IntentCommand intent);

    void UserCmd(in AiUserCmd cmd);

    /// <summary>Where a living actor stood at a tick (§3.4.4), humans and bots alike.</summary>
    void Frame(in AiActorFrame frame);

    /// <summary>An event about an actor no brain plays (a human's sight, §3.4.4): recorded, never fed to a brain.</summary>
    void Observed(in AiEvent e);
}

/// <summary>A sink that records nothing (no state, so one per session costs nothing).</summary>
public sealed class NoRecording : IAiRecordSink
{
    public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) { }

    public void BotRemoved(ActorId bot) { }

    public void Event(in AiEvent e) { }

    public void Snapshot(AiSnapshot snapshot) { }

    public void Intent(in IntentCommand intent) { }

    public void UserCmd(in AiUserCmd cmd) { }

    public void Frame(in AiActorFrame frame) { }

    public void Observed(in AiEvent e) { }
}
