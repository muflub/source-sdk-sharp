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

    public void Start(string path, in AiRecordingHeader header, ActorAnonymiser? actors = null)
    {
        Stop();
        _file = File.Create(path);
        _recorder = new AiRecorder(_file, header, actors);
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

    public void DecisionTree(IReadOnlyList<string> names, IReadOnlyList<int> branches) => _recorder?.DecisionTree(names, branches);

    public void Event(in AiEvent e) => _recorder?.Event(e);

    public void Snapshot(AiSnapshot snapshot) => _recorder?.Snapshot(snapshot);

    public void Intent(in IntentCommand intent) => _recorder?.Intent(intent);

    public void UserCmd(in AiUserCmd cmd) => _recorder?.UserCmd(cmd);

    public void Frame(in AiActorFrame frame) => _recorder?.Frame(frame);

    public void Observed(in AiEvent e) => _recorder?.Observed(e);

    /// <summary>What the brains know of the world, written right after the header (format 2).</summary>
    public void World(AiRecordedWorld world) => _recorder?.World(world);
}

/// <summary>
/// <c>ai_record start &lt;path&gt; [tag]</c> / <c>ai_record stop</c>: the
/// session's inputs and outputs to an <c>.airec</c> (the scenario harness's
/// recordings, §6.6); the tag names the map and its source's hash.
/// </summary>
/// <summary>
/// <c>ai_step [ticks|off]</c> (§7.3's live scrubbing, with <c>ai_sync 1</c>):
/// holds the brains, then lets the next <c>ticks</c> ticks' decides run (6:
/// every bot once); <c>off</c> lets them run every tick again.
/// </summary>
[ConCommand("ai_step", Help = "ai_step [ticks|off]: hold the bots' brains and let the next ticks' decides run (6 = every bot once); off resumes", Flags = ConVarFlags.Cheat)]
public sealed class AiStepCommand(Hl2mpAiSession ai, ILog log)
{
    public void Execute([Complete("off")] string ticks = "")
    {
        if (ticks == "off")
        {
            ai.Session.Stepping = false;
            ai.Session.StepTicks = 0;
            log.Msg("[SourceSharp/Ai] ai_step off: the brains run every tick\n");
            return;
        }

        int count = int.TryParse(ticks, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 ? n : DecideSchedule.Period;
        ai.Session.Stepping = true;
        ai.Session.StepTicks += count;
        log.Msg(string.Create(CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] ai_step: held; {ai.Session.StepTicks} tick(s) of decides to run, {ai.Session.Counters.StepsRun} run, {ai.Session.Counters.StepsHeld} held\n"));
    }
}

/// <summary>
/// <c>ai_record start [path] [tag]</c> / <c>ai_record human</c> /
/// <c>ai_record stop</c>: the session's inputs and outputs to an
/// <c>.airec</c> (the scenario harness's recordings, §6.6), every player's
/// usercmds and frames and a person's sight with them (§3.4.4). With no path
/// (or <c>human</c>), the file is the mod's
/// <c>ai-recordings/human/&lt;utc&gt;.airec</c>, local and outside git. Every
/// person is anonymised at write time: a random actor serial for the
/// session, and nothing of a name, a Steam id or chat is ever written.
/// Recording changes nothing in play, so it needs no <c>sv_cheats</c>.
/// </summary>
[ConCommand("ai_record", Help = "ai_record human | start [path] [tag] | stop: record the AI session (every player's usercmds, anonymised) to an .airec; with no path, <mod>/ai-recordings/human/<utc>.airec")]
public sealed class AiRecordCommand(Hl2mpAiSession ai, VisionPass vision, IEngineFrame frame, IModInfo mod, ILog log)
{
    /// <summary>Where a recording with no path goes, under the mod's game dir.</summary>
    public const string HumanDirectory = "ai-recordings/human";

    /// <summary>
    /// The mod's own game dir: <paramref name="gameDir"/>, or, for a game run
    /// on an instance (<c>-instance</c>: a private game dir whose entries are
    /// links to the tree's, removed when the game exits), the tree's dir its
    /// <c>maps</c> link points into, so a recording outlives the instance. A
    /// tree's own <c>maps</c> is a directory (its gameinfo.txt may be a link,
    /// into the repo's game/ sources, which is why that one is not followed).
    /// </summary>
    public static string TreeGameDir(string gameDir)
    {
        var maps = new DirectoryInfo(Path.Combine(gameDir, "maps"));
        return maps.Exists && maps.LinkTarget is { } target
               && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target, gameDir))) is { } tree
            ? tree
            : gameDir;
    }

    /// <summary>The file a recording started at <paramref name="utc"/> with no path goes to.</summary>
    public static string HumanPath(string gameDir, DateTime utc)
        => Path.Combine(gameDir, "ai-recordings", "human", utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + ".airec");

    public void Execute([Complete("human", "start", "stop")] string action, string? file = null, string? mapTag = null)
    {
        if (action == "start" || (action == "human" && file is null))
        {
            string path;
            if (file is not null)
                path = file;
            else
            {
                path = HumanPath(TreeGameDir(mod.GameDir), DateTime.UtcNow);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }

            MemoryWorld? world = ai.Session.World;
            string tag = mapTag ?? world?.Map.Map ?? "";
            ulong nav = world?.Graph?.Hash ?? 0;
            ai.Recording.Start(path, new AiRecordingHeader(Hl2mpBots.MatchSeed, tag, nav, 0, VisionPass.Slots, Hl2mpAiSession.SightedCapacity),
                               Anonymiser(BitConverter.ToUInt64(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8))));
            // The brains' world, so the recording replays as the game decided (§2.6).
            if (world is not null)
                ai.Recording.World(new AiRecordedWorld(world.Map, world.TickSeconds, ai.Session.Curves?.Json ?? "", nav));
            // The bots already playing, so the recording knows them for bots (§3.4.4's metrics).
            ai.Session.AnnounceBots();
            var held = new List<AiEvent>();
            vision.HeldSights(new Tick(frame.TickCount()), held);
            foreach (AiEvent e in held)
                ai.Recording.Observed(e);
            log.Msg($"[SourceSharp/Ai] ai_record: recording to {path} ({tag}), nav {nav:x16}\n");
            return;
        }

        if (action == "stop" && file is null)
        {
            // Every step handed over is taken back first, so the recording
            // holds the intent of every decide whose snapshot it holds.
            ai.Session.Drain(wait: true);
            int records = ai.Recording.Records;
            ai.Recording.Stop();
            log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] ai_record: stopped after {records} records\n"));
            return;
        }

        log.Warning("[SourceSharp/Ai] usage: ai_record human | start [path] [tag] | stop\n");
    }

    /// <summary>A recording's anonymiser: every person (a human's actor, <see cref="ActorDirectory.HumanSerialBit"/>) by a random serial.</summary>
    public static ActorAnonymiser Anonymiser(ulong seed)
        => new(id => (id.Serial & ActorDirectory.HumanSerialBit) != 0, ActorDirectory.HumanSerialBit, seed);
}

/// <summary>
/// <c>ai_bot_act &lt;name&gt; fire &lt;seconds&gt; | reload | select &lt;slot&gt;</c>:
/// a scripted action through the bot's command path (the scenario harness's
/// "human", §6.6), holding where it stands and looks.
/// </summary>
[ConCommand("ai_bot_act", Help = "ai_bot_act <name> fire <seconds> | hold <seconds> | reload | select <slot>: a scripted action", Flags = ConVarFlags.Cheat)]
public sealed class AiBotActCommand(Hl2mpBots bots, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, Act act, float? amount = null)
    {
        float seconds = amount ?? 0f;
        int slot = (int)seconds;
        ActionKind? kind = (act, amount) switch
        {
            (Act.Fire, not null) => ActionKind.Fire,
            (Act.Hold, not null) => ActionKind.None,
            (Act.Reload, _) => ActionKind.Reload,
            (Act.Select, not null) => ActionKind.SelectWeapon,
            _ => null,
        };
        if (kind is not { } k)
        {
            log.Warning("[SourceSharp/Ai] usage: ai_bot_act <name> fire <seconds> | hold <seconds> | reload | select <slot>\n");
            return;
        }

        if (bots.Act(name, k, seconds, slot) is { } refusal)
            log.Warning($"[SourceSharp/Ai] ai_bot_act refused: {refusal}\n");
    }

    public enum Act
    {
        Fire,
        Hold,
        Reload,
        Select,
    }
}
