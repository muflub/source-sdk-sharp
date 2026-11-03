using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Resources;
using TF2.Shared.Rules;

using TF2.Tests.Entities;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Entities;

/// <summary>
/// <c>tf_logic_cp_timer</c> (<c>CCPTimerLogic</c>, shared/tf/tf_gamerules.cpp:
/// 20002-20114) and the <c>TimerMayExpire</c> walk over it (L14033-14041): the
/// attack/defend countdown for one team and one point — it starts while that
/// team may take the point, ends only once the point is fully owned, and holds
/// the round timer open for as long as the team can still capture.
/// </summary>
public sealed class CPTimerLogicTests
{
    private const int Red = (int)TFTeam.Red;
    private const int Blue = (int)TFTeam.Blue;

    /// <summary>
    /// A 5cp-shaped map: the point BLU's countdown guards, a master to number
    /// the points, and the timer itself finding its point on the round's
    /// RoundSpawn. The clock starts at 100.
    /// </summary>
    private static (ObjectiveRig Rig, TeamControlPoint Point, CPTimerLogic Timer,
                    ObjectiveResourceNetState State) Rig()
    {
        var rig = new ObjectiveRig();
        var point = rig.Point("cp_mid", 0, TFTeam.Red);
        rig.Master();
        var timer = rig.Spawn<CPTimerLogic>(CPTimerLogic.MapClass, ("targetname", "cp_timer"),
                                            ("controlpoint", "cp_mid"), ("timer_length", "30"), ("team_number", "3"));
        var state = new ObjectiveResourceNetState();
        state.BindLocal();
        rig.Objectives.Attach(state);
        rig.Clock.Now = 100f;
        rig.StartRound();
        return (rig, point, timer, state);
    }

    [Fact]
    public void The_countdown_starts_when_its_team_may_take_the_point()
    {
        var (rig, _, timer, state) = Rig();
        rig.Settings.CapLinear = false;   // the point may be taken from here

        timer.OnThink();

        Assert.Equal(1, timer.OnCountdownStart.FireCount);
        Assert.Equal(130f, state.TimerTimes[0]);   // curtime + timer_length
    }

    [Fact]
    public void The_countdown_does_not_start_while_the_point_may_not_be_taken()
    {
        var (rig, point, timer, _) = Rig();
        rig.Settings.CapLinear = true;                          // the caplinear walk decides…
        rig.Objectives.SetCPLocked(point.PointIndex, true);     // …and this point is locked

        timer.OnThink();

        Assert.Equal(0, timer.OnCountdownStart.FireCount);
    }

    [Fact]
    public void A_countdown_underway_does_not_start_again()
    {
        var (rig, _, timer, _) = Rig();
        rig.Settings.CapLinear = false;
        timer.OnThink();

        rig.Clock.Now = 110f;
        timer.OnThink();

        Assert.Equal(1, timer.OnCountdownStart.FireCount);
    }

    [Fact]
    public void A_countdown_the_team_lost_resets_and_the_next_chance_starts_it_again()
    {
        var (rig, point, timer, state) = Rig();
        rig.Settings.CapLinear = false;
        timer.OnThink();
        Assert.Equal(130f, state.TimerTimes[0]);

        rig.Settings.CapLinear = true;                              // the caplinear walk decides…
        rig.Objectives.SetCPLocked(point.PointIndex, true);          // …no longer takeable: stock invalidates
        rig.Clock.Now = 110f;
        timer.OnThink();

        rig.Settings.CapLinear = false;                             // takeable again
        rig.Clock.Now = 120f;
        timer.OnThink();

        Assert.Equal(2, timer.OnCountdownStart.FireCount);
        Assert.Equal(150f, state.TimerTimes[0]);
    }

    [Fact]
    public void The_countdown_end_waits_for_the_point_to_be_fully_owned()
    {
        var (rig, _, timer, state) = Rig();
        rig.Settings.CapLinear = false;
        timer.OnThink();

        rig.Objectives.SetCappingTeam(0, Blue);   // the point is being taken: stock will not reset
        rig.Clock.Now = 131f;                      // past the 30 s countdown
        timer.OnThink();
        Assert.Equal(0, timer.OnCountdownEnd.FireCount);

        rig.Objectives.SetCappingTeam(0, (int)TFTeam.Unassigned);
        timer.OnThink();

        Assert.Equal(1, timer.OnCountdownEnd.FireCount);
        Assert.Equal(-1f, state.TimerTimes[0]);
    }

    [Fact]
    public void A_team_win_invalidates_the_countdown()
    {
        var (rig, _, timer, state) = Rig();
        rig.Settings.CapLinear = false;
        timer.OnThink();
        Assert.Equal(130f, state.TimerTimes[0]);

        rig.Round.State = RoundState.TeamWin;   // won, our job is done
        rig.Clock.Now = 110f;
        timer.OnThink();

        // Stock's team-win arm invalidates and — there is no return (L20056-
        // 20061) — runs on into the block below, which starts the countdown
        // anew while the team may still take the point: stock's quirk, kept.
        Assert.Equal(2, timer.OnCountdownStart.FireCount);
        Assert.Equal(140f, state.TimerTimes[0]);   // 110 + 30, not the old 130
    }

    [Fact]
    public void The_timer_may_not_expire_while_its_team_can_still_take_the_point()
    {
        var (rig, point, timer, _) = Rig();
        rig.Settings.CapLinear = false;   // the point may be taken from here

        Assert.False(timer.TimerMayExpire(), "BLU can take the point, so the round timer holds");

        rig.Settings.CapLinear = true;                            // the caplinear walk decides…
        rig.Objectives.SetCPLocked(point.PointIndex, true);        // …the point is locked

        Assert.True(timer.TimerMayExpire(), "with the point locked the timer may expire");
    }

    [Fact]
    public void An_entity_naming_a_point_that_is_not_there_never_starts()
    {
        var rig = new ObjectiveRig();
        var timer = rig.Spawn<CPTimerLogic>(CPTimerLogic.MapClass, ("targetname", "lonely"),
                                            ("controlpoint", "cp_nowhere"), ("timer_length", "30"), ("team_number", "3"));
        rig.StartRound();
        rig.Clock.Now = 100f;

        timer.OnThink();

        Assert.Equal(0, timer.OnCountdownStart.FireCount);
        Assert.True(timer.TimerMayExpire(), "with no point there is nothing to hold the timer");
    }

    [Fact]
    public void The_veto_walks_the_cp_timer_entities_and_holds_while_one_says_no()
    {
        var (rig, point, timer, _) = Rig();
        rig.Settings.CapLinear = false;   // the live entity's team can take its point
        var veto = new CPTimerLogicTimerVeto(rig.Registry);

        Assert.False(veto.TimerMayExpire(), "the live entity's team can still take its point");

        rig.Settings.CapLinear = true;                            // the caplinear walk decides…
        rig.Objectives.SetCPLocked(point.PointIndex, true);        // …the point is locked

        Assert.True(veto.TimerMayExpire(), "with the point locked the walk says the timer may expire");
        _ = timer;
    }

    [Fact]
    public void The_veto_sits_in_the_round_machine_s_chain_before_the_train_watchers()
    {
        var services = new ServiceContainer();
        services.AddInstance<ILog>(new RecordingLog());
        services.AddInstance<IEntityDirectory>(new ScriptedEntityRegistry(TestEngines.Server()));

        // the veto reaches the round machine only through the side's
        // registration: the scan claims the [SideService] mark, the registrar
        // names the chain, and the machine reads IHooks<ITimerExpiryVeto>
        SideServices.Register(services, Side.Server,
            [typeof(CPTimerLogicTimerVeto), typeof(TrainWatcherTimerVeto), typeof(TeamRoundRegistrar)]);

        var chain = services.Resolve<IHooks<ITimerExpiryVeto>>()!;
        Assert.Equal(2, chain.All.Count);
        var ordered = chain.All.Cast<IOrderedHook>().ToArray();
        Assert.IsType<CPTimerLogicTimerVeto>(chain.All[0]);
        Assert.IsType<TrainWatcherTimerVeto>(chain.All[1]);
        // stock walks the CP timers before the base rules' watcher walk
        Assert.True(ordered[0].HookOrder < ordered[1].HookOrder);
        Assert.InRange(ordered[0].HookOrder, 999_100, 999_199);
    }
}
