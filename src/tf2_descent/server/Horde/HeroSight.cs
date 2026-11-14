using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;

using TF2.Descent.Ai.Level;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Mode;

namespace TF2.Descent.Server.Horde;

/// <summary>A hero as sight needs it: the player, its eye, and the room it stands in (-1 for none).</summary>
public readonly record struct HeroEye(EntityHandle Player, Vector Eye, int Room, float Yaw = 0f);

/// <summary>What sight asks of the world: the potentially visible set, then a line.</summary>
public interface ISightWorld
{
    /// <summary>Whether the box is in the potentially visible set of <paramref name="hero"/>'s eye.</summary>
    bool InPvs(EntityHandle hero, Vector mins, Vector maxs);

    /// <summary>Whether a line from <paramref name="from"/> to <paramref name="to"/> reaches it past the world's brushes.</summary>
    bool LineClear(Vector from, Vector to);
}

/// <summary>The engine's answers: <c>BoxInPvs</c> from the hero's eye, and a line trace against brushes.</summary>
[SideService(Side.Server, As = [typeof(ISightWorld)])]
public sealed class EngineSightWorld(IEnginePlayers players, IEngineWorldQuery world) : ISightWorld
{
    public bool InPvs(EntityHandle hero, Vector mins, Vector maxs) => players.BoxInPvs(hero, mins, maxs);

    /// <summary>
    /// <see cref="TraceMask.Brushes"/>: the world and brush entities. A prop or
    /// another body does not block sight here, so a robot behind a crate counts
    /// as seen: the error is on the side of "seen", never "unseen".
    /// </summary>
    public bool LineClear(Vector from, Vector to) => !world.TraceLine(from, to, TraceMask.Brushes).Hit;
}

/// <summary>
/// What the heroes can see, for the embodiment rules (plan_descent_logic 3.4:
/// a body is taken and given up only where no hero can see), on the game
/// thread, a tick at a time.
///
/// <para>
/// <b>A point</b> is seen when, for any hero, a box around it is in the PVS of
/// the hero's eye and a line from the eye reaches either of two test points: 16
/// and 64 units above it (a standing robot's knees and chest, at scale 1). The
/// PVS costs no trace; each line is one. Answers are kept for the tick, so a
/// point asked twice (the observation and the decision ask after every body)
/// costs once. <b>The budget</b>: at most <see cref="TraceBudget"/> lines a
/// tick; once spent, every point not yet answered is SEEN, the safe answer (a
/// body is not taken or given up there this tick).
/// </para>
///
/// <para>
/// <b>A room</b> is seen into when a hero stands in it, or its bounds are in a
/// hero's PVS. No line: the PVS reaches further than sight, so the answer errs
/// towards "seen into" and a far robot waits a room further back than it need
/// (the rule is conservative, as the plan allows: never "unseen" wrongly).
/// </para>
/// </summary>
public sealed class HeroSight : IHeroSight
{
    /// <summary>Lines a tick at most.</summary>
    public const int DefaultTraceBudget = 128;

    private readonly ISightWorld _world;
    private readonly HordeLevel _level;
    private readonly HeroEye[] _heroes;
    private readonly Dictionary<AiVector, bool> _seen;
    private readonly bool[] _roomKnown;
    private readonly bool[] _roomSeen;
    private int _heroCount;

    public HeroSight(ISightWorld world, HordeLevel level, int traceBudget, HeroEye[] heroes, Dictionary<AiVector, bool> seen,
                     bool[] roomKnown, bool[] roomSeen)
    {
        _world = world;
        _level = level;
        TraceBudget = traceBudget;
        _heroes = heroes;
        _seen = seen;
        _roomKnown = roomKnown;
        _roomSeen = roomSeen;
    }

    /// <summary>Sight over <paramref name="level"/> for up to <paramref name="maxHeroes"/> heroes, its scratch made once.</summary>
    public static HeroSight For(ISightWorld world, HordeLevel level, int maxHeroes, int traceBudget = DefaultTraceBudget) =>
        new(world, level, traceBudget, new HeroEye[maxHeroes], new Dictionary<AiVector, bool>(512),
            new bool[level.RoomCount], new bool[level.RoomCount]);

    public int TraceBudget { get; }

    /// <summary>Lines traced this tick.</summary>
    public int TracesThisTick { get; private set; }

    /// <summary>Points answered "seen" this tick only because the budget was spent.</summary>
    public int OverBudgetThisTick { get; private set; }

    /// <summary>Lines traced since the level began.</summary>
    public long Traces { get; private set; }

    /// <summary>Starts a tick with <paramref name="heroes"/>' eyes: forgets the last tick's answers and resets the budget.</summary>
    public void BeginTick(ReadOnlySpan<HeroEye> heroes)
    {
        _heroCount = Math.Min(heroes.Length, _heroes.Length);
        heroes[.._heroCount].CopyTo(_heroes);
        _seen.Clear();
        Array.Clear(_roomKnown);
        TracesThisTick = 0;
        OverBudgetThisTick = 0;
    }

    public bool CanSee(AiVector point)
    {
        if (_seen.TryGetValue(point, out bool known))
            return known;

        bool seen = Look(point, out bool final);
        if (final)
            _seen[point] = seen;
        return seen;
    }

    public bool CanSeeInto(int room)
    {
        if ((uint)room >= (uint)_roomKnown.Length)
            return true;
        if (_roomKnown[room])
            return _roomSeen[room];

        var r = _level.Rooms[room];
        bool seen = false;
        for (int h = 0; h < _heroCount && !seen; h++)
            seen = _heroes[h].Room == room || _world.InPvs(_heroes[h].Player, HordeVectors.ToEngine(r.Min), HordeVectors.ToEngine(r.Max));

        _roomKnown[room] = true;
        _roomSeen[room] = seen;
        return seen;
    }

    /// <summary>
    /// The look at one point. <paramref name="final"/> is false when the budget
    /// cut it short: the "seen" it answers then is not the point's answer, so
    /// it is not kept, and a later tick looks again.
    /// </summary>
    private bool Look(AiVector point, out bool final)
    {
        final = true;
        Vector p = HordeVectors.ToEngine(point);
        Vector mins = p + new Vector(-16f, -16f, 0f), maxs = p + new Vector(16f, 16f, 72f);
        for (int h = 0; h < _heroCount; h++)
        {
            ref readonly var hero = ref _heroes[h];
            if (!_world.InPvs(hero.Player, mins, maxs))
                continue;

            for (int k = 0; k < 2; k++)
            {
                if (TracesThisTick >= TraceBudget)
                {
                    OverBudgetThisTick++;
                    final = false;
                    return true;
                }

                TracesThisTick++;
                Traces++;
                if (_world.LineClear(hero.Eye, p + new Vector(0f, 0f, k == 0 ? 16f : 64f)))
                    return true;
            }
        }

        return false;
    }
}

/// <summary>Who the heroes are this tick: the living human players on the heroes' team.</summary>
public interface IHordeHeroes
{
    /// <summary>Fills <paramref name="places"/> and <paramref name="eyes"/> alike, as many as both hold; answers how many.</summary>
    int Gather(HordeLevel level, Span<HeroPlace> places, Span<HeroEye> eyes);
}

/// <summary>
/// The heroes from the teams: <see cref="DescentMode.HeroTeam"/>'s players who
/// are alive and not bots. A hero's seat is its player's entity index (1 to
/// the player count); its room is the level room holding its feet (-1 for
/// none, which wakes nothing).
/// </summary>
[SideService(Side.Server, As = [typeof(IHordeHeroes)])]
public sealed class HordeHeroes(ITeams teams, IEnginePlayers players, IEngineEntities entities, IBotPlayers bots) : IHordeHeroes
{
    public int Gather(HordeLevel level, Span<HeroPlace> places, Span<HeroEye> eyes)
    {
        int n = 0;
        int max = Math.Min(places.Length, eyes.Length);
        var team = teams.Players((int)DescentMode.HeroTeam);
        for (int k = 0; k < team.Count && n < max; k++)
        {
            var player = team[k];
            if (!players.IsAlive(player) || bots.IsBot(player))
                continue;

            var feet = HordeVectors.ToAi(entities.GetOrigin(player));
            int room = level.RoomAt(feet);
            places[n] = new HeroPlace(entities.GetIndex(player), room, feet);
            eyes[n] = new HeroEye(player, entities.GetEyePosition(player), room, entities.GetEyeAngles(player).Y);
            n++;
        }

        return n;
    }
}
