using System.Diagnostics;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Ai.Hl2mp.Senses;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp.Chat;

/// <summary>Bot chat's data file, read once through the file seam.</summary>
public interface IChatWordsSource
{
    ChatWords? Words { get; }

    string Refusal { get; }
}

/// <summary><see cref="IChatWordsSource"/> over the game's files: <see cref="Path"/>.</summary>
[SideService(Side.Server, As = [typeof(IChatWordsSource)])]
public sealed class GameFileChatWords(IEngineFiles files) : IChatWordsSource
{
    public const string Path = "scripts/ai/hl2mp_chat.json";

    private ChatWords? _words;
    private string? _refusal;

    public ChatWords? Words
    {
        get
        {
            if (_words is null && _refusal is null)
                Load();
            return _words;
        }
    }

    public string Refusal => _refusal ?? "";

    private void Load()
    {
        string? json = files.ReadGameFile(Path);
        if (json is null)
        {
            _refusal = $"no chat words: expected {Path}";
            return;
        }

        try
        {
            _words = ChatWords.Parse(json);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            _refusal = $"{Path} cannot be read: {e.Message}";
        }
    }
}

/// <summary>The help file questions are answered from, read once through the file seam.</summary>
public interface IChatHelpSource
{
    ChatHelp? Help { get; }

    string Refusal { get; }
}

/// <summary><see cref="IChatHelpSource"/> over the game's files: <see cref="Path"/>.</summary>
[SideService(Side.Server, As = [typeof(IChatHelpSource)])]
public sealed class GameFileChatHelp(IEngineFiles files) : IChatHelpSource
{
    public const string Path = "scripts/ai/hl2mp_help.json";

    private ChatHelp? _help;
    private string? _refusal;

    public ChatHelp? Help
    {
        get
        {
            if (_help is null && _refusal is null)
                Load();
            return _help;
        }
    }

    public string Refusal => _refusal ?? "";

    private void Load()
    {
        string? json = files.ReadGameFile(Path);
        if (json is null)
        {
            _refusal = $"no help for questions: expected {Path}";
            return;
        }

        try
        {
            _help = ChatHelp.Parse(json);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
        {
            _refusal = $"{Path} cannot be read: {e.Message}";
        }
    }
}

/// <summary>What chat may know of a map: the static knowledge every bot has before it plays (§5.0), the layout, and which weapons are the good ones.</summary>
public interface IChatMapFacts
{
    MapKnowledge? Map { get; }

    NavGraph? Graph { get; }

    /// <summary>The classes a bot keeps vague about (ruling 2026-11-03).</summary>
    IReadOnlySet<string> GoodWeapons { get; }
}

/// <summary>
/// What a bot may know of the one who asked it something (§3.6). The one place chat reads an
/// actor: whether this bot can see the asker, as its own vision has it, and where the asker
/// stands, which goes only to pick the item marked on the asker's own screen. Never another player.
/// <c>PerceptionCensusTests</c> names it and <c>ChatCensusTests</c> holds it to these calls.
/// </summary>
[SideService(Side.Server, As = [typeof(IChatAskerEyes)])]
public sealed class ChatAskerEyes(VisionPass vision) : IChatAskerEyes
{
    public AiVector? SeenBy(int slot, int asker)
        => vision.SightOf(slot) is { } sight && sight.Sees(asker) && vision.ReadOf(asker) is { } read ? read.Origin : null;

    public AiVector? At(int asker) => vision.ReadOf(asker)?.Origin;
}

/// <summary>Seam over <see cref="ChatAskerEyes"/>, so a fact can stand in for a bot's sight.</summary>
public interface IChatAskerEyes
{
    AiVector? SeenBy(int slot, int asker);

    AiVector? At(int asker);
}

/// <summary>The console settings bot chat follows (§3.6), behind a seam so a fact can set them.</summary>
public interface IChatConfig
{
    bool Enabled { get; }

    ChatTarget Target { get; }

    float TimeoutSeconds { get; }

    ChatSettings Settings { get; }
}

/// <summary><see cref="IChatConfig"/> over the convars.</summary>
[SideService(Side.Server, As = [typeof(IChatConfig)])]
public sealed class ConVarChatConfig(ConVarRegistry convars) : IChatConfig
{
    private ChatSettings _settings = new();

    public bool Enabled => convars.Get<AiChatConVar>()?.Value == true;

    public ChatTarget Target => new(convars.Get<AiChatUrlConVar>()?.Value ?? "", convars.Get<AiChatModelConVar>()?.Value ?? OllamaChatWriter.DefaultModel);

    public float TimeoutSeconds => convars.Get<AiChatTimeoutConVar>()?.Value ?? 5;

    public ChatSettings Settings
    {
        get
        {
            float interval = convars.Get<AiChatIntervalConVar>()?.Value ?? 30;
            float cooldown = convars.Get<AiChatCooldownConVar>()?.Value ?? 60;
            float chance = (convars.Get<AiChatReplyChanceConVar>()?.Value ?? 10) / 100f;
            if (interval != _settings.IntervalSeconds || cooldown != _settings.BotCooldownSeconds || chance != _settings.ReplyChance)
                _settings = _settings with { IntervalSeconds = interval, BotCooldownSeconds = cooldown, ReplyChance = chance };
            return _settings;
        }
    }
}

/// <summary>The writer bot chat asks through; the HTTP one unless a fact hands in a fake.</summary>
public interface IBotChatWriterFactory
{
    IBotChatWriter Create();
}

/// <summary><see cref="IBotChatWriterFactory"/>: the model server's HTTP client.</summary>
[SideService(Side.Server, As = [typeof(IBotChatWriterFactory)])]
public sealed class OllamaChatWriterFactory : IBotChatWriterFactory
{
    public IBotChatWriter Create() => OllamaChatWriter.Create();
}

/// <summary>
/// Bot chat's place in the game (plan_hl2mp_ai §3.6), optional and off by
/// default (<c>ai_chat</c>): while off it answers every hook at once and starts
/// nothing. While on, it follows the roster, hands the kill feed, a bot's own
/// hits (<see cref="IBotFightClock"/>) and pickups and the chat a bot hears to the <see cref="ChatDirector"/>,
/// and says its lines through <see cref="HostSay"/>, the path a player's chat
/// takes. What it reads is what a player sees: the kill feed, the scoreboard,
/// the bot's own holdings. It reads no position, and nothing it does touches
/// a brain, a command or a recording (<c>ChatCensusTests</c>). A server that
/// is missing or refuses prints one console line and the bots stay quiet,
/// asking again every <see cref="RetrySeconds"/>.
/// </summary>
[SideService(Side.Server, As = [typeof(IPlayerChatHook), typeof(IDeathNoticeHook)])]
public sealed class Hl2mpBotChat(Hl2mpBots bots, HostSay say, IPlayerDirectory directory, IConnectedClients clients, IEngineEntities entities,
                                 IEngineFrame frame, IBotInventory inventory, IBotFightClock fights, IChatWordsSource wordsSource, IBotChatWriterFactory writers, ILog log,
                                 IChatConfig config, IEnginePlayers players, IChatHelpSource helpSource, IChatMapFacts mapFacts, IChatAskerEyes eyes, IPlayerMarkers markers)
    : IPlayerChatHook, IDeathNoticeHook, IGameLifecycle, IFrameLifecycle, IChatWorld
{
    /// <summary>How long the bots stay quiet before asking a server that did not answer again.</summary>
    public const float RetrySeconds = 30f;

    /// <summary>How often the scoreboard and the bots' holdings are looked at: twice a second.</summary>
    public const int ScanTicks = 33;

    /// <summary>The chat stream's salt, a stream apart from every other draw a bot makes.</summary>
    public const ulong ChatSalt = 0x4348_4154UL;

    private readonly Dictionary<int, HashSet<string>> _held = new();
    private readonly List<int> _hearers = new();
    private ChatDirector? _director;
    private ChatWorker? _worker;
    private IBotChatWriter? _writer;
    private int _rosterVersion = -1;
    private bool _reported;
    private string _refusalSaid = "";

    /// <summary>The counts <c>/ai/counters</c> and <c>/ai/chat</c> show.</summary>
    public ChatCounters Counters { get; } = new();

    /// <summary>The director, while chat runs.</summary>
    public ChatDirector? Director => _director;

    public int LifecycleOrder => ModStages.First + 620;

    public int FrameOrder => ModStages.First + 620;

    private float Now => frame.TickCount() * Hl2mpAiSession.TickSeconds;

    public void OnLevelInitPostEntity(string mapName)
    {
        _director?.Reset();
        _rosterVersion = -1;
        _held.Clear();
    }

    /// <summary>What is in flight is cancelled before the level goes (§3.6), and the thread is joined.</summary>
    public void OnLevelShutdownPreEntity() => Stop();

    public void OnShutdown() => Stop();

    private void Stop()
    {
        _worker?.Dispose();
        _worker = null;
        (_writer as IDisposable)?.Dispose();
        _writer = null;
        _director = null;
        _rosterVersion = -1;
        _reported = false;
    }

    public void OnPostThink(float frameTime)
    {
        if (!config.Enabled)
        {
            if (_worker is not null)
                Stop();
            return;
        }

        if (Started() is { } director)
        {
            // The steady cost, not the one start-up: that is a level's, once.
            long start = Stopwatch.GetTimestamp();
            float now = Now;
            Follow(director);
            if (frame.TickCount() % ScanTicks == 0)
            {
                ApplySettings(director);
                Scan(director, now);
            }

            foreach (BotMoment moment in bots.TakeMoments())
            {
                // A revenge the kill feed has told chat already; a corner only the bot knows of.
                if (moment.Kind == "spot")
                    director.CaughtAtSpot(moment.Slot, NameOf(moment.Other.Slot), now);
                else if (moment.Kind == "hot")
                    director.RunningHot(moment.Slot, now);
                else if (moment.Kind == "cold")
                    director.RunningCold(moment.Slot, now);
            }

            director.Tick(now);
            ReportServer(director, now);
            Counters.MainTick((long)Stopwatch.GetElapsedTime(start).TotalNanoseconds);
        }
    }

    /// <summary>The director, built when chat is first on in a level (a bad data file is said once, and chat stays off).</summary>
    private ChatDirector? Started()
    {
        if (_director is not null)
            return _director;
        if (wordsSource.Words is not { } words)
        {
            if (_refusalSaid != wordsSource.Refusal)
            {
                _refusalSaid = wordsSource.Refusal;
                log.Msg($"[SourceSharp/Ai] ai_chat: {_refusalSaid}\n");
            }

            return null;
        }

        _writer = writers.Create();
        _worker = new ChatWorker(_writer, new ChatOutputFilter(words), Counters);
        _director = new ChatDirector(words, new ChatPromptBuilder(words), _worker, Counters, this, AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ ChatSalt, 1));
        ApplySettings(_director);
        _director.Answers = BookFor(_director);
        _worker.Start(_director.Target);
        return _director;
    }

    /// <summary>The level's answers: the map as every bot knows it and the help file. With no help file a question is talk, as it was, and that is said once.</summary>
    private ChatAnswerBook? BookFor(ChatDirector d)
    {
        if (helpSource.Help is not { } help)
        {
            log.Msg($"[SourceSharp/Ai] ai_chat: {helpSource.Refusal}\n");
            return null;
        }

        if (mapFacts.Map is not { } map)
            return null;
        return new ChatAnswerBook(help, map, new ChatPlaces(map, mapFacts.Graph), mapFacts.GoodWeapons, AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ ChatSalt, 2));
    }

    private void ApplySettings(ChatDirector d)
    {
        d.Settings = config.Settings;
        d.Aim(config.Target);
        d.TimeoutSeconds = config.TimeoutSeconds;
    }

    /// <summary>The bots' roster as the core has it: one that joined gets its persona, one that left forgets everything.</summary>
    private void Follow(ChatDirector d)
    {
        if (bots.Version == _rosterVersion || wordsSource.Words is not { } words)
            return;
        _rosterVersion = bots.Version;
        foreach (int slot in d.Slots.ToArray())
        {
            if (!bots.Bots.Any(b => b.Id.Slot == slot))
            {
                d.Leave(slot);
                _held.Remove(slot);
            }
        }

        foreach (Hl2mpBot b in bots.Bots)
        {
            if (!d.Slots.Contains(b.Id.Slot))
                d.Join(b.Id.Slot, PersonaOf(b, words));
        }
    }

    /// <summary>
    /// A bot's persona: its name, the first word of it, and the tone of the trait its name comes from.
    /// The same seed as its name's (the roster's): most presets tie several skills, and the seed
    /// settles which, so a seed of its own gave "Bobby Never Quits" another trait's voice.
    /// </summary>
    public static ChatPersona PersonaOf(in Hl2mpBot bot, ChatWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        BotTrait trait = BotNamer.Trait(bot.Skills, AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ Hl2mpBots.NameSalt, bot.SeedIndex));
        int space = bot.Name.IndexOf(' ', StringComparison.Ordinal);
        return new ChatPersona(bot.Name, space > 0 ? bot.Name[..space] : bot.Name, words.ToneOf(bot.Skills, trait));
    }

    /// <summary>Twice a second, what a player's own screen shows: a new weapon in the bot's hands, and whether it leads the scoreboard.</summary>
    private void Scan(ChatDirector d, float now)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            if (p == EntityHandle.None)
                continue;
            IReadOnlyList<string> held = inventory.Held(p);
            if (!_held.TryGetValue(b.Id.Slot, out HashSet<string>? was))
                _held[b.Id.Slot] = was = [.. held];
            foreach (string w in held)
            {
                if (was.Add(w))
                    d.PickedUp(b.Id.Slot, w, now);
            }

            // A life's holdings go with it: picking the same weapon up in the next life is news again.
            was.IntersectWith(held);
            if (fights.LastHitTick(b.Id.Slot) is >= 0 and var hit)
                d.Fighting(b.Id.Slot, hit * Hl2mpAiSession.TickSeconds);
            ChatScore score = ScoreOf(b.Id.Slot);
            d.Ranked(b.Id.Slot, score.Rank, score.Frags, now);
        }
    }

    /// <summary>One console line per outage, and a pause; an answer ends the outage.</summary>
    private void ReportServer(ChatDirector d, float now)
    {
        if (d.LastFailure == ChatFailure.None)
        {
            _reported = false;
            return;
        }

        if (d.LastFailure is ChatFailure.ServerDown or ChatFailure.ModelMissing or ChatFailure.Other)
        {
            if (d.PausedUntil <= now)
                d.PausedUntil = now + RetrySeconds;
            if (_reported)
                return;
            _reported = true;
            log.Msg($"[SourceSharp/Ai] ai_chat: {d.LastFailureDetail}; the bots stay quiet (start a model: tools/ai-chat-model)\n");
        }
    }

    public void Fill(in DeathNoticeFacts death, IDeathNoticeFields fields)
    {
        if (_director is not { } d)
            return;
        int victim = entities.GetIndex(death.Victim);
        int killer = death.Scorer == EntityHandle.None ? 0 : entities.GetIndex(death.Scorer);
        d.Death(victim, NameOf(victim), killer, killer == 0 ? "" : NameOf(killer), death.Weapon, Now);
    }

    /// <summary>A line someone said: the bots a player in their place would see it hear it (<see cref="HostSay.CanHear"/>).</summary>
    public void OnPlayerChat(PlayerChat chat)
    {
        if (_director is not { } d)
            return;
        _hearers.Clear();
        foreach (Hl2mpBot b in bots.Bots)
        {
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            if (p != EntityHandle.None && p != chat.Speaker && say.CanHear(p, chat.Speaker, chat.TeamOnly))
                _hearers.Add(b.Id.Slot);
        }

        if (_hearers.Count == 0)
            return;
        int speaker = entities.GetIndex(chat.Speaker);
        d.Heard(speaker, speaker.ToString(CultureInfo.InvariantCulture), NameOf(speaker), chat.Text, _hearers, Now);
    }

    private string NameOf(int slot) => clients.ClientConVar(slot, "name") ?? "someone";

    /// <inheritdoc />
    public ChatScore ScoreOf(int slot) => Score(slot, directory.All, entities.GetIndex, p => Spectators.Is(players, p), NameOf);

    /// <summary>
    /// The scoreboard as a bot talks about it: of those in the game. Someone watching is
    /// no rival and no leader (§4.1), though a bot may answer what they say (the say
    /// path's own rule, <c>HostSay.CanHear</c>: they can be heard).
    /// </summary>
    public static ChatScore Score(int slot, IEnumerable<(EntityHandle Player, PlayerRecord Record)> all, Func<EntityHandle, int> slotOf, Func<EntityHandle, bool> watching, Func<int, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(slotOf);
        ArgumentNullException.ThrowIfNull(watching);
        ArgumentNullException.ThrowIfNull(nameOf);
        int frags = 0, deaths = 0, playing = 0, better = 0, leaderFrags = int.MinValue, leaderSlot = 0;
        foreach ((EntityHandle player, PlayerRecord record) in all)
        {
            if (watching(player))
                continue;
            int s = slotOf(player);
            playing++;
            if (s == slot)
            {
                frags = record.Kills;
                deaths = record.Deaths;
            }

            if (record.Kills > leaderFrags)
            {
                leaderFrags = record.Kills;
                leaderSlot = s;
            }
        }

        foreach ((EntityHandle other, PlayerRecord record) in all)
        {
            if (record.Kills > frags && !watching(other))
                better++;
        }

        return new ChatScore(frags, deaths, better + 1, playing, leaderSlot == slot ? "you" : leaderSlot == 0 ? "nobody" : nameOf(leaderSlot), Math.Max(leaderFrags, 0));
    }

    /// <inheritdoc />
    public void Say(int slot, string line)
    {
        foreach (Hl2mpBot b in bots.Bots)
        {
            if (b.Id.Slot != slot)
                continue;
            EntityHandle p = bots.Applier.PlayerOf(b.Id);
            if (p != EntityHandle.None)
                say.Say(p, line, teamOnly: false);
            return;
        }
    }

    /// <inheritdoc />
    public AiVector? AskerSeenBy(int slot, int asker) => eyes.SeenBy(slot, asker);

    /// <inheritdoc />
    public AiVector? AskerAt(int asker) => eyes.At(asker);

    /// <inheritdoc />
    public void Mark(int asker, in ChatMark mark)
        => markers.Show(asker, new Vector(mark.At.X, mark.At.Y, mark.At.Z), mark.Label,
                        mark.Kind == ChatMarkKind.Area ? PlayerMarkerKind.Area : PlayerMarkerKind.Exact, mark.Radius, mark.Seconds);

    /// <summary><c>/ai/chat</c>: the counters and each bot's last lines.</summary>
    public string Json()
    {
        ChatDirector? d = _director;
        return AiJson.Write(new
        {
            enabled = config.Enabled,
            url = d?.Target.Url ?? "",
            model = d?.Target.Model ?? "",
            bots = bots.Bots.Select(b => new { slot = b.Id.Slot, name = b.Name, lines = d?.LinesOf(b.Id.Slot) ?? [] }),
            refused = Enum.GetValues<ChatRefusal>().Where(r => r != ChatRefusal.None).ToDictionary(r => r.ToString(), Counters.RefusedBy),
            lastRefused = d?.Refused ?? [],
        });
    }
}
