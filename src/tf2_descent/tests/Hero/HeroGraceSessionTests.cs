using SourceSharp.Abi;
using SourceSharp.Ai;

using TF2.Descent.Ai.Senses;
using TF2.Descent.Server.Hero;
using TF2.Descent.Server.Horde.Session;
using TF2.Descent.Tests.Horde;

using Xunit;

using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// The respawn's grace in the server's horde (this lane; plan_descent_logic 4.7): the session reads
/// the downed state's board (<see cref="RespawnGraces"/>) each tick, so a hero in his grace is
/// hidden from every brain's sight and his steps go unheard; after it lapses by time the robots
/// that watched him stay blind to death.json's <c>respawnBlindSeconds</c> after the respawn; his
/// first act ends all of it. His death drops every sound he made that no robot has heard yet, and
/// the lamp and the dark (<c>IHeroVisibility</c>) scale every robot's range for him.
/// </summary>
public sealed class HeroGraceSessionTests
{
    private const int Seat = 1;

    private sealed class Rig
    {
        public Rig()
        {
            HordeSession? session = null;
            Clock = new RigClock(null);
            Horde = new ServerHordeRig(null, brains: r =>
            {
                session = new HordeSession(new RecordingMotion(), new CountingActions(), r.SightWorld, r.Heroes, new RigRules(), Clock,
                                           new NoLandings(), new HordeWorkers(), DeclaredConVars.Holding<HordeAiSyncConVar>("1"),
                                           DeclaredConVars.Holding<HordeAiThreadsConVar>("1"), DeclaredConVars.Fresh<HordeAiRecordConVar>(),
                                           new RigMod(Path.GetTempPath()), r.Log, new RigAttackWorld(), r.Nav(clock: Clock), r.Motion,
                                           Graces, Visibility);
                return session;
            });
            Clock.Rig = Horde;
            Session = session!;
        }

        public ServerHordeRig Horde { get; }
        public HordeSession Session { get; }
        public RigClock Clock { get; }
        public RespawnGraces Graces { get; } = new();
        public RigVisibility Visibility { get; } = new();
        public long Now => Horde.Now;

        /// <summary>The body lent in room 0, facing +Y.</summary>
        public int Body { get; private set; }

        /// <summary>A soldier lent in room 0 (facing +Y) and the hero in front of it, 220 units off.</summary>
        public Rig WithARobotFacingTheHero(float distance = 220f)
        {
            var (_, body) = Horde.Lent("soldier");
            Body = body.Index;
            Horde.Heroes.Put(Horde.Level, Centre(0) + At(0, distance));
            return this;
        }

        public bool Sees => Session.Brains!.Vision.Holds(Body, 0);
    }

    private static long Ticks(double seconds) => (long)Math.Ceiling(seconds * ServerHordeRig.Tps);

    private static readonly long GraceTicks = Ticks(TheRules.Content.Death.Downed.RespawnGraceSeconds);
    private static readonly long BlindTicks = Ticks(TheRules.Content.Death.Downed.RespawnBlindSeconds);

    //-------------------------------------------------------------------------
    // The veil by the board

    [Fact]
    public void A_hero_with_no_grace_is_seen_by_the_ordinary_rules()
    {
        var rig = new Rig();

        Assert.Equal(HeroVeil.None, rig.Session.VeilOf(Seat, rig.Now));
    }

    [Fact]
    public void A_hero_in_his_respawns_grace_is_hidden()
    {
        var rig = new Rig();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);

        Assert.Equal(HeroVeil.Hidden, rig.Session.VeilOf(Seat, rig.Now + GraceTicks - 1));
    }

    [Fact]
    public void The_tick_his_grace_lapses_he_is_watched_to_the_blind_ceiling_then_seen()
    {
        var rig = new Rig();
        long from = rig.Now;
        rig.Graces.Began(Seat, from, from + GraceTicks);

        Assert.Equal(HeroVeil.Watched, rig.Session.VeilOf(Seat, from + GraceTicks));
        Assert.Equal(HeroVeil.Watched, rig.Session.VeilOf(Seat, from + BlindTicks - 1));
        Assert.Equal(HeroVeil.None, rig.Session.VeilOf(Seat, from + BlindTicks));
    }

    [Fact]
    public void His_first_act_lifts_the_veil_at_once()
    {
        var rig = new Rig();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);

        rig.Graces.Acted(Seat);

        Assert.Equal(HeroVeil.None, rig.Session.VeilOf(Seat, rig.Now + 1));
    }

    //-------------------------------------------------------------------------
    // Through the session's tick

    [Fact]
    public void A_robot_facing_a_hero_in_his_grace_never_sees_him()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);

        rig.Horde.Tick((int)GraceTicks - 1);

        Assert.False(rig.Sees);
        Assert.Equal(0, rig.Session.Brains!.Vision.Gains);
        Assert.True(rig.Session.Brains.Vision.Watching(rig.Body, 0), "the stimulus: the robot had him in view");
    }

    [Fact]
    public void Without_a_grace_the_same_robot_sees_him()
    {
        var rig = new Rig().WithARobotFacingTheHero();

        rig.Horde.Tick(60);

        Assert.True(rig.Sees);   // the control for the fact above
    }

    [Fact]
    public void A_hero_who_acts_in_his_grace_is_seen_by_the_robot_facing_him()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);
        rig.Horde.Tick(60);
        Assert.False(rig.Sees);

        rig.Graces.Acted(Seat);
        rig.Horde.Tick(60);

        Assert.True(rig.Sees);
    }

    [Fact]
    public void A_robot_that_did_not_have_him_in_view_through_his_grace_sees_him_once_it_lapses()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);
        rig.Horde.SightWorld.Pvs = false;   // a wall between them through the grace
        rig.Horde.Tick((int)GraceTicks + 1);
        Assert.False(rig.Session.Brains!.Vision.Watching(rig.Body, 0));

        rig.Horde.SightWorld.Pvs = true;
        rig.Horde.Tick(60);

        Assert.True(rig.Sees);
    }

    [Fact]
    public void A_robot_that_watched_him_through_his_grace_stays_blind_after_it_while_he_stands_in_view()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);

        rig.Horde.Tick((int)GraceTicks + 200);

        Assert.Equal(HeroVeil.Watched, rig.Session.VeilOf(Seat, rig.Now));
        Assert.False(rig.Sees);
    }

    [Fact]
    public void Past_the_blind_ceiling_a_robot_still_facing_him_sees_him()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);

        rig.Horde.Tick((int)BlindTicks + 60);

        Assert.True(rig.Sees);
    }

    [Fact]
    public void A_graced_heros_steps_go_unheard_and_his_shot_is_heard()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);
        rig.Horde.Tick(2);
        var at = new Vector(Centre(0).X, Centre(0).Y + 220f, 0f);
        long heard = rig.Session.Brains!.SoundsHeard;

        rig.Session.Heard(SoundKind.Footstep, at, 600f, Seat);
        rig.Horde.Tick();
        Assert.Equal((heard, 1L), (rig.Session.Brains.SoundsHeard, rig.Session.GraceSilenced));

        rig.Session.Heard(SoundKind.Gunfire, at, 1500f, Seat);
        rig.Horde.Tick();
        Assert.Equal(heard + 1, rig.Session.Brains.SoundsHeard);
    }

    [Fact]
    public void A_heros_steps_with_no_grace_are_heard()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(2);
        long heard = rig.Session.Brains!.SoundsHeard;

        rig.Session.Heard(SoundKind.Footstep, new Vector(Centre(0).X, Centre(0).Y + 220f, 0f), 600f, Seat);
        rig.Horde.Tick();

        Assert.Equal(heard + 1, rig.Session.Brains.SoundsHeard);   // the control for the fact above
    }

    //-------------------------------------------------------------------------
    // The death

    [Fact]
    public void His_death_drops_the_sounds_he_made_that_no_robot_has_heard()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(2);
        var at = new Vector(Centre(1).X, Centre(1).Y, 0f);
        rig.Session.Heard(SoundKind.Gunfire, at, 1500f, Seat);
        rig.Session.Heard(SoundKind.Gunfire, at, 1500f, 0);   // a sound nobody made: kept

        rig.Session.HeroGone(Seat);
        rig.Horde.Tick();

        Assert.Equal(1, rig.Session.TakeFarSounds().Length);
    }

    [Fact]
    public void His_death_through_the_downed_tap_forgets_him_and_his_respawn_brings_him_back()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(60);
        Assert.True(rig.Sees);
        var tap = new DownedHordeTap(rig.Session);
        rig.Horde.Heroes.Heroes.Clear();   // dead: no longer gathered

        tap.HeroDied(Seat, default);
        rig.Horde.Tick(2);

        Assert.Equal((1L, 0L), (tap.Forgotten, tap.Returned));
        Assert.False(rig.Sees);
        Assert.Equal(-1, rig.Horde.Roster.Carried(rig.Horde.Roster.Count - 1).TargetSeat);
        tap.HeroRespawned(Seat, default);
        Assert.Equal(1L, tap.Returned);
    }

    [Fact]
    public void His_death_drops_his_sounds_waiting_for_the_far_simulation()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(2);
        rig.Session.TakeFarSounds();
        var at = new Vector(Centre(1).X, Centre(1).Y, 0f);
        rig.Session.Heard(SoundKind.Gunfire, at, 1500f, Seat);
        rig.Session.Heard(SoundKind.Gunfire, at, 1500f, 0);
        rig.Horde.Tick();   // heard: queued for the far simulation's next tick, a second off

        rig.Session.HeroGone(Seat);

        Assert.Equal(-1, Assert.Single(rig.Session.TakeFarSounds().ToArray()).Seat);
    }

    [Fact]
    public void A_robot_lent_a_body_just_after_his_death_does_not_hear_his_last_shot()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(2);
        rig.Session.Heard(SoundKind.Gunfire, new Vector(Centre(1).X, Centre(1).Y, 0f), 1500f, Seat);
        rig.Horde.Tick();
        long heard = rig.Session.Brains!.SoundsHeard;

        rig.Session.HeroGone(Seat);
        rig.Horde.Heroes.Heroes.Clear();
        rig.Horde.Lent("soldier");   // a body lent between his last shot and the far simulation's tick
        rig.Horde.Tick();

        Assert.Equal(heard, rig.Session.Brains.SoundsHeard);
    }

    [Fact]
    public void A_sound_he_owns_after_his_death_is_no_lead()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(2);
        long heard = rig.Session.Brains!.SoundsHeard;
        rig.Session.HeroGone(Seat);

        rig.Session.Heard(SoundKind.Death, new Vector(Centre(0).X, Centre(0).Y + 220f, 0f), 1500f, Seat);   // his death cry
        rig.Horde.Tick();

        Assert.Equal((heard, 1L), (rig.Session.Brains.SoundsHeard, rig.Session.DeadSilenced));
    }

    [Fact]
    public void Once_his_respawn_is_told_his_sounds_are_heard_again()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(2);
        long heard = rig.Session.Brains!.SoundsHeard;
        rig.Session.HeroGone(Seat);
        rig.Session.HeroBack(Seat);

        rig.Session.Heard(SoundKind.Gunfire, new Vector(Centre(0).X, Centre(0).Y + 220f, 0f), 1500f, Seat);
        rig.Horde.Tick();

        Assert.Equal((heard + 1, 0L), (rig.Session.Brains.SoundsHeard, rig.Session.DeadSilenced));
    }

    [Fact]
    public void A_dead_hero_who_stands_before_the_downed_state_tells_his_respawn_is_hidden()
    {
        var rig = new Rig();
        rig.Graces.Died(Seat);

        Assert.Equal(HeroVeil.Hidden, rig.Session.VeilOf(Seat, rig.Now));
    }

    //-------------------------------------------------------------------------
    // The dev print

    private static string SightPrint(Rig rig)
    {
        int from = rig.Horde.Log.Lines.Count;
        new HordeSightCommand(rig.Session, rig.Visibility, rig.Graces, rig.Clock, rig.Horde.Log).Execute(rig.Horde.Roster.Count - 1);
        return Assert.Single(rig.Horde.Log.Lines.Skip(from)).Text;
    }

    [Fact]
    public void The_sight_print_says_a_robot_does_not_see_a_hero_in_his_grace_and_why()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Graces.Began(Seat, rig.Now, rig.Now + GraceTicks);
        rig.Horde.Tick(30);

        string line = SightPrint(rig);

        Assert.Contains("-> player 1: does not see him: he is in his respawn's grace;", line);
        Assert.Contains("(in; x1.00, scale 1: the rig's)", line);
        Assert.Contains("pvs yes; line eye clear chest clear;", line);
        Assert.Contains("s left (untouchable), veil Hidden (watching him since his grace)", line);
    }

    [Fact]
    public void The_sight_print_says_a_robot_sees_a_hero_with_no_grace()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Horde.Tick(60);

        string line = SightPrint(rig);

        Assert.Contains("-> player 1: SEES him;", line);
        Assert.Contains("grace none, veil None", line);
    }

    [Fact]
    public void The_sight_print_names_a_scaled_range_that_falls_short()
    {
        var rig = new Rig().WithARobotFacingTheHero();
        rig.Visibility.Scale = 0.01;
        rig.Horde.Tick(30);

        string line = SightPrint(rig);

        Assert.Contains("does not see him: out of range;", line);
        Assert.Contains("(OUT; x0.01, scale 0.01: the rig's)", line);
    }

    //-------------------------------------------------------------------------
    // The lamp and the dark

    [Fact]
    public void The_heros_visibility_scales_every_robots_range_for_him()
    {
        var rig = new Rig().WithARobotFacingTheHero(distance: 240f);
        rig.Horde.Tick();
        float range = rig.Session.Brains!.SenseOf(rig.Body).Range;
        rig.Visibility.Scale = 200.0 / range;   // his scaled range falls short of him

        rig.Horde.Tick(120);

        Assert.False(rig.Sees);
        rig.Visibility.Scale = 1.0;
        rig.Horde.Tick(120);
        Assert.True(rig.Sees);
    }
}
