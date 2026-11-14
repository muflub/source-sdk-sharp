using SourceSharp.Ai;

using TF2.Descent.Ai.Brains;
using TF2.Descent.Tests.Horde;

using Xunit;

using static TF2.Descent.Tests.Hero.DownedRig;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Hero;

/// <summary>
/// After a death, unhosted (this lane's rule; the plan is silent): a respawn is not an
/// execution. A hero who stands again after a death takes no damage for death.json's
/// respawn grace (6 s; the rig runs 10 ticks a second, so 60 ticks), ended early by his
/// first shot, swing or cast; the listeners hear his death and his respawn, and a robot that
/// is told to forget a dead hero walks home instead of searching where he fell.
/// </summary>
public sealed class RespawnGraceTests
{
    /// <summary>A solo hero killed and stood up again at the arrival: the death, then a frame.</summary>
    private static DownedRig Respawned()
    {
        var rig = new DownedRig(A);
        rig.Hit(A, 500);
        rig.RunTo(1010);
        rig.Clock.Respawn(PlayerOf(A));
        rig.RunTo(1011);
        return rig;
    }

    [Fact]
    public void A_respawned_hero_takes_no_damage_in_his_grace()
    {
        var rig = Respawned();

        float lands = rig.Hit(A, 50);

        Assert.Equal(0f, lands);
        Assert.Equal(rig.Clock.Max, rig.Clock.Health(PlayerOf(A)));
    }

    [Fact]
    public void A_lethal_hit_in_the_grace_does_not_kill_him_again()
    {
        var rig = Respawned();

        rig.Hit(A, 5000);

        Assert.Contains(PlayerOf(A), rig.Clock.Alive);
        Assert.Single(rig.Told.Died);
    }

    [Fact]
    public void The_grace_lapses_after_six_seconds()
    {
        var rig = Respawned();
        Assert.Equal(0f, rig.Hit(A, 50));

        rig.RunTo(1011 + 60);

        Assert.Equal(50f, rig.Hit(A, 50));
    }

    [Fact]
    public void His_first_act_ends_the_grace()
    {
        var rig = Respawned();

        rig.Hero(A).Acted();
        rig.Think();

        Assert.Equal(50f, rig.Hit(A, 50));
    }

    [Fact]
    public void Acting_before_the_death_does_not_end_the_grace()
    {
        var rig = new DownedRig(A);
        rig.Hero(A).Acted();
        rig.Hit(A, 500);
        rig.RunTo(1010);
        rig.Clock.Respawn(PlayerOf(A));
        rig.RunTo(1012);

        Assert.Equal(0f, rig.Hit(A, 50));
    }

    [Fact]
    public void The_listeners_hear_the_death_and_the_respawn()
    {
        var rig = Respawned();

        Assert.Equal([A], rig.Told.Died);
        Assert.Equal([A], rig.Told.Respawned);
    }

    //-------------------------------------------------------------------------
    // The board the horde reads (RespawnGraces): written by the downed state only.

    [Fact]
    public void His_respawn_puts_his_grace_on_the_board_the_horde_reads()
    {
        var rig = Respawned();

        Assert.True(rig.Graces.TryGet(A, out var g));
        Assert.False(g.Acted);
        Assert.Equal(60, g.Until - g.From);   // six seconds at the rig's ten ticks a second
        Assert.True(rig.Graces.Untouchable(A, rig.Clock.Tick));
        Assert.Equal(rig.Downed.Untouchable(A), rig.Graces.Untouchable(A, rig.Clock.Tick));
    }

    [Fact]
    public void The_board_and_the_downed_state_agree_his_grace_lapses_at_the_same_tick()
    {
        var rig = Respawned();
        rig.Graces.TryGet(A, out var g);

        rig.RunTo((int)g.Until - 1);
        Assert.Equal((true, true), (rig.Downed.Untouchable(A), rig.Graces.Untouchable(A, rig.Clock.Tick)));
        rig.RunTo((int)g.Until);
        Assert.Equal((false, false), (rig.Downed.Untouchable(A), rig.Graces.Untouchable(A, rig.Clock.Tick)));
    }

    [Fact]
    public void His_first_act_is_marked_on_the_board()
    {
        var rig = Respawned();

        rig.Hero(A).Acted();
        rig.Think();

        Assert.True(rig.Graces.TryGet(A, out var g) && g.Acted);
        Assert.False(rig.Graces.Untouchable(A, rig.Clock.Tick));
    }

    [Fact]
    public void His_death_marks_him_dead_on_the_board()
    {
        var rig = Respawned();
        rig.RunTo(1011 + 60);

        rig.Hit(A, 5000);

        Assert.True(rig.Graces.TryGet(A, out var g) && g.Dead);
        Assert.False(rig.Graces.Untouchable(A, rig.Clock.Tick));
    }

    [Fact]
    public void His_first_death_marks_him_dead_on_the_board()
    {
        var rig = new DownedRig(A);
        Assert.False(rig.Graces.TryGet(A, out _));

        rig.Hit(A, 500);

        Assert.True(rig.Graces.TryGet(A, out var g) && g.Dead);
    }

    [Fact]
    public void A_new_level_clears_the_board()
    {
        var rig = Respawned();

        rig.Downed.OnLevelInit("tf2_descent-1-s1");

        Assert.False(rig.Graces.TryGet(A, out _));
    }

    [Fact]
    public void A_downed_hero_revived_is_not_untouchable()
    {
        var rig = new DownedRig(A, B);
        rig.Hit(A, 500);
        rig.Downed.DevRevive(A, B);
        rig.RunTo(1100);

        Assert.False(rig.Downed.IsDown(A));
        Assert.Equal(50f, rig.Hit(A, 50));
    }

    //-------------------------------------------------------------------------
    // The brain's side: what the forget does to a hunt (the event DownedHordeTap pushes).

    private static readonly ActorId Bot = new(1, 1);
    private static readonly ActorId HeroActor = new(HordeSlots.FirstHero, 1);

    /// <summary>
    /// One robot at its post that saw a hero a room away and lost him (he died); then, when
    /// <paramref name="forget"/>, told what the tap tells at a death. Answers the node its
    /// decider ends in after a few decides.
    /// </summary>
    private static string AfterTheDeath(bool forget)
    {
        var post = Centre(0);
        var level = Row(4, markers: 0);
        var s = new AiSession(new InlineTransport(), new NoRecording(), HordeSlots.Slots, HordeSlots.Sighted)
        {
            TeamCallouts = false,
            World = HordeBrains.World(0.015f),
            Curves = HordeSkillMap.Curves(),
            Deciders = HordeDeciders.For(level).Make,
        };
        s.AddBot(Bot, 42, Levels.Eleven());
        int tick = 100;
        var fell = Centre(1);
        void Step(bool seen)
        {
            tick++;
            AiSnapshot snap = s.Snapshots.BeginWrite(new Tick(tick));
            var view = snap.Add(Bot, new SelfState(post, default, default, 100, 0, HordeWeapons.RocketLauncher, -1, 0,
                                                   SelfFlags.Alive | SelfFlags.Grounded, Team: HordeBrains.RobotTeam));
            if (seen)
                view.AddSighted(new ActorFacts(HeroActor, fell, default, -1, ActorActivity.None, 1f, Team: HordeBrains.HeroTeam));
            s.Publish(snap);
        }

        s.Push(AiEvent.SightGained(Bot, new Tick(tick + 1), HeroActor, fell, default, 1f));
        for (int i = 0; i < 6; i++)
            Step(seen: true);
        s.Push(AiEvent.SightLost(Bot, new Tick(tick + 1), HeroActor, fell, default));
        if (forget)
            s.Push(AiEvent.ActorSpectating(Bot, new Tick(tick + 1), HeroActor, true));
        for (int i = 0; i < 6; i++)
            Step(seen: false);
        return s.BrainIn(1)!.Decider!.Doing.Leaf;
    }

    [Fact]
    public void A_robot_not_told_runs_to_where_the_hero_fell()
    {
        Assert.Equal(HuntDecider.NameOf(HuntDecider.Node.LastKnown), AfterTheDeath(forget: false));
    }

    [Fact]
    public void A_robot_told_to_forget_a_dead_hero_goes_home()
    {
        Assert.Equal(HuntDecider.NameOf(HuntDecider.Node.Hold), AfterTheDeath(forget: true));
    }
}
