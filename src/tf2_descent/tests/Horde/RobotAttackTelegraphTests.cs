using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Brains;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Horde.Attacks;
using TF2.Descent.Shared;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The hands' HO12 verbs (<see cref="RobotAttacks"/>): the sniper's laser over its charge and the
/// shot at its end, the minigun's spin-up, the knife's backstab, the flame's afterburn, the
/// sentry buster's spin and blast, and the spy's cloak, each against the rules' numbers.
/// </summary>
public sealed class RobotAttackTelegraphTests
{
    private static double Tps => ServerHordeRig.Tps;

    private static RobotAttack AttackOf(string id) => TheRules.Attacks.Of(id);

    private static int TicksOf(double seconds) => (int)RobotAttackRules.Ticks(seconds, Tps);

    //-------------------------------------------------------------------------
    // The sniper's telegraph
    //-------------------------------------------------------------------------

    [Fact]
    public void A_snipers_shot_follows_its_laser_by_the_rules_charge()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("sniper");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);
        int charge = TicksOf(AttackOf("sniper_rifle").WindupSeconds);
        long asked = rig.Now;

        rig.Fire(body);
        rig.Tick(charge - 1);
        Assert.Empty(rig.World.Hurts);
        int lasers = rig.World.Lasers.Count;
        rig.Tick();

        var hurt = Assert.Single(rig.World.Hurts);
        Assert.Equal(AttackRig.HeroEntity, hurt.Victim);
        Assert.Equal(asked + charge, rig.Now);
        Assert.InRange(lasers, charge / 2 - 1, charge / 2 + 1);   // every other tick of the charge
        Assert.Equal(lasers, rig.Attacks.Lasers);
        var (from, to) = rig.World.Lasers[^1];
        Assert.True((to - from).Normalized.X > 0.999f, "the laser runs along the gun (+x)");
    }

    [Fact]
    public void A_shot_with_no_telegraph_draws_no_laser()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("scout");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Fire(body);
        rig.Tick(60);

        Assert.NotEmpty(rig.World.Hurts);
        Assert.Empty(rig.World.Lasers);
    }

    //-------------------------------------------------------------------------
    // The minigun's spin
    //-------------------------------------------------------------------------

    [Fact]
    public void A_minigun_fires_its_first_round_the_rules_spin_after_its_first_ask_and_stays_spun()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("heavy");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);
        int spin = TicksOf(AttackOf("minigun").SpinUpSeconds);
        long first = rig.Now;
        long firstRound = 0;

        for (int t = 0; t < spin + 30; t++)
        {
            if (t % DecideSchedule.Period == 0)
                rig.Fire(body);
            if (firstRound == 0 && rig.World.Hurts.Count > 0)
                firstRound = rig.Now;
            rig.Tick();
        }

        Assert.True(firstRound >= first + spin && firstRound <= first + spin + DecideSchedule.Period,
                    $"the first round at {firstRound - first} ticks; the spin is {spin}");
        Assert.True(rig.Attacks.Refused(HordeWeapons.Minigun, AttackRefusal.SpinningUp) > 0);
        Assert.Contains(rig.World.Sounds, s => s.Sound == RobotAttacks.MinigunWindUp);
        // Kept asking (inside its spin-down, past its interval): no new spin.
        rig.Tick(TicksOf(AttackOf("minigun").IntervalSeconds) + 1);
        int hits = rig.World.Hurts.Count;
        rig.Fire(body);
        rig.Tick();
        Assert.Equal(hits + 1, rig.World.Hurts.Count);

        // A gap of a second: it spins up again.
        rig.Tick((int)Tps);
        rig.Fire(body);
        rig.Tick();
        Assert.Equal(hits + 1, rig.World.Hurts.Count);
    }

    //-------------------------------------------------------------------------
    // The knife
    //-------------------------------------------------------------------------

    /// <summary>The damage a spy's swing deals a hero 40 units in front of it who faces <paramref name="heroYaw"/>.</summary>
    private static (float Damage, long Backstabs, double Plain) SwingAt(float heroYaw)
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("spy");
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(40, 0), heroYaw);
        var player = rig.Horde.Heroes.Heroes[0].Eye.Player;
        rig.World.Players.Add(player);
        rig.World.Shot = RigAttackWorld.Meets(player);

        rig.Fire(body);
        rig.Tick(TicksOf(AttackOf("knife").WindupSeconds));

        return (Assert.Single(rig.World.Hurts).Damage, rig.Attacks.Backstabs, RobotAttackRules.AttackDamage(rig.Roster.Stats(id.Index), AttackOf("knife")));
    }

    [Fact]
    public void A_knife_in_a_heros_back_deals_the_rules_backstab()
    {
        var (damage, backstabs, plain) = SwingAt(0f);   // the hero faces +x, away from the spy at -x

        Assert.Equal(1, backstabs);
        Assert.Equal(plain * AttackOf("knife").BackstabMultiplier, damage, 3);
    }

    [Fact]
    public void A_knife_from_the_front_or_the_side_deals_a_plain_swing()
    {
        var front = SwingAt(180f);   // facing the spy
        var side = SwingAt(-135f);   // the spy 135 degrees off his back: outside the cone

        Assert.Equal(0, front.Backstabs);
        Assert.Equal(front.Plain, front.Damage, 3);
        Assert.Equal(0, side.Backstabs);
        Assert.Equal(side.Plain, side.Damage, 3);
    }

    //-------------------------------------------------------------------------
    // The flame's afterburn
    //-------------------------------------------------------------------------

    [Fact]
    public void A_flame_sets_the_hero_alight_for_the_rules_afterburn()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("pyro");
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(200, 0));
        var player = rig.Horde.Heroes.Heroes[0].Eye.Player;
        rig.World.Players.Add(player);

        rig.Fire(body);

        var ignite = Assert.Single(rig.World.Ignites);
        Assert.Equal((player, body.Handle), (ignite.Victim, ignite.Attacker));
        Assert.Equal((float)AttackOf("flamethrower").AfterburnSeconds, ignite.Seconds, 3);
    }

    //-------------------------------------------------------------------------
    // The sentry buster
    //-------------------------------------------------------------------------

    [Fact]
    public void A_buster_spins_with_stocks_telegraph_takes_nothing_then_blasts_the_heroes_in_reach_and_itself()
    {
        var rig = new AttackRig();
        var (id, body) = rig.Robot("sentry_buster");
        float radius = (float)AttackOf("sentry_buster").BlastRadius;
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(radius * 0.5f, 0));
        rig.Horde.Heroes.Put(rig.Horde.Level, Centre(0) + At(-(radius + 60f), 0));
        var near = rig.Horde.Heroes.Heroes[0].Eye.Player;
        var far = rig.Horde.Heroes.Heroes[1].Eye.Player;
        rig.World.Shot = (_, to) => new TraceResult(true, 1f, to, default, to.X > Centre(0).X ? near : far);
        int windup = TicksOf(AttackOf("sentry_buster").WindupSeconds);

        rig.Fire(body);
        Assert.True(rig.Attacks.Detonating(body.Index));
        Assert.Contains(rig.World.Sounds, s => s.Sound == RobotAttacks.BusterSpin);
        Assert.Equal(rig.World.Named.Sequence, body.State.Gesture);
        var guard = rig.Attacks.Guard(new TF2.Descent.Server.Horde.RobotGuardHit(body, id.Index, AttackRig.HeroEntity, "bullet", default, 50, 100, 100));
        Assert.Equal(0, guard);
        rig.Tick(windup - 1);
        Assert.Empty(rig.World.Hurts);
        rig.Tick();

        double damage = RobotAttackRules.AttackDamage(rig.Roster.Stats(id.Index), AttackOf("sentry_buster"));
        var heroHurt = Assert.Single(rig.World.Hurts, h => h.Victim != body.Handle);
        Assert.Equal((near, body.Handle, DamageType.Blast), (heroHurt.Victim, heroHurt.Attacker, heroHurt.Bits));
        Assert.Equal(damage, heroHurt.Damage, 3);
        var self = Assert.Single(rig.World.Hurts, h => h.Victim == body.Handle);
        Assert.Equal((body.Handle, DamageType.Blast), (self.Attacker, self.Bits));
        Assert.True(self.Damage > rig.Roster.Life(id.Index), "its own blast is more than its life");
        Assert.Contains(rig.World.Particles, p => p.Name == RobotAttacks.BlastTrail);
        Assert.False(rig.Attacks.Detonating(body.Index));
        Assert.Equal(1, rig.Attacks.Blasts);
    }

    [Fact]
    public void A_buster_blasts_once_however_often_it_is_asked()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("sentry_buster");
        int windup = TicksOf(AttackOf("sentry_buster").WindupSeconds);

        for (int t = 0; t < windup + 10; t++)
        {
            rig.Fire(body);
            rig.Tick();
        }

        Assert.Equal(1, rig.Attacks.Blasts);
        Assert.True(rig.Attacks.Refused(HordeWeapons.SentryBuster, AttackRefusal.WindingUp) > 0);
    }

    //-------------------------------------------------------------------------
    // The spy's cloak
    //-------------------------------------------------------------------------

    [Fact]
    public void A_cloak_hides_the_spy_and_its_knife_and_a_swing_asked_cloaked_uncloaks_first()
    {
        var rig = new AttackRig();
        var (_, body) = rig.Robot("spy");
        rig.World.Shot = RigAttackWorld.Meets(AttackRig.HeroEntity);

        rig.Attacks.Act(body.Index, new ActionIntent(ActionKind.Ability, FireStyle.Tap, HordeAbilities.Cloak, 0));
        Assert.True(body.Has(RobotFlags.Cloaked));
        Assert.False(body.Weapon!.Shown);
        Assert.Contains(rig.World.Sounds, s => s.Sound == RobotAttacks.CloakSound);

        rig.Fire(body);
        rig.Tick(30);
        Assert.Empty(rig.World.Hurts);
        Assert.False(body.Has(RobotFlags.Cloaked));
        Assert.True(body.Weapon.Shown);
        Assert.Equal(1, rig.Attacks.Refused(HordeWeapons.Knife, AttackRefusal.Cloaked));

        rig.Fire(body);
        rig.Tick(30);
        Assert.Single(rig.World.Hurts);
    }
}
