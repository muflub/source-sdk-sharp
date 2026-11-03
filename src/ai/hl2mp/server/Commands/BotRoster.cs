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

/// <summary>
/// The roster's bots (plan_hl2mp_ai §3.5): each entry of an
/// <c>ai_bot_roster</c> spec resolved to its skills, judged by the pool on
/// this server whatever the client's screen did, and added once a frame
/// leaves room. A preset, <c>skills=</c>, or <c>profile=&lt;name&gt;</c>,
/// read from <c>cfg/ai/bots/&lt;name&gt;.json</c> on this server's disk (a
/// dedicated server's roster names its own profiles; a client cannot upload
/// one). A refused entry is a console line naming it and the fault, and adds
/// no bot.
/// </summary>
[SideService(Side.Server)]
public sealed class Hl2mpRoster(Hl2mpBots bots, IBotProfileSource profiles, ILog log)
{
    public const string ProfileDirectory = "cfg/ai/bots";

    private readonly Queue<(int Entry, string? Name, SkillProfile Skills)> _pending = new();

    /// <summary>Bots read from a spec and not yet added.</summary>
    public int Pending => _pending.Count;

    /// <summary>Reads <paramref name="spec"/>: each entry it can resolve waits for <see cref="AddPending"/>; each it cannot is a line.</summary>
    public void Take(string spec)
    {
        var refusals = new List<string>();
        IReadOnlyList<RosterEntry> entries = RosterSpec.Parse(spec, refusals);
        foreach (string why in refusals)
            log.Warning($"[SourceSharp/Ai] ai_bot_roster refused {why}\n");

        int index = 0;
        foreach (RosterEntry entry in entries)
        {
            index++;
            SkillVerdict verdict = Resolve(entry, out string? name, out SkillProfile? skills);
            if (!verdict.Accepted)
            {
                log.Warning($"[SourceSharp/Ai] ai_bot_roster refused entry {index} '{entry.Skills}': {verdict.Refusal}\n");
                continue;
            }

            _pending.Enqueue((index, name, skills!));
        }
    }

    /// <summary>
    /// Adds pending bots while there is room for <paramref name="room"/> more;
    /// the rest are dropped with a line, so a roster larger than the server
    /// never takes the slot a player joins in. Answers how many were added.
    /// </summary>
    public int AddPending(int room)
    {
        int added = 0;
        while (_pending.TryDequeue(out var next))
        {
            if (added >= room)
            {
                log.Warning($"[SourceSharp/Ai] ai_bot_roster dropped entry {next.Entry} and {_pending.Count} after it: no room on the server\n");
                _pending.Clear();
                break;
            }

            if (bots.Add(next.Name, next.Skills) is { } refusal)
                log.Warning($"[SourceSharp/Ai] ai_bot_roster refused entry {next.Entry}: {refusal}\n");
            else
                added++;
        }

        return added;
    }

    /// <summary>The name and skills an entry stands for, judged by this server's pool.</summary>
    public SkillVerdict Resolve(RosterEntry entry, out string? name, out SkillProfile? skills)
    {
        name = entry.Name;
        skills = null;
        if (!entry.Skills.StartsWith("profile=", StringComparison.Ordinal))
            return BotSkillSpec.Parse(name ?? "custom", entry.Skills, out skills);

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
/// <see cref="RosterSpec"/>. The create-server screen sets it before
/// <c>map</c>; a dedicated server sets it in server.cfg. Read once at the
/// level's start and cleared.
/// </summary>
[ConVar("ai_bot_roster", "", Help = "Bots to add when the next level starts: easy|normal|hard|expert, name=<n>:skills=a,...,j or profile=<name>, separated by ';'")]
public sealed class AiBotRosterConVar : StringConVar { }

/// <summary>
/// Reads <see cref="AiBotRosterConVar"/> when a level is up, clears it, and
/// adds its bots on the frames after (the navigation and senses are loaded
/// by then), leaving one slot for a player (plan_hl2mp_ai §3.5, Phase 5c
/// step 3).
/// </summary>
[SideService(Side.Server)]
public sealed class AiBotRosterService(Hl2mpRoster roster, Hl2mpBots bots, ConVarRegistry convars, IEngineEnvironment environment,
                                       ILog log) : IGameLifecycle, IFrameLifecycle
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
        if (convars.Get<AiBotRosterConVar>() is not { } convar || convar.Value.Trim().Length == 0)
            return;
        string spec = convar.Value;
        convar.SetValue("");
        log.Msg($"[SourceSharp/Ai] ai_bot_roster for {mapName}: {spec}\n");
        roster.Take(spec);
    }

    public void OnPreThink(float frameTime)
    {
        if (roster.Pending == 0)
            return;
        Globals globals = environment.Globals;
        int clients = globals.IsAvailable ? globals.MaxClients : 0;
        roster.AddPending(Math.Max(0, clients - 1 - bots.Bots.Count));
    }
}
