using System.Collections.Immutable;

using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde;

namespace TF2.Descent.Server.Hero;

/// <summary>
/// One robot's death, as everything that pays for a kill reads it (XP now; the
/// loot roll, the bestiary's screens and the horde's rewards later).
/// </summary>
/// <param name="Sequence">The level's kill number: 0 for the first death of a level, one more for each after, never reused
/// within the level; a new level starts again at 0. The loot rules key their roll on it (<c>KillRoll</c>'s <c>killSeq</c>).</param>
/// <param name="Robot">The roster row that died.</param>
/// <param name="Template">Its robots.json template.</param>
/// <param name="Level">Its monster level.</param>
/// <param name="Scale">Its size.</param>
/// <param name="Affixes">Its champion affixes (none for a plain robot).</param>
/// <param name="Boss">Whether it was the level's boss.</param>
/// <param name="Stats">What the rules made of it (<see cref="RobotRules.Stats"/>): its XP value among them.</param>
/// <param name="Position">Where it died: the body's origin, or the roster's place for a robot that had no body.</param>
/// <param name="Killer">The killing player's index; 0 when no player killed it (the world, a trap, a barrel, a status effect).</param>
/// <param name="Attacker">The entity the killing hit named; <see cref="EntityHandle.None"/> for a death with no hit.</param>
public readonly record struct RobotKill(
    uint Sequence,
    RobotId Robot,
    string Template,
    int Level,
    double Scale,
    ImmutableArray<string> Affixes,
    bool Boss,
    RobotStats Stats,
    Vector Position,
    int Killer,
    EntityHandle Attacker)
{
    /// <summary>Whether a player killed it.</summary>
    public bool ByPlayer => Killer > 0;
}

/// <summary>
/// THE SEAM WHERE A ROBOT'S DEATH IS PAID FOR: told once for every robot that
/// dies on the level, whichever way it died (a hit, a burn far from any hero,
/// a dev command), in the order they died. A chain, registered by
/// <see cref="RobotKillRegistrar"/>; each member is an <see cref="IOrderedHook"/>
/// with an order of its own.
/// </summary>
public interface IRobotKillHook
{
    void OnRobotKilled(in RobotKill kill);
}

/// <summary>The roster of the level being played, as <see cref="RobotKills"/> reads it.</summary>
public interface IKillRoster
{
    /// <summary>This level's roster; null when the level has no horde.</summary>
    RobotRoster? Roster { get; }
}

/// <summary>The horde's roster: the current controller's, once it has begun.</summary>
[SideService(Side.Server, As = [typeof(IKillRoster)])]
public sealed class HordeKillRoster(HordeService horde) : IKillRoster
{
    public RobotRoster? Roster => horde.Current is { Begun: true } c ? c.Roster : null;
}

/// <summary>
/// Tells <see cref="IRobotKillHook"/> of every robot death, exactly once.
///
/// <list type="bullet">
/// <item>A KILLING HIT: the body's health runs out, the shell runs its death,
/// and the core tells the entity death chain (<see cref="IEntityDeathHook"/>)
/// with the attacker and where the body stood. The body names its roster row,
/// which the hit has already marked dead (<c>RobotBodies.Hit</c>).</item>
/// <item>A DEATH WITH NO HIT: a robot far from any hero burns to death, or is
/// killed with no body, and its roster row goes dead with no event. Every
/// frame, after the horde's own tick, the roster is swept for dead rows not
/// yet told (<see cref="Sweep"/>); they are told with no killer, at the
/// roster's place.</item>
/// </list>
///
/// A row is told once by its <see cref="RobotId"/> (row and serial), whichever
/// way it is found first. The kill number restarts with each level's roster.
/// </summary>
[SideService(Side.Server)]
public sealed class RobotKills(IKillRoster horde, IEntityDirectory directory, IEngineEntities entities,
                               IHooks<IRobotKillHook> hooks, ILog log)
    : IEntityDeathHook, IFrameLifecycle, IOrderedHook
{
    /// <summary>After the horde's tick (<see cref="HordeService.Order"/>), in TF2 Descent's horde block.</summary>
    public const int Order = ModStages.First + 540;

    private readonly HashSet<RobotId> _told = [];
    private RobotRoster? _roster;
    private uint _next;

    public int HookOrder => Order;

    public int FrameOrder => Order;

    /// <summary>How many deaths this level's roster has told; the next kill's <see cref="RobotKill.Sequence"/>.</summary>
    public uint Told => _next;

    public void OnEntityDied(EntityDeath death)
    {
        if (directory.Get(death.Victim) is not RobotBody body || Current() is not { } roster)
            return;

        var id = body.Robot;
        if (!id.IsValid || id.Index >= roster.Count || roster.Id(id.Index) != id || roster.State(id.Index) != RobotState.Dead)
            return;

        int killer = death.Attacker != EntityHandle.None && entities.IsValid(death.Attacker) && entities.IsPlayer(death.Attacker)
            ? entities.GetIndex(death.Attacker)
            : 0;
        Tell(roster, id.Index, death.Origin, killer, death.Attacker);
    }

    public void OnThink(float frameTime) => Sweep();

    /// <summary>Tells every dead row of the roster not told yet (the deaths no hit reported); answers how many.</summary>
    public int Sweep()
    {
        if (Current() is not { } roster)
            return 0;

        int told = 0;
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster.State(i) != RobotState.Dead || _told.Contains(roster.Id(i)))
                continue;

            var at = roster.Position(i);
            Tell(roster, i, new Vector(at.X, at.Y, at.Z), 0, EntityHandle.None);
            told++;
        }

        return told;
    }

    /// <summary>The level's roster; a different one from the last call (a new level, or none) starts the count again.</summary>
    private RobotRoster? Current()
    {
        var roster = horde.Roster;
        if (!ReferenceEquals(roster, _roster))
        {
            _roster = roster;
            _told.Clear();
            _next = 0;
        }

        return roster;
    }

    private void Tell(RobotRoster roster, int i, Vector at, int killer, EntityHandle attacker)
    {
        var id = roster.Id(i);
        if (!_told.Add(id))
            return;

        var kill = new RobotKill(_next++, id, roster.Template(i), roster.MonsterLevel(i), roster.Scale(i), roster.Affixes(i),
                                 roster.IsBoss(i), roster.Stats(i), at, killer, attacker);
        log.Msg(System.FormattableString.Invariant(
            $"[TF2Descent/Kills] kill {kill.Sequence}: {id} {kill.Template} level {kill.Level} at ({at.X:0} {at.Y:0} {at.Z:0}) by {(killer > 0 ? "player " + killer : "no player")}\n"));
        hooks.Each(nameof(IRobotKillHook.OnRobotKilled), h => h.OnRobotKilled(in kill));
    }
}

/// <summary>The kill seam's chain (<see cref="IRobotKillHook"/>), which only a registrar can name.</summary>
[SideService(Side.Server)]
public sealed class RobotKillRegistrar : ISideServiceRegistrar
{
    public void Register(ISideServiceRegistry registry) => registry.AddHooks<IRobotKillHook>();
}
