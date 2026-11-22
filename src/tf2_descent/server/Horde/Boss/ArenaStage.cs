using System.Globalization;

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Horde.Boss;

/// <summary>One entity an arena input reached: its handle and the middle of its bounds.</summary>
public readonly record struct ArenaEntity(EntityHandle Handle, Vector Centre);

/// <summary>
/// THE SEAM WHERE THE BOSS FIGHT CHANGES ITS ARENA (the ARNA contract of 2026-11-16): an input
/// (<c>Enable</c>, <c>Disable</c>) fired at every entity the map names <paramref name="name"/>;
/// answers the entities it reached, none when the map has none of that name.
/// </summary>
public interface IArenaEntities
{
    IReadOnlyList<ArenaEntity> Fire(string name, string input);
}

/// <summary>The engine's answer to <see cref="IArenaEntities"/>: the core's entity list by name and its input path (what the debug API's <c>/entity/input</c> fires through).</summary>
[SideService(Side.Server, As = [typeof(IArenaEntities)])]
public sealed class EngineArenaEntities(IEngineEntities entities) : IArenaEntities
{
    public IReadOnlyList<ArenaEntity> Fire(string name, string input)
    {
        var found = entities.FindAllByName(name);
        var reached = new List<ArenaEntity>(found.Count);
        foreach (var e in found)
        {
            var origin = entities.GetOrigin(e);
            var (mins, maxs) = entities.GetBounds(e);
            entities.FireInput(e, input);
            reached.Add(new ArenaEntity(e, origin + (mins + maxs) * 0.5f));
        }

        return reached;
    }
}

/// <summary>
/// THE SEAM WHERE THE HORDE'S PATHS LEARN THE ARENA CHANGED: the stage tells it the inputs it fires,
/// by the brush's name, whether the map holds the brush or not (the horde's navigation matches it to
/// the level's obstacles of that name, <c>HordeNav</c>), the inputs of one event together (a drilled
/// station's pillar falling and its rubble landing are one change of the arena).
/// </summary>
public interface IArenaObstacles
{
    /// <summary>The brushes named in <paramref name="changes"/> were each enabled or disabled, together.</summary>
    void Changed(IReadOnlyList<(string Name, bool Enabled)> changes);
}

/// <summary>The boss's phase the arena has reached this level, as the staged hazards read it (1-based; 1 before the fight's first change).</summary>
public interface IArenaPhase
{
    int Phase { get; }
}

/// <summary>
/// THE ARENA AS THE ENCOUNTER CHANGES IT (the ARNA contract agreed with the map session on
/// 2026-11-16): the named brush entities of the level's BSP, driven by Enable and Disable as the
/// fight decides, never by the map's own I/O, so a replay and a second load agree. Names are exact,
/// lower case, decimal, unpadded:
/// <list type="bullet">
/// <item><c>descent_boss_mouth_1</c> Disabled when the level's boss is first found (a leg-1 mouth
/// is open from the start), <c>descent_boss_mouth_&lt;leg&gt;</c> when the boss enters that phase.</item>
/// <item><c>descent_boss_platform_&lt;phase&gt;</c> Disabled when the boss enters that phase (2, 3, 4);
/// the staged hazards (<c>descent_boss_hazard_&lt;phase&gt;_&lt;kind&gt;</c>) turn on with it
/// (<see cref="Phase"/>, read by the hazard service).</item>
/// <item><c>descent_boss_pillar_&lt;n&gt;</c> Disabled and <c>descent_boss_rubble_&lt;n&gt;</c> Enabled when
/// the Tank finishes drilling station n (its final the final's number), with the pillar's dust and
/// crash (bosses.json's <c>pillarDust</c>, <c>pillarFalls</c>).</item>
/// <item><c>descent_boss_exit</c> Disabled on the boss's death. The transition gate
/// (<see cref="IBossExit"/>) stays the authority, so a level without the brush still lets heroes down.</item>
/// </list>
/// Each name is fired once a level. A name the map does not hold is said once on the log and is no
/// error. Each input is told to the horde's paths (<see cref="IArenaObstacles"/>), which plan round
/// the brushes that stand in the way. The changes hold for the level (a boss reset does not undo them: the design is silent;
/// this lane's choice). A level's end forgets everything: the next load's map stands as it ships.
/// </summary>
[SideService(Side.Server, As = [typeof(IArenaPhase)])]
public sealed class ArenaStage(IArenaEntities entities, IArenaObstacles obstacles, IBossWorld world, IHordeRules rules, ILog log) : IArenaPhase, IGameLifecycle
{
    /// <summary>In the boss band, after the precache (+1330).</summary>
    public int LifecycleOrder => ModStages.First + 1340;

    public const string Enable = "Enable", Disable = "Disable";

    public const string ExitName = "descent_boss_exit";

    public static string PillarName(int station) => "descent_boss_pillar_" + station.ToString(CultureInfo.InvariantCulture);

    public static string RubbleName(int station) => "descent_boss_rubble_" + station.ToString(CultureInfo.InvariantCulture);

    public static string PlatformName(int phase) => "descent_boss_platform_" + phase.ToString(CultureInfo.InvariantCulture);

    public static string MouthName(int leg) => "descent_boss_mouth_" + leg.ToString(CultureInfo.InvariantCulture);

    public static string HazardName(int phase, string kind) => "descent_boss_hazard_" + phase.ToString(CultureInfo.InvariantCulture) + "_" + kind;

    private readonly HashSet<string> _fired = new(StringComparer.Ordinal);
    private readonly List<(string Name, string Input, int Reached)> _log = [];

    /// <summary>The highest phase the fight has entered this level (1-based), held through a reset.</summary>
    public int Phase { get; private set; } = 1;

    /// <summary>Every input this level fired, in order, with how many entities it reached.</summary>
    public IReadOnlyList<(string Name, string Input, int Reached)> Fired => _log;

    /// <summary>Names this level fired at that the map does not hold.</summary>
    public int Missing { get; private set; }

    /// <summary>The level's boss stands: the leg-1 mouths open.</summary>
    public void BossFound() => Tell(Once(MouthName(1), Disable, "the leg-1 mouths are open from the start", out _), null);

    /// <summary>The boss entered <paramref name="phase"/> (1-based); the phases between the last and it are entered too.</summary>
    public void PhaseEntered(int phase)
    {
        for (int p = Phase + 1; p <= phase; p++)
        {
            Phase = p;
            string why = string.Create(CultureInfo.InvariantCulture, $"the boss entered phase {p}");
            Tell(Once(PlatformName(p), Disable, why, out _), Once(MouthName(p), Disable, why, out _));
            log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Boss] arena: phase {p}: its staged hazards ({HazardName(p, "<kind>")}) are on\n"));
        }
    }

    /// <summary>The Tank finished drilling station <paramref name="station"/> (its final included).</summary>
    public void StationDrilled(int station)
    {
        string why = string.Create(CultureInfo.InvariantCulture, $"the Tank drilled station {station}");
        var sounds = rules.Rules.Bosses.Settings.Sounds;
        var pillar = Once(PillarName(station), Disable, why, out var fell);
        foreach (var p in fell)
        {
            world.Particle(sounds.PillarDust, p.Centre);
            world.Sound(p.Handle, sounds.PillarFalls);
        }

        // The pillar's fall and its rubble's landing are one change of the arena: the paths never plan with the pillar gone and no rubble.
        Tell(pillar, Once(RubbleName(station), Enable, why, out _));
    }

    /// <summary>The boss is dead: the brush over the way down goes.</summary>
    public void Fallen() => Tell(Once(ExitName, Disable, "the boss is dead", out _), null);

    /// <summary>Tells the horde's paths the inputs fired, together (none fired: nothing told).</summary>
    private void Tell((string Name, bool Enabled)? first, (string Name, bool Enabled)? second)
    {
        var changes = new List<(string Name, bool Enabled)>(2);
        if (first is { } a)
            changes.Add(a);
        if (second is { } b)
            changes.Add(b);
        if (changes.Count > 0)
            obstacles.Changed(changes);
    }

    /// <summary>
    /// Fires <paramref name="input"/> at <paramref name="name"/> unless this level already did; says once when the map holds none.
    /// Answers the change to tell the paths (none when already fired), the entities it reached into <paramref name="reached"/>.
    /// </summary>
    private (string Name, bool Enabled)? Once(string name, string input, string why, out IReadOnlyList<ArenaEntity> reached)
    {
        reached = [];
        if (!_fired.Add(name))
            return null;
        reached = entities.Fire(name, input);
        _log.Add((name, input, reached.Count));
        if (reached.Count == 0)
        {
            Missing++;
            log.Msg($"[TF2Descent/Boss] arena: no {name} on this map ({why}): nothing to {input}; said once\n");
        }
        else
        {
            log.Msg(string.Create(CultureInfo.InvariantCulture, $"[TF2Descent/Boss] arena: {input} {name} ({reached.Count} entit{(reached.Count == 1 ? "y" : "ies")}): {why}\n"));
        }

        return (name, input == Enable);
    }

    public void OnLevelInit(string mapName) => Forget();

    public void OnLevelShutdown() => Forget();

    private void Forget()
    {
        _fired.Clear();
        _log.Clear();
        Missing = 0;
        Phase = 1;
    }

    /// <summary>
    /// <c>tf2_descent_boss_arena [phase N | drill N | fallen]</c>'s verbs: the same calls the encounter,
    /// the Tank's asks and the boss's death make, for a dev proof on a map whose fight has not got
    /// there. Answers false for a verb it does not know.
    /// </summary>
    public bool Dev(string verb, int n)
    {
        switch (verb)
        {
            case "phase":
                PhaseEntered(n);
                return true;
            case "drill":
                StationDrilled(n);
                return true;
            case "fallen":
                Fallen();
                return true;
            case "":
                return true;
            default:
                return false;
        }
    }

    /// <summary>The stage as lines for <c>tf2_descent_boss_arena</c>.</summary>
    public IEnumerable<string> Lines()
    {
        yield return string.Create(CultureInfo.InvariantCulture, $"arena: phase {Phase}, {_log.Count} input(s) fired, {Missing} name(s) the map does not hold");
        foreach (var (name, input, reached) in _log)
            yield return string.Create(CultureInfo.InvariantCulture, $"  {input} {name}: {reached} entit{(reached == 1 ? "y" : "ies")}");
    }
}
