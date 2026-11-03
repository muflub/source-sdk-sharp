using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp.Senses;

/// <summary>
/// The session's record sink, switched on and off by <c>ai_record</c>
/// (plan_hl2mp_ai §2.6, §7.1): drops everything until started, then writes
/// an <c>.airec</c> until stopped. A recording starts before its bots are
/// added (the scenario harness adds them after), so it holds their seeds.
/// </summary>
public sealed class RecordingSwitch : IAiRecordSink
{
    private AiRecorder? _recorder;
    private FileStream? _file;

    public bool Recording => _recorder is not null;

    public int Records => _recorder?.Records ?? 0;

    public void Start(string path, in AiRecordingHeader header)
    {
        Stop();
        _file = File.Create(path);
        _recorder = new AiRecorder(_file, header);
    }

    public void Stop()
    {
        _recorder = null;
        _file?.Flush();
        _file?.Dispose();
        _file = null;
    }

    public void BotAdded(ActorId bot, ulong seed, ReadOnlySpan<byte> skillLevels) => _recorder?.BotAdded(bot, seed, skillLevels);

    public void BotRemoved(ActorId bot) => _recorder?.BotRemoved(bot);

    public void Event(in AiEvent e) => _recorder?.Event(e);

    public void Snapshot(AiSnapshot snapshot) => _recorder?.Snapshot(snapshot);

    public void Intent(in IntentCommand intent) => _recorder?.Intent(intent);

    public void UserCmd(in AiUserCmd cmd) => _recorder?.UserCmd(cmd);
}

/// <summary>
/// <c>ai_record start &lt;path&gt; [tag]</c> / <c>ai_record stop</c>: the
/// session's inputs and outputs to an <c>.airec</c> (the scenario harness's
/// recordings, §6.6); the tag names the map and its source's hash.
/// </summary>
[ConCommand("ai_record", Help = "ai_record start <path> [tag] | stop: record the AI session to an .airec", Flags = ConVarFlags.Cheat)]
public sealed class AiRecordCommand(Hl2mpAiSession ai, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length >= 2 && p[0] == "start")
        {
            string tag = p.Length > 2 ? p[2] : "";
            ai.Recording.Start(p[1], new AiRecordingHeader(Hl2mpBots.MatchSeed, tag, 0, 0, VisionPass.Slots, Hl2mpAiSession.SightedCapacity));
            log.Msg($"[SourceSharp/Ai] ai_record: recording to {p[1]} ({tag})\n");
            return;
        }

        if (p.Length == 1 && p[0] == "stop")
        {
            int records = ai.Recording.Records;
            ai.Recording.Stop();
            log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] ai_record: stopped after {records} records\n"));
            return;
        }

        log.Warning("[SourceSharp/Ai] usage: ai_record start <path> [tag] | stop\n");
    }
}

/// <summary>
/// <c>ai_bot_act &lt;name&gt; fire &lt;seconds&gt; | reload | select &lt;slot&gt;</c>:
/// a scripted action through the bot's command path (the scenario harness's
/// "human", §6.6), holding where it stands and looks.
/// </summary>
[ConCommand("ai_bot_act", Help = "ai_bot_act <name> fire <seconds> | reload | select <slot>: a scripted action", Flags = ConVarFlags.Cheat)]
public sealed class AiBotActCommand(Hl2mpBots bots, ILog log) : IConCommand
{
    public void Execute(string args)
    {
        string[] p = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        float seconds = 0f;
        int slot = 0;
        ActionKind? kind = p.Length < 2 ? null : p[1] switch
        {
            "fire" when p.Length > 2 && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) => ActionKind.Fire,
            "reload" => ActionKind.Reload,
            "select" when p.Length > 2 && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out slot) => ActionKind.SelectWeapon,
            _ => null,
        };
        if (kind is not { } k)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_act <name> fire <seconds> | reload | select <slot>\n");
            return;
        }

        if (bots.Act(p[0], k, seconds, slot) is { } refusal)
            log.Warning($"[SourceSharp/Ai] ai_bot_act refused: {refusal}\n");
    }
}
