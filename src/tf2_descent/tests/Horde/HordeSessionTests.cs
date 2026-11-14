using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Server;
using TF2.Descent.Ai.Far;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Server.Horde.Motion;
using TF2.Descent.Server.Horde.Senses;
using TF2.Descent.Server.Horde.Session;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>A session that does nothing: the facts that are not about the AI.</summary>
internal sealed class IdleBrains : IHordeBrainHost
{
    public void Begin(HordeController horde, long now) { }

    public void Tick(long now) { }

    public ReadOnlySpan<SoundEvent> TakeFarSounds() => [];

    public void End() { }
}

/// <summary>Every order and hold the session hands the motion.</summary>
internal sealed class RecordingMotion : IRobotMotion
{
    public List<(int Body, MoveIntent Move, LookIntent Look, long Expires)> Orders { get; } = [];
    public List<int> Holds { get; } = [];

    public void Order(int body, in MoveIntent move, in LookIntent look, long expiresTick) => Orders.Add((body, move, look, expiresTick));

    public void Hold(int body) => Holds.Add(body);
}

internal sealed class RigClock(ServerHordeRig? rig) : IHordeClock
{
    public ServerHordeRig? Rig { get; set; } = rig;

    public long Tick => Rig?.Now ?? 0;

    public double TicksPerSecond => ServerHordeRig.Tps;
}

internal sealed class NoLandings : IHeroLandings
{
    public bool Landed(EntityHandle hero) => false;
}

internal sealed class RigMod(string dir) : IModInfo
{
    public string Id => "tf2_descent";
    public string GameDir => dir;
    public string ManagedDir => dir;
    public IReadOnlyList<string> AssembliesFor(Side side) => [];
}

/// <summary>
/// The horde's AI session in the server's horde (plan_descent_logic HO4, HO5)
/// over the rig's recording engine: it begins with the controller and ends
/// before it, joining its workers; a second level starts a fresh one; lent
/// bodies get brains whose intents reach the motion; a heard sound reaches the
/// far simulation too; a hit reaches a brain through the hit chain.
/// </summary>
public sealed class HordeSessionTests
{
    private static (ServerHordeRig Rig, HordeSession Session, HordeWorkers Workers, RecordingMotion Motion) Make(bool inline, int threads = 2,
                                                                                                                 TF2.Descent.Ai.Level.HordeLevel? level = null)
    {
        HordeSession? session = null;
        var workers = new HordeWorkers();
        var motion = new RecordingMotion();
        var clock = new RigClock(null);
        var rig = new ServerHordeRig(level, brains: r =>
        {
            var sync = DeclaredConVars.Holding<HordeAiSyncConVar>(inline ? "1" : "0");

            var t = DeclaredConVars.Holding<HordeAiThreadsConVar>(threads.ToString(System.Globalization.CultureInfo.InvariantCulture));

            session = new HordeSession(motion, new RobotActionTally(), r.SightWorld, r.Heroes, new RigRules(), clock, new NoLandings(), workers,
                                       sync, t, DeclaredConVars.Fresh<HordeAiRecordConVar>(), new RigMod(Path.GetTempPath()), r.Log);
            return session;
        });
        clock.Rig = rig;
        return (rig, session!, workers, motion);
    }

    [Fact]
    public void The_session_begins_with_the_controller_and_its_workers_are_joined_when_it_ends()
    {
        var (rig, session, workers, _) = Make(inline: false, threads: 2);
        Assert.NotNull(session.Brains);
        Assert.Equal(2, workers.Pool.Alive);
        Assert.Contains(rig.Log.Lines, l => l.Text.Contains("AI session 1: 54 slots") && l.Text.Contains("pooled, 2 threads"));

        rig.Controller.End();
        Assert.Null(session.Brains);
        Assert.Equal(0, workers.Pool.Alive);
        Assert.False(workers.Pool.Running);
    }

    [Fact]
    public void A_second_level_starts_a_fresh_session_on_the_same_pool()
    {
        var (rig, session, workers, _) = Make(inline: false, threads: 2);
        rig.Lent("soldier");
        rig.Tick(12);
        Assert.Equal(1, session.Brains!.Session.BotCount);
        rig.Controller.End();

        var second = rig.Spawner.Controller()!;
        Assert.True(second.Begin(Row(3, markers: 2, seed: 77), ServerHordeRig.Tps, 1, rig.Now));
        Assert.Equal(2, session.Levels);
        Assert.Equal(0, session.Brains!.Session.BotCount);
        Assert.Equal(2, workers.Pool.Alive);
        Assert.Equal(2, workers.Starts);
        second.End();
        Assert.Equal(0, workers.Pool.Alive);
    }

    [Fact]
    public void A_lent_body_gets_a_brain_whose_intent_reaches_the_motion()
    {
        var (rig, session, _, motion) = Make(inline: true);
        var (_, body) = rig.Lent("soldier");
        rig.Heroes.Put(rig.Level, Centre(0) + At(0, 220));   // in front: the body faces +Y
        rig.Tick(30);
        Assert.Equal(body.Lease.Actor, session.Brains!.BrainAt(body.Index));
        Assert.Contains(motion.Orders, o => o.Body == body.Index && o.Look.Kind == LookKind.Actor && o.Look.Actor == session.Brains.HeroSlots.ActorOf(1));
        Assert.Contains(motion.Holds, h => h == body.Index);   // the hold given when its brain was added
    }

    [Fact]
    public void A_heard_gunshot_reaches_the_far_simulation_and_alerts_a_sleeping_pack()
    {
        var (rig, session, _, _) = Make(inline: true);
        var roster = rig.Roster;
        int pack = Enumerable.Range(0, roster.PackCount).First(p => roster.PackRoom(p) == 1);
        Assert.Equal(AlertLevel.Calm, roster.PackAlert(pack));
        int first = roster.PackFirst(pack);
        Assert.Equal(RobotState.Asleep, roster.State(first));

        session.Heard(SoundKind.Gunfire, new Vector(Centre(1).X, Centre(1).Y, 0f), 600f, 0);
        rig.Tick((int)rig.Controller.FarInterval + 1);

        Assert.Equal(1, session.FarSounds);
        Assert.Equal(AlertLevel.Alerted, roster.PackAlert(pack));
        Assert.Equal(0, session.TakeFarSounds().Length);
    }

    [Fact]
    public void A_second_call_at_the_same_tick_steps_nothing()
    {
        var (rig, session, _, _) = Make(inline: true);
        rig.Lent("soldier");
        rig.Tick(3);
        long publishes = session.Brains!.Publishes;
        session.Tick(rig.Now);
        Assert.Equal(publishes, session.Brains.Publishes);
        Assert.Equal(1, session.RepeatedTicks);
    }

    [Fact]
    public void A_robot_lent_a_body_just_after_a_gunshot_still_hears_it()
    {
        var (rig, session, _, _) = Make(inline: true);
        rig.Tick(2);
        session.Heard(SoundKind.Gunfire, new Vector(Centre(1).X, Centre(1).Y, 0f), 1500f, 0);
        rig.Tick();     // heard by no brain: none yet; the far simulation's next tick is to come
        Assert.Equal(0, session.Brains!.SoundsHeard);

        var (_, body) = rig.Lent("soldier");   // the lend falls between the sound and the far tick
        rig.Tick();
        Assert.Equal(1, session.Brains.SoundsHeard);
        Assert.Equal(body.Lease.Actor, session.Brains.BrainAt(body.Index));
    }

    [Fact]
    public void A_sound_the_horde_does_not_listen_for_is_not_queued()
    {
        var (_, session, _, _) = Make(inline: true);
        session.Heard(SoundKind.Unknown, default, 600f, 0);
        Assert.Equal(0, session.SoundsQueued);
    }

    [Fact]
    public void A_full_sound_queue_counts_what_it_refuses_and_the_line_says_events_were_lost()
    {
        var (rig, session, _, _) = Make(inline: true);
        rig.Lent("soldier");
        for (int i = 0; i < HordeSession.SoundCapacity + 3; i++)
            session.Heard(SoundKind.Footstep, new Vector(Centre(0).X, Centre(0).Y, 0f), 300f, 0);
        Assert.Equal(3, session.SoundsOverflowed);
        Assert.Contains("EVENTS LOST", session.Line());
    }

    [Fact]
    public void A_hit_on_a_body_is_damage_for_its_brain_through_the_hit_chain()
    {
        var (rig, session, _, _) = Make(inline: true);
        var (_, body) = rig.Lent("soldier");
        rig.Tick(2);
        var tally = new RobotHitTally();
        var chain = new RobotHitChain(new RigHitHooks([tally, new HordeHitSense(session)]));
        long before = session.Brains!.Session.Counters.EventsIn;
        chain.OnRobotHit(new RobotHit(body.Robot, body.Index, EntityHandle.None, 20f, DamageType.Bullet, "bullet", 20, 100, false,
                                      HitGroup.Generic, default, default));
        Assert.Equal(1, tally.Hits);
        Assert.Equal(1, session.RobotHits);
        Assert.Equal(before + 1, session.Brains.Session.Counters.EventsIn);
    }

    [Fact]
    public void A_hero_actor_and_a_robot_actor_answer_where_their_eyes_are()
    {
        var (rig, session, _, _) = Make(inline: true);
        var (_, body) = rig.Lent("soldier");
        rig.Heroes.Put(rig.Level, Centre(1));
        rig.Tick(2);
        var hero = session.Brains!.HeroSlots.ActorOf(1);
        Assert.True(session.TryEye(hero, out var eye));
        Assert.Equal(Centre(1).X, eye.X);
        Assert.True(session.TryEye(body.Lease.Actor, out var robotEye));
        Assert.True(robotEye.Z > body.Position.Z);
        Assert.False(session.TryEye(new ActorId(body.Index, body.Lease.Serial + 1), out _));
    }
}
