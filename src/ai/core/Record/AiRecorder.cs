namespace SourceSharp.Ai;

/// <summary>
/// Writes a session's inputs (bots and their seeds and skills, events,
/// snapshots), its outputs (intents) and every player's usercmds to an
/// <c>.airec</c> stream (plan_hl2mp_ai §2.6). The stream is the caller's.
/// </summary>
public sealed class AiRecorder : IAiRecordSink
{
    private readonly Stream _out;
    private readonly MemoryStream _payload = new();
    private readonly BinaryWriter _w;

    public AiRecorder(Stream output, in AiRecordingHeader header)
    {
        ArgumentNullException.ThrowIfNull(output);
        _out = output;
        _w = new BinaryWriter(_payload);
        AiRecordFormat.WriteFileHeader(_out);
        AiRecordFormat.Write(Begin(), header);
        End(AiRecordType.Header);
    }

    /// <summary>How many records have been written, the header included.</summary>
    public int Records { get; private set; }

    public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels)
    {
        BinaryWriter w = Begin();
        AiRecordFormat.Write(w, bot);
        w.Write(seed);
        w.Write((ushort)skillLevels.Length);
        w.Write(skillLevels);
        End(AiRecordType.BotAdded);
    }

    public void BotRemoved(ActorId bot)
    {
        AiRecordFormat.Write(Begin(), bot);
        End(AiRecordType.BotRemoved);
    }

    public void Event(in AiEvent e)
    {
        AiRecordFormat.Write(Begin(), e);
        End(AiRecordType.Event);
    }

    public void Snapshot(AiSnapshot snapshot)
    {
        AiRecordFormat.Write(Begin(), snapshot);
        End(AiRecordType.Snapshot);
    }

    public void Intent(in IntentCommand intent)
    {
        AiRecordFormat.Write(Begin(), intent);
        End(AiRecordType.Intent);
    }

    public void UserCmd(in AiUserCmd cmd)
    {
        AiRecordFormat.Write(Begin(), cmd);
        End(AiRecordType.UserCmd);
    }

    private BinaryWriter Begin()
    {
        _w.Flush();
        _payload.SetLength(0);
        return _w;
    }

    private void End(AiRecordType type)
    {
        _w.Flush();
        AiRecordFormat.WriteRecord(_out, type, _payload.GetBuffer().AsSpan(0, (int)_payload.Length));
        Records++;
    }
}
