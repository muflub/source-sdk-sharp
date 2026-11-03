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
}
