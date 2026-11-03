using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>The server's own profile files, by path under its game dir.</summary>
public interface IBotProfileSource
{
    /// <summary>The bytes of <paramref name="path"/> on the game's paths, or null.</summary>
    byte[]? Read(string path);
}

/// <summary><see cref="IBotProfileSource"/> over the engine's file seam: a dedicated server's own disk.</summary>
[SideService(Side.Server, As = [typeof(IBotProfileSource)])]
public sealed class GameFileBotProfiles(IEngineFiles files) : IBotProfileSource
{
    public byte[]? Read(string path) => files.ReadGameFileBytes(path);
}

/// <summary>The server's slots as the roster counts them (plan_hl2mp_ai §3.5; user ruling 2026-11-02).</summary>
public interface IServerSlots
{
    /// <summary>The engine's client slots on this level, 0 when no level is up.</summary>
    int MaxClients { get; }

    /// <summary>
    /// The people on the server, connecting or spawned: every engine client
    /// that is not a bot, and on a listen server at least its host, who takes
    /// a slot only after the level is up.
    /// </summary>
    int Humans { get; }
}

/// <summary>
/// <see cref="IServerSlots"/> over the engine's clients: a slot with a network
/// channel holds a person from the moment it connects, before it has a player
/// entity (an address, <c>GetPlayerNetInfo</c>); a bot has no channel. The
/// user id answers only from the entity: it does not see a connecting client
/// (found live: two humans in <c>status</c>, one counted).
/// </summary>
[SideService(Side.Server, As = [typeof(IServerSlots)])]
public sealed class EngineServerSlots(IConnectedClients clients, IEngineEnvironment environment, IEngineHost host) : IServerSlots
{
    public int MaxClients => environment.Globals.IsAvailable ? environment.Globals.MaxClients : 0;

    public int Humans => CountHumans(MaxClients, clients, host.IsDedicatedServer);

    /// <summary>The clients of <paramref name="max"/> slots with a channel, and a listen server's host before it connects.</summary>
    public static int CountHumans(int max, IClientInfo clients, bool dedicated)
    {
        int humans = 0;
        for (int slot = 1; slot <= max; slot++)
        {
            if (clients.Address(slot) is not null)
                humans++;
        }

        return humans == 0 && max > 0 && !dedicated ? 1 : humans;
    }
}

/// <summary>What a roster entry is, which says what a re-roll may change (§3.5).</summary>
public enum RosterKind : byte
{
    /// <summary>A difficulty: its levels are drawn from the seed; a re-roll draws again.</summary>
    Preset,

    /// <summary>A named custom allocation or a saved profile: stays as the user built it.</summary>
    NamedCustom,

    /// <summary>An unnamed custom allocation: stays until a re-roll, which keeps its total and spreads it anew.</summary>
    UnnamedCustom,
}

/// <summary>A bot the server wants: its name, skills and seed, and whether it is in the game or why it is not.</summary>
public sealed class RosterBot(int ordinal, string? givenName, RosterKind kind, SkillProfile shape)
{
    /// <summary>Its place in the order the bots were added (a lower one is older).</summary>
    public int Ordinal { get; } = ordinal;

    /// <summary>The name it was given, or null when the pools name it.</summary>
    public string? GivenName { get; } = givenName;

    public RosterKind Kind { get; } = kind;

    /// <summary>What its draws start from: its preset's levels, or the custom allocation.</summary>
    public SkillProfile Shape { get; } = shape;

    public SkillProfile Skills { get; set; } = shape;

    public int SeedIndex { get; set; }

    public string Name { get; set; } = givenName ?? "";

    /// <summary>Its actor while it is in the game, else none.</summary>
    public ActorId Actor { get; set; }

    public bool Active => Actor.IsValid;

    /// <summary>Why it is not in the game ("" while it is).</summary>
    public string Reason { get; set; } = "";

    /// <summary>Lower came off first: the oldest benched comes back first.</summary>
    public long BenchOrder { get; set; }

    /// <summary>The level whose start last refused it: it waits for the next level (a map with no nav).</summary>
    public int RefusedOnLevel { get; set; } = -1;
}

/// <summary>
/// The server's set of wanted bots (plan_hl2mp_ai §3.5; user rulings
/// 2026-11-02), in memory, never on disk: every bot of <c>ai_bot_add</c> and
/// <c>ai_bot_roster</c> joins it (the create-server window sets the roster)
/// and <c>ai_bot_kick</c> takes it out. A preset bot draws its levels from its
/// seed as it joins (<see cref="SkillSpread"/>).
/// <list type="bullet">
/// <item><b>A level change</b> ends every bot in the engine; on the new
/// level each comes back with its name, skills and seed.
/// <c>ai_bot_reroll_on_map 1</c> gives each a new seed first
/// (<see cref="LevelStarted"/>).</item>
/// <item><b>Free slots:</b> the bots in the game are at most
/// <c>maxplayers − humans − </c><see cref="Reserve"/>, so a person can always
/// connect (a full server refuses before game code sees it). Over that, the
/// newest active bot is kicked at once (the highest <see cref="RosterBot.Ordinal"/>).
/// Under it, the oldest benched comes back (the lowest
/// <see cref="RosterBot.BenchOrder"/>, then the lowest ordinal), but not until
/// <see cref="RestoreHoldTicks"/> after the last bench: a person who connects
/// and drops costs one kick and one return, however often it repeats.</item>
/// </list>
/// Each entry is judged by this server's pool whatever the client's screen
/// did: a preset, <c>skills=</c>, or <c>profile=&lt;name&gt;</c>, read from
/// <c>cfg/ai/bots/&lt;name&gt;.json</c> on this server's disk (a dedicated
/// server's roster names its own profiles; a client cannot upload one). A
/// refused entry is a console line naming it and the fault, and joins nothing.
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpRoster(Hl2mpBots bots, IBotProfileSource profiles, IServerSlots slots, ILog log)
{
    public const string ProfileDirectory = "cfg/ai/bots";

    /// <summary>The slots no bot takes, so a person can always connect (<c>ai_bot_reserve_slots</c>).</summary>
    public int Reserve { get; set; } = 1;

    /// <summary>How long after a bench no bot comes back: 5 s, the hold that stops a quick connect and drop thrashing.</summary>
    public const int RestoreHoldTicks = 336;

    /// <summary>How often the people are counted (each is a crossing per slot): every 8 ticks, 0.12 s.</summary>
    public const int RecountTicks = 8;

    /// <summary>Mixed into the match seed for a preset's draw, a stream apart from a bot's name and motion.</summary>
    public const ulong SpreadSalt = 0x5350_5245UL;

    private readonly List<RosterBot> _bots = new();
    private int _nextOrdinal;
    private long _benchCount;
    private int _holdUntil;
    private int _humans;
    private int _level;
    private bool _recount = true;

    /// <summary>The wanted bots, in the order they were added.</summary>
    public IReadOnlyList<RosterBot> Bots => _bots;

    /// <summary>The wanted bots not in the game.</summary>
    public int Benched => _bots.Count(b => !b.Active);

    /// <summary>The people, as last counted.</summary>
    public int Humans => _humans;

    /// <summary>How many bots may be in the game now.</summary>
    public int Room => Math.Max(0, slots.MaxClients - _humans - Reserve);

    /// <summary>The allocation a preset bot draws from <paramref name="seedIndex"/>.</summary>
    public static SkillProfile Draw(SkillProfile shape, int seedIndex)
        => SkillSpread.Draw(shape, AiRandom.SeedFor(Hl2mpBots.MatchSeed ^ SpreadSalt, seedIndex));

    /// <summary>
    /// <c>ai_bot_add</c>: a bot joins the roster, and the game when there is
    /// room (else it waits, benched). Answers null, or why it did not join.
    /// </summary>
    public string? Add(string? name, string? spec)
    {
        SkillVerdict verdict = BotSkillSpec.Parse(name ?? "custom", spec, out SkillProfile? skills);
        if (!verdict.Accepted)
            return verdict.Refusal;
        if (bots.AddRefusal is { } refused)
            return refused;
        RosterKind kind = SkillPresets.Names.Contains(skills!.Name) && SkillPresets.TryGet(skills.Name, out SkillProfile? preset) && preset!.SameLevels(skills)
            ? RosterKind.Preset
            : name is null ? RosterKind.UnnamedCustom : RosterKind.NamedCustom;
        if (Join(name, kind, skills) is { } unnamed)
            return unnamed;

        Count();
        RosterBot bot = _bots[^1];
        if (_bots.Count(b => b.Active) >= Room)
        {
            Bench(bot, NoRoom());
            return null;
        }

        if (Activate(bot) is { } refusal)
        {
            _bots.RemoveAt(_bots.Count - 1);
            return refusal;
        }

        return null;
    }

    /// <summary>Makes the entry: its seed, its draw if a preset, and its name, unique on the roster.</summary>
    private string? Join(string? name, RosterKind kind, SkillProfile shape)
    {
        var bot = new RosterBot(++_nextOrdinal, name, kind, shape) { SeedIndex = _nextOrdinal };
        if (kind == RosterKind.Preset)
            bot.Skills = Draw(shape, bot.SeedIndex);
        if (name is null && NameFor(bot) is { } refusal)
            return refusal;
        _bots.Add(bot);
        return null;
    }

    private string? NameFor(RosterBot bot)
    {
        IEnumerable<string> taken = _bots.Where(b => !ReferenceEquals(b, bot)).Select(b => b.Name);
        if (bots.DrawName(bot.Skills, bot.SeedIndex, taken, out string? drawn) is { } refusal)
            return refusal;
        bot.Name = drawn!;
        return null;
    }

    /// <summary>
    /// <c>ai_bot_kick</c>: the named bot (or every one, for "all") leaves the
    /// roster as well as the game. Answers how many went.
    /// </summary>
    public int Remove(string name)
    {
        int went = 0;
        for (int i = _bots.Count - 1; i >= 0; i--)
        {
            RosterBot bot = _bots[i];
            if (name != "all" && !string.Equals(bot.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;
            _bots.RemoveAt(i);
            if (!bot.Active)
                went++;
        }

        return went + bots.Kick(name);
    }

    /// <summary>
    /// Reads <paramref name="spec"/>: it is the server's roster from now on,
    /// so a server.cfg that sets the same spec every level holds the same
    /// bots; each entry it cannot resolve is a line. They come in by
    /// <see cref="Reconcile"/> once the binding has followed the level.
    /// </summary>
    public void Take(string spec)
    {
        var refusals = new List<string>();
        IReadOnlyList<RosterEntry> entries = RosterSpec.Parse(spec, refusals);
        foreach (string why in refusals)
            log.Warning($"[SourceSharp/Ai] ai_bot_roster refused {why}\n");

        Remove("all");
        _nextOrdinal = 0;
        int index = 0;
        foreach (RosterEntry entry in entries)
        {
            index++;
            SkillVerdict verdict = Resolve(entry, out string? name, out SkillProfile? skills, out RosterKind kind);
            if (!verdict.Accepted)
            {
                log.Warning($"[SourceSharp/Ai] ai_bot_roster refused entry {index} '{entry.Skills}': {verdict.Refusal}\n");
                continue;
            }

            if (Join(name, kind, skills!) is { } refusal)
            {
                log.Warning($"[SourceSharp/Ai] ai_bot_roster refused entry {index}: {refusal}\n");
                continue;
            }

            Bench(_bots[^1], "the level is starting");
        }

        _level++;
        _holdUntil = 0;
        _recount = true;
    }

    /// <summary>
    /// A new level is up and the engine has dropped its bots: every active
    /// bot is waiting to come back, ahead of any benched before, and with
    /// <paramref name="reroll"/> (<c>ai_bot_reroll_on_map</c>) each draws anew
    /// from a seed of the match, <paramref name="mapName"/> and the level's
    /// number, benched ones too: a preset keeps its difficulty and total and a
    /// new spread, an unnamed custom bot its total, and a bot the pools named
    /// a new name from its new traits; a named custom or saved bot stays as
    /// built, and a name given to a preset bot stays with it.
    /// </summary>
    public void LevelStarted(string mapName, bool reroll)
    {
        _level++;
        _holdUntil = 0;
        _recount = true;
        foreach (RosterBot bot in _bots)
        {
            if (bot.Active)
            {
                bot.Actor = ActorId.None;
                bot.Reason = "the level changed";
                bot.BenchOrder = long.MinValue + bot.Ordinal;
            }
        }

        if (!reroll)
            return;
        foreach (RosterBot bot in _bots)
        {
            if (bot.Kind == RosterKind.NamedCustom)
                continue;
            bot.SeedIndex = RerollIndex(mapName, _level, bot.Ordinal);
            bot.Skills = Draw(bot.Shape, bot.SeedIndex);
            bot.Name = "";
            if (bot.GivenName is not null)
                bot.Name = bot.GivenName;
            else if (NameFor(bot) is { } refusal)
                log.Warning($"[SourceSharp/Ai] ai_bot_reroll_on_map: bot {bot.Ordinal} keeps no name: {refusal}\n");
        }

        log.Msg($"[SourceSharp/Ai] ai_bot_reroll_on_map: {_bots.Count(b => b.Kind != RosterKind.NamedCustom)} of {_bots.Count} bots drawn anew for {mapName}\n");
    }

    /// <summary>The seed a bot draws from when re-rolled: of the match seed (through the draw), the map, the level's number and its place, stable across runs.</summary>
    public static int RerollIndex(string mapName, int level, int ordinal)
    {
        // FNV-1a over the map's bytes: string.GetHashCode differs by process, so a replay's would.
        uint h = 2166136261;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(mapName))
            h = (h ^ b) * 16777619;
        h = (h ^ (uint)level) * 16777619;
        h = (h ^ (uint)ordinal) * 16777619;
        return (int)(h & 0x7FFF_FFFF);
    }

    /// <summary>One frame, after the binding has followed the engine: bots that left, then the slots.</summary>
    public void Reconcile(int tick)
    {
        foreach (RosterBot bot in _bots)
        {
            if (bot.Active && !bots.Bots.Any(b => b.Id == bot.Actor))
            {
                // Left by another route (a console kick): it is still wanted, so it comes back.
                bot.Actor = ActorId.None;
                Bench(bot, "it left the game");
            }
        }

        if (!_recount && tick % RecountTicks != 0)
            return;
        _recount = false;
        Count();

        int room = Room, active = _bots.Count(b => b.Active);
        for (int i = _bots.Count - 1; i >= 0 && active > room; i--)
        {
            RosterBot bot = _bots[i];
            if (!bot.Active)
                continue;
            bots.Kick(bot.Actor, "benched: a player took its slot");
            bot.Actor = ActorId.None;
            Bench(bot, NoRoom());
            log.Msg($"[SourceSharp/Ai] bot {bot.Name} benched: {bot.Reason}\n");
            active--;
            _holdUntil = tick + RestoreHoldTicks;
        }

        if (active >= room || _bots.Count == active || tick < _holdUntil)
        {
            SayRoom(active, room);
            return;
        }

        foreach (RosterBot bot in _bots.Where(b => !b.Active && b.RefusedOnLevel != _level).OrderBy(b => b.BenchOrder).ThenBy(b => b.Ordinal).ToArray())
        {
            if (active >= room)
                break;
            if (Activate(bot) is { } refusal)
            {
                // Waits for the next level: a map with no nav refuses every bot, and would say so every frame.
                bot.RefusedOnLevel = _level;
                bot.Reason = refusal;
                log.Warning($"[SourceSharp/Ai] bot {bot.Name} stays benched: {refusal}\n");
                continue;
            }

            active++;
        }

        SayRoom(active, room);
    }

    /// <summary>Whatever was said of a benched bot before, once the slots are full what keeps it out is the room.</summary>
    private void SayRoom(int active, int room)
    {
        if (active < room)
            return;
        foreach (RosterBot bot in _bots)
        {
            if (!bot.Active && bot.RefusedOnLevel != _level)
                bot.Reason = NoRoom();
        }
    }

    private void Count() => _humans = slots.Humans;

    private string NoRoom()
        => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"no free slot ({_humans} human(s) of {slots.MaxClients} slots, {Reserve} kept free)");

    private void Bench(RosterBot bot, string reason)
    {
        bot.Reason = reason;
        bot.BenchOrder = ++_benchCount;
    }

    private string? Activate(RosterBot bot)
    {
        if (bots.Add(bot.Name, bot.Skills, bot.SeedIndex, out ActorId id) is { } refusal)
            return refusal;
        bot.Actor = id;
        bot.Reason = "";
        return null;
    }

    /// <summary>What <c>ai_bot_count</c> prints: the counts, then every wanted bot, active or benched and why.</summary>
    public IEnumerable<string> Status()
    {
        yield return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"[SourceSharp/Ai] bots: {_bots.Count(b => b.Active)} active, {Benched} benched of {_bots.Count} wanted ({_humans} human(s), {slots.MaxClients} slots, {Reserve} kept free)\n");
        foreach (RosterBot bot in _bots)
        {
            string skills = $"{BotSkillLabel.For(bot.Skills)} {bot.Skills.Name} ({bot.Skills.Spent} points, {bot.Skills.StyleName}) [{string.Join(",", bot.Skills.Levels)}]";
            yield return bot.Active
                ? $"[SourceSharp/Ai]   {bot.Name}: client {bot.Actor.Slot}, {skills}\n"
                : $"[SourceSharp/Ai]   {bot.Name}: benched, {bot.Reason}, {skills}\n";
        }
    }

    /// <summary>The name, skills and kind an entry stands for, judged by this server's pool.</summary>
    public SkillVerdict Resolve(RosterEntry entry, out string? name, out SkillProfile? skills, out RosterKind kind)
    {
        name = entry.Name;
        skills = null;
        kind = RosterKind.NamedCustom;
        if (!entry.Skills.StartsWith("profile=", StringComparison.Ordinal))
        {
            SkillVerdict parsed = BotSkillSpec.Parse(name ?? "custom", entry.Skills, out skills);
            if (parsed.Accepted)
            {
                kind = entry.Skills.StartsWith("skills=", StringComparison.Ordinal)
                    ? name is null ? RosterKind.UnnamedCustom : RosterKind.NamedCustom
                    : RosterKind.Preset;
            }

            return parsed;
        }

        string profile = entry.Skills["profile=".Length..];
        if (ProfileFile.NameRefusal(profile) is { } bad)
            return SkillVerdict.Refuse(bad);
        string path = $"{ProfileDirectory}/{profile}.json";
        if (profiles.Read(path) is not { } bytes)
            return SkillVerdict.Refuse($"no profile '{profile}' on this server: expected {path}");
        SkillVerdict verdict = ProfileFile.Read(bytes, path, profile, StandardSkills.StandardPool(), out BotProfileDocument? doc);
        if (!verdict.Accepted)
            return verdict;
        // A profile's own name wins unless the entry names the bot.
        name ??= profile;
        skills = doc!.Profile;
        return SkillVerdict.Ok;
    }
}

/// <summary>
/// <c>ai_bot_roster</c>: the bots the next level starts with, as a
/// <see cref="RosterSpec"/>. The create-server window sets it before
/// <c>map</c>; a dedicated server sets it in server.cfg. Read once at the
/// level's start and cleared; what it names is the server's roster from then
/// on (<see cref="Hl2mpRoster"/>).
/// </summary>
[ConVar("ai_bot_roster", "", Help = "Bots to add when the next level starts: easy|normal|hard|expert, name=<n>:skills=a,...,j or profile=<name>, separated by ';'")]
public sealed class AiBotRosterConVar : StringConVar { }

/// <summary>
/// <c>ai_bot_reroll_on_map</c> (user ruling 2026-11-02): 1 draws every roster
/// bot anew when a new level loads, new seed, new spread within its
/// difficulty, new name; 0 brings them back unchanged. Archived, so it
/// survives a restart.
/// </summary>
[ConVar("ai_bot_reroll_on_map", "0", Help = "1: every roster bot gets a new seed, skill spread and name on each new level (a preset keeps its difficulty); 0: they come back unchanged", Flags = ConVarFlags.Archive)]
public sealed class AiBotRerollConVar : BoolConVar { }

/// <summary>
/// <c>ai_bot_reserve_slots</c>: the slots the roster's bots leave free, so a
/// person can always connect (a full server refuses before game code sees it).
/// </summary>
[ConVar("ai_bot_reserve_slots", "1", Help = "Slots the roster's bots leave free for people to connect to", Min = 0, Max = 64)]
public sealed class AiBotReserveConVar : IntConVar { }

/// <summary>
/// Drives the roster with the game: at a level's start, reads
/// <see cref="AiBotRosterConVar"/> and clears it, or tells the roster the
/// level changed; each frame, reconciles it (plan_hl2mp_ai §3.5).
/// </summary>
[SideService(Side.Server)]
public sealed class AiBotRosterService(Hl2mpRoster roster, ConVarRegistry convars, IEngineFrame frame, ILog log) : IGameLifecycle, IFrameLifecycle
{
    /// <summary>After the navigation (400), the senses (450) and the binding's level line (500).</summary>
    public int LifecycleOrder => ModStages.First + 700;

    /// <summary>
    /// After <see cref="Hl2mpBots"/> (500) has followed the core's bots: a
    /// level change or a server restart (Start from the create-server screen
    /// in game: disconnect, then map) ends every bot, and the binding drops
    /// the old ones on its first frame. Added before that, a new bot took an
    /// old one's slot and the old entry was rebound to it (seen live: the
    /// inspector listed five bots for three, slot 2 answering the old
    /// Deadeye). A bot added here plays from the next frame.
    /// </summary>
    public int FrameOrder => ModStages.First + 550;

    public void OnLevelInitPostEntity(string mapName)
    {
        if (convars.Get<AiBotRosterConVar>() is { } convar && convar.Value.Trim().Length > 0)
        {
            string spec = convar.Value;
            convar.SetValue("");
            log.Msg($"[SourceSharp/Ai] ai_bot_roster for {mapName}: {spec}\n");
            roster.Take(spec);
            return;
        }

        roster.LevelStarted(mapName, convars.Get<AiBotRerollConVar>()?.Value == true);
    }

    public void OnPreThink(float frameTime)
    {
        if (convars.Get<AiBotReserveConVar>() is { } reserve)
            roster.Reserve = reserve.Value;
        roster.Reconcile(frame.TickCount());
    }
}
