using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;
using TF2.Descent.Ai.Motion;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Rules.Content;
using TF2.Descent.Rules.Robots;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Session;
using TF2.Descent.Server.Skills;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The horde's status sink (<see cref="RobotStatusEffects"/>, HO8) over the server's horde: a
/// skill's condition becomes a status on the robot's row with its end tick, and what each does
/// (a stun stops a walking body until its end tick, a slow slows it, a knock-up throws it, a
/// burn ticks as the applier's hits and stops at its end, a burn's kill pays the applier once,
/// a mark raises a hit); the target words; a taunt; a debuff on what a robot deals; a status
/// on a robot with no body; and a steady tick that allocates nothing.
/// </summary>
public sealed class RobotStatusEffectsTests
{
    private static readonly EntityHandle Hero = new(0x0001_0000u | 3);

    private sealed class RigCurrent(ServerHordeRig rig) : IHordeCurrent
    {
        public HordeController? Current => rig.Controller;
    }

    private sealed class Clock(ServerHordeRig rig) : IHordeClock
    {
        public long Tick => rig.Now;
        public double TicksPerSecond => ServerHordeRig.Tps;
    }

    private sealed class Taunts : IRobotTaunts
    {
        public List<(RobotId Robot, int Body, int Seat)> Told { get; } = [];
        public void Taunted(RobotId robot, int body, int seat) => Told.Add((robot, body, seat));
    }

    /// <summary>A status tick dealt as the engine deals it: the body's hit, then the core's death for a killing one.</summary>
    private sealed class Damage(ServerHordeRig rig) : IRobotStatusDamage
    {
        public List<(int Body, int Applier, double Amount, string Type, string Status)> Dealt { get; } = [];
        public RobotKills? Kills { get; set; }
        public bool Record { get; set; } = true;

        public void Deal(RobotBodies bodies, RobotBody body, int applier, double amount, string type, string status)
        {
            if (Record)
                Dealt.Add((body.Index, applier, amount, type, status));
            var attacker = applier > 0 ? new EntityHandle(0x0001_0000u | (uint)applier) : EntityHandle.None;
            bodies.BeginStatusTick(body.Index, amount, type);
            body.OnDamagedAt(attacker, (float)amount, RobotDamageKinds.BitsOf(type), default, default, HitGroup.Generic);
            bodies.EndStatusTick();
            if (Kills is { } kills && rig.Roster.State(body.Robot.Index) == RobotState.Dead && body.EngineHealth == 0)
                kills.OnEntityDied(new EntityDeath(body.Handle, attacker, default));
        }
    }

    private sealed class Told : IRobotKillHook
    {
        public List<RobotKill> Kills { get; } = [];
        public void OnRobotKilled(in RobotKill kill) => Kills.Add(kill);
    }

    private sealed class OneHook(IRobotKillHook hook) : IHooks<IRobotKillHook>
    {
        public IReadOnlyList<IRobotKillHook> All => [hook];
        public void Each(string what, Action<IRobotKillHook> call) => call(hook);
    }

    private sealed class Rosters(ServerHordeRig rig) : IKillRoster
    {
        public RobotRoster? Roster => rig.Roster;
    }

    private sealed record Kit(ServerHordeRig Rig, RobotStatusEffects Sink, Taunts Taunts, Damage Damage);

    private static Kit Make()
    {
        var rig = new ServerHordeRig();
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex is > 0 and < 33);
        var taunts = new Taunts();
        var damage = new Damage(rig);
        var sink = new RobotStatusEffects(new RigCurrent(rig), rig.Registry, rig.Engine, new Clock(rig), new RigRules(), rig.Motion, damage,
                                          taunts);
        return new Kit(rig, sink, taunts, damage);
    }

    /// <summary>Server ticks: the horde's, then the sink's.</summary>
    private static void Tick(Kit k, int ticks = 1)
    {
        for (int t = 0; t < ticks; t++)
        {
            k.Rig.Tick();
            k.Sink.OnThink(0.015f);
        }
    }

    private static int RowOf(string id) => TheRules.StatusBook.RowOf(id);

    private static long Ticks(double seconds) => StatusRules.Ticks(seconds, ServerHordeRig.Tps);

    //-------------------------------------------------------------------------
    // Apply
    //-------------------------------------------------------------------------

    [Fact]
    public void An_emp_stun_puts_the_stun_on_the_row_for_the_skills_seconds_by_the_hero()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");

        var answer = k.Sink.Apply(body.Handle, "emp_stun", 1.5, Hero, "emp_charge", "enemy_minions");

        Assert.Equal(RobotStatusAnswer.Applied, answer);
        var slot = k.Rig.Roster.StatusSlot(id.Index, RowOf("stun"));
        Assert.Equal((k.Rig.Now + Ticks(1.5), 3), (slot.EndTick, slot.Applier));
        Assert.True(k.Rig.Roster.Stunned(id.Index));
    }

    [Fact]
    public void The_bodys_networked_status_bits_are_written_at_once()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");

        k.Sink.Apply(body.Handle, "slowed", 2, Hero, "emp_charge", "enemies");

        Assert.Equal(1 << RowOf("slow"), body.State.Statuses);
    }

    [Fact]
    public void A_boss_is_not_stunned_by_an_effect_for_minions_but_is_slowed_by_one_for_bosses()
    {
        var k = Make();
        var level = Row(4, markers: 3, boss: new TF2.Descent.Ai.Level.HordeBoss("sir_nukesalot", 1, new TF2.Descent.Ai.Level.PackMarker(Centre(1), 0)));
        var rig = new ServerHordeRig(level);
        rig.Engine.Returns("Entities.IsValid", true);
        rig.Engine.ReturnsEachCall("Entities.IsPlayer", () => rig.Engine.Calls[^1].ArgAs<EntityHandle>(0).EntryIndex is > 0 and < 33);
        var sink = new RobotStatusEffects(new RigCurrent(rig), rig.Registry, rig.Engine, new Clock(rig), new RigRules(), rig.Motion, k.Damage,
                                          k.Taunts);
        Assert.True(rig.Roster.IsBoss(0));
        Assert.True(rig.Controller.LendByHand(0, rig.Now, out string why), why);
        var boss = rig.Bodies.Body(rig.Roster.Body(0));

        Assert.Equal(RobotStatusAnswer.NotARobot, sink.Apply(boss.Handle, "emp_stun", 2, Hero, "emp_charge", "enemy_minions"));
        Assert.Equal(RobotStatusAnswer.Applied, sink.Apply(boss.Handle, "slowed", 2, Hero, "emp_charge", "enemy_bosses"));
        Assert.False(rig.Roster.Stunned(0));
        Assert.True(rig.Roster.SpeedFactor(0) < 1f);
    }

    [Fact]
    public void A_minion_is_stunned_by_an_effect_for_minions_and_not_slowed_by_one_for_bosses()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");

        Assert.Equal(RobotStatusAnswer.NotARobot, k.Sink.Apply(body.Handle, "slowed", 2, Hero, "emp_charge", "enemy_bosses"));
        Assert.Equal(RobotStatusAnswer.Applied, k.Sink.Apply(body.Handle, "emp_stun", 2, Hero, "emp_charge", "enemy_minions"));
        Assert.Equal(1f, k.Rig.Roster.SpeedFactor(id.Index) + (k.Rig.Roster.Stunned(id.Index) ? 1f : 0f), 3);   // stunned (factor 0), not merely slowed
    }

    [Fact]
    public void A_fire_immune_robot_does_not_burn()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier", 1.0, "fireproof");

        Assert.Equal(RobotStatusAnswer.Immune, k.Sink.Apply(body.Handle, "burn", 5, Hero, "fireball", "enemies"));
        Assert.Equal(0, k.Rig.Roster.StatusBits(id.Index));
    }

    [Fact]
    public void Something_that_is_no_lent_robot_is_not_a_robot()
    {
        var k = Make();

        Assert.Equal(RobotStatusAnswer.NotARobot, k.Sink.Apply(k.Rig.Bodies.Body(7).Handle, "stun", 2, Hero, "x", "enemies"));
        Assert.Equal(RobotStatusAnswer.NotARobot, k.Sink.Apply(Hero, "stun", 2, Hero, "x", "enemies"));
    }

    [Fact]
    public void A_word_that_does_nothing_to_a_robot_is_not_available()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");

        Assert.Equal(RobotStatusAnswer.NotAvailable, k.Sink.Apply(body.Handle, "crit_boost", 2, Hero, "x", "enemies"));
    }

    [Fact]
    public void A_second_stun_keeps_the_later_end_as_the_rules_say()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");
        k.Sink.Apply(body.Handle, "stun", 2, Hero, "x", "enemies");
        long end = k.Rig.Roster.StatusSlot(id.Index, RowOf("stun")).EndTick;

        k.Sink.Apply(body.Handle, "stun", 0.5, Hero, "x", "enemies");

        Assert.Equal(end, k.Rig.Roster.StatusSlot(id.Index, RowOf("stun")).EndTick);
    }

    //-------------------------------------------------------------------------
    // What each does
    //-------------------------------------------------------------------------

    [Fact]
    public void A_stun_stops_a_walking_robot_and_it_walks_again_at_the_end_tick()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");
        Tick(k, 12);
        k.Rig.Motion.Order(body.Index, MotionRig.Follow(body.Position + At(3000, 0)), LookIntent.AlongPath, long.MaxValue);
        Tick(k, 30);
        var walking = body.Position;
        Tick(k, 1);
        Assert.True(body.Position.X > walking.X, "the stimulus: the body walks before the stun");

        k.Sink.Apply(body.Handle, "emp_stun", 2, Hero, "emp_charge", "enemy_minions");
        long end = k.Rig.Roster.StatusSlot(id.Index, RowOf("stun")).EndTick;
        Tick(k, 70);   // it slides to a stop on the floor's friction
        var stopped = body.Position;
        while (k.Rig.Now < end - 1) Tick(k);
        Assert.Equal(stopped, body.Position);
        Assert.True(k.Rig.Roster.Stunned(id.Index));

        Tick(k, 20);

        Assert.False(k.Rig.Roster.Stunned(id.Index));
        Assert.True(body.Position.X > stopped.X + 10f, $"at {body.Position}, stopped at {stopped}");
        Assert.Equal(0, body.State.Statuses);
    }

    [Fact]
    public void A_knock_up_throws_the_body_up()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");
        Tick(k, 20);
        float ground = body.Position.Z;

        Assert.Equal(RobotStatusAnswer.Applied, k.Sink.Apply(body.Handle, "knock_up", 0, Hero, "stomp", "enemies"));
        Tick(k, 15);

        Assert.True(body.Position.Z > ground + 20f, $"at z {body.Position.Z}, ground {ground}");
    }

    [Fact]
    public void A_burn_ticks_as_the_appliers_hits_and_stops_at_its_end_tick()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("heavy");
        k.Sink.Apply(body.Handle, "burn", 3, Hero, "fireball", "enemies");
        long end = k.Rig.Roster.StatusSlot(id.Index, RowOf("burn")).EndTick;
        double life = k.Rig.Roster.Life(id.Index);

        while (k.Rig.Now <= end + 100) Tick(k);

        int ticks = (int)(Ticks(3) / Ticks(0.5));
        Assert.Equal(ticks, k.Damage.Dealt.Count);
        Assert.All(k.Damage.Dealt, d => Assert.Equal((body.Index, 3, 4.0, "fire", "burn"), d));
        Assert.Equal(ticks, k.Rig.Hits.Hits.Count);
        Assert.All(k.Rig.Hits.Hits, h => Assert.Equal(("fire", 3), (h.DamageType, (int)h.Attacker.EntryIndex)));
        Assert.Equal(life - ticks * RobotRules.DamageTaken(k.Rig.Roster.Stats(id.Index), "fire", 4), k.Rig.Roster.Life(id.Index), 6);
        Assert.Equal(0, k.Rig.Roster.StatusBits(id.Index));
    }

    [Fact]
    public void A_burns_kill_pays_the_applier_through_the_kill_seam_once()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("scout");
        var told = new Told();
        var kills = new RobotKills(new Rosters(k.Rig), k.Rig.Registry, k.Rig.Engine, new OneHook(told), k.Rig.Log);
        k.Damage.Kills = kills;
        k.Rig.Roster.Hurt(id.Index, k.Rig.Roster.Life(id.Index) - 0.5);   // one tick from death
        k.Sink.Apply(body.Handle, "burn", 3, Hero, "fireball", "enemies");

        for (int t = 0; t < 400; t++)
        {
            Tick(k);
            kills.Sweep();
        }

        var kill = Assert.Single(told.Kills);
        Assert.Equal((id, 3), (kill.Robot, kill.Killer));
        Assert.Single(k.Damage.Dealt);
    }

    [Fact]
    public void A_marked_robot_takes_more_from_a_hit()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");
        k.Sink.Apply(body.Handle, "mini_crit_marked", 4, Hero, "juggle_doctrine", "enemies");

        body.OnDamagedAt(Hero, 40, DamageType.Bullet, default, default, HitGroup.Chest);

        Assert.Equal(RobotRules.DamageTaken(k.Rig.Roster.Stats(id.Index), "bullet", 40) * 1.35, k.Rig.Hits.Hits.Single().Taken, 5);
    }

    //-------------------------------------------------------------------------
    // Threaten, weaken
    //-------------------------------------------------------------------------

    [Fact]
    public void A_taunt_makes_the_robot_target_the_hero_and_tells_its_brain()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");

        Assert.Equal(RobotStatusAnswer.Applied, k.Sink.Threaten(body.Handle, Hero, 4, "maggots"));

        Assert.Equal(3, k.Rig.Roster.TauntSeat(id.Index, k.Rig.Now));
        Assert.Equal(k.Rig.Now + Ticks(4), k.Rig.Roster.TauntUntil(id.Index));
        Assert.Equal((id, body.Index, 3), Assert.Single(k.Taunts.Told));
    }

    [Fact]
    public void A_debuffs_damage_all_pct_lowers_what_the_robot_deals_until_it_ends()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");

        var answer = k.Sink.Weaken(body.Handle, [new StatAmount("damage_all_pct", -30)], 4, Hero, "maggots", "enemies");

        Assert.Equal(RobotStatusAnswer.Applied, answer);
        Assert.Equal(0.7, k.Sink.DamageMultiplier(id), 9);
        k.Rig.Now += Ticks(4);
        Assert.Equal(1.0, k.Sink.DamageMultiplier(id));
    }

    [Fact]
    public void A_debuff_with_no_stat_a_robot_reads_is_not_available()
    {
        var k = Make();
        var (_, body) = k.Rig.Lent("soldier");

        Assert.Equal(RobotStatusAnswer.NotAvailable, k.Sink.Weaken(body.Handle, [new StatAmount("move_speed_pct", -30)], 4, Hero, "x", "enemies"));
    }

    //-------------------------------------------------------------------------
    // No body, a new level, the steady tick
    //-------------------------------------------------------------------------

    [Fact]
    public void A_status_stays_on_the_row_when_the_body_goes_back_and_ends_at_its_end_tick()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");
        k.Sink.Apply(body.Handle, "slowed", 3, Hero, "x", "enemies");
        long end = k.Rig.Roster.StatusSlot(id.Index, RowOf("slow")).EndTick;

        Assert.True(k.Rig.Controller.ReturnByHand(id.Index, k.Rig.Now, out _));
        Assert.Equal(1 << RowOf("slow"), k.Rig.Roster.StatusBits(id.Index));
        Assert.True(k.Rig.Controller.LendByHand(id.Index, k.Rig.Now, out _));
        var again = k.Rig.Bodies.Body(k.Rig.Roster.Body(id.Index));
        Assert.Equal(1 << RowOf("slow"), again.State.Statuses);

        while (k.Rig.Now <= end) Tick(k);

        Assert.Equal(0, k.Rig.Roster.StatusBits(id.Index));
    }

    [Fact]
    public void A_dead_robots_statuses_are_dropped()
    {
        var k = Make();
        var (id, body) = k.Rig.Lent("soldier");
        k.Sink.Apply(body.Handle, "slowed", 3, Hero, "x", "enemies");

        body.OnDamagedAt(Hero, 1_000_000, DamageType.Blast, default, default, HitGroup.Chest);

        Assert.Empty(k.Rig.Roster.Statuses(id.Index).Active);
    }

    [Fact]
    public void A_steady_thousand_ticks_with_statuses_on_48_bodies_allocate_nothing()
    {
        var k = Make();
        var bodies = new List<RobotBody>();
        for (int n = 0; n < 48; n++)
            bodies.Add(k.Rig.Lent("heavy").Body);
        k.Damage.Record = false;
        k.Rig.HitMembers.Clear();   // the facts' own tally keeps a list; the chain's members are not the subject
        void ApplyAll()
        {
            foreach (var b in bodies)
            {
                k.Sink.Apply(b.Handle, "slowed", 30, Hero, "x", "enemies");
                k.Sink.Apply(b.Handle, "mini_crit_marked", 30, Hero, "x", "enemies");
                k.Sink.Apply(b.Handle, "burn", 30, Hero, "x", "enemies");
            }
        }

        long Window()   // a thousand ticks; the bytes this thread allocated over them
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 1000; t++)
            {
                k.Rig.Now++;
                k.Sink.OnThink(0.015f);
            }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        ApplyAll();
        Window();   // warm: the runtime's own first-run work is not the tick's
        ApplyAll();
        long dealtBefore = k.Sink.TicksDealt;
        long allocated = Window();

        Assert.True(k.Sink.TicksDealt - dealtBefore > 1000, $"{k.Sink.TicksDealt - dealtBefore} ticks dealt: the burns did not tick");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void The_status_tick_runs_after_the_hordes_tick_and_before_the_kill_sweep_at_an_order_of_its_own()
    {
        Assert.True(HordeService.Order < RobotStatusEffects.Order && RobotStatusEffects.Order < RobotKills.Order);
        var frames = typeof(HordeService).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IFrameLifecycle).IsAssignableFrom(t))
            .Select(t => ((IFrameLifecycle)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t)).FrameOrder)
            .ToList();
        Assert.True(frames.Count >= 3);
        Assert.Single(frames, o => o == RobotStatusEffects.Order);
    }
}
