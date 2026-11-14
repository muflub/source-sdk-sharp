using SourceSharp.Abi;

using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Attacks;

namespace TF2.Descent.Server.Horde.Champions;

/// <summary>
/// The deaths that blow up (plan_tf2_rpg 2f's <i>Sentry Buster death</i>; HO12), a member of the
/// robot hit chain: when a hit kills a champion with <c>explode_on_death</c>, every hero within
/// the row's radius of where it fell, with a clear line, takes the row's share of the robot's
/// damage as a blast (<see cref="RobotBlast"/>), with MvM's buster explosion and its sound.
/// A sentry buster killed before its own blast blows the same way with its attack's radius and
/// damage (stock's buster detonates when killed; here at once, with no spin: its body is gone).
/// A robot's own blast killing it blows nothing more.
/// </summary>
[SideService(Side.Server)]
public sealed class ChampionDeaths(IHordeRules rules, IHordeCurrent horde, IRobotAttackWorld world, IHordeHeroes heroes) : IRobotHitSink, IOrderedHook
{
    /// <summary>After the blast's push (<c>RobotBlastPush</c>, First + 520) and the other readers of a hit.</summary>
    public const int Order = ModStages.First + 540;

    private const int MaxHeroes = 33;

    private readonly HeroPlace[] _places = new HeroPlace[MaxHeroes];
    private readonly HeroEye[] _eyes = new HeroEye[MaxHeroes];

    public int HookOrder => Order;

    /// <summary>Deaths that blew, and the heroes they hit.</summary>
    public long Blew { get; private set; }

    public long HeroHits { get; private set; }

    public void OnRobotHit(in RobotHit hit)
    {
        if (!hit.Killed || horde.Current is not { Roster: { } roster, Bodies: { } bodies, Level: { } level } || hit.Body < 1 || hit.Body > bodies.LastBody)
            return;
        var body = bodies.Body(hit.Body);
        if (hit.Attacker == body.Handle)
            return;

        int row = hit.Robot.Index;
        var stats = roster.Stats(row);
        var template = rules.Rules.Robots.Template(roster.Template(row));
        float radius = 0f, damage = 0f;
        if (template.Attack == RobotLooks.SentryBusterAttack)
        {
            var blast = rules.Rules.Attacks.Of(template);
            radius = (float)blast.BlastRadius;
            damage = (float)RobotAttackRules.AttackDamage(stats, blast);
        }
        else if (Explodes(roster, row) is { } affix)
        {
            radius = (float)affix.Param("radius");
            damage = (float)(stats.Damage * affix.Param("damagePct") / 100);
        }

        if (radius <= 0f)
            return;
        Blew++;
        HeroHits += RobotBlast.Hit(world, heroes, level, body, radius, damage, _places, _eyes);
        world.Particle(RobotAttacks.BlastTrail, HordeVectors.ToEngine(body.Position));
        world.Particle(RobotAttacks.BlastRing, HordeVectors.ToEngine(body.Position));
        world.Sound(body.Handle, RobotAttacks.BusterExplode);
    }

    private ChampionAffix? Explodes(RobotRoster roster, int row)
    {
        var affixes = roster.Affixes(row);
        if (affixes.IsDefaultOrEmpty)
            return null;
        foreach (var id in affixes)
            if (rules.Rules.Content.Champions.TryGet(id, out var a) && a.Behaviour == ChampionBehaviours.ExplodeOnDeath)
                return a;
        return null;
    }
}
