using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Rules;

using TF2.Tests.Entities;

using Xunit;

namespace TF2.Tests.Rules;

/// <summary>
/// The capping-team walk of <c>CTFGameRules::TimerMayExpire</c>
/// (shared/tf/tf_gamerules.cpp:14004-14020), as an <see cref="ITimerExpiryVeto"/>:
/// while any control point has a capping team the round timer may not expire,
/// nor for a tenth of a second after the last cap — the cap-in-progress buffer
/// that keeps a cap's AddTime output from losing the race with a timer's
/// OnFinished.
/// </summary>
public sealed class CappingTeamTimerVetoTests
{
    private static (ObjectiveResource Objectives, FakeClock Clock, CappingTeamTimerVeto Veto) Rig()
    {
        var objectives = new ObjectiveResource();
        objectives.SetNumControlPoints(4);
        var clock = new FakeClock { Now = 100f };
        return (objectives, clock, new CappingTeamTimerVeto(objectives, clock));
    }

    [Fact]
    public void A_point_being_capped_holds_the_round_timer()
    {
        var (objectives, _, veto) = Rig();
        Assert.True(veto.TimerMayExpire(), "nothing is capping yet");

        objectives.SetCappingTeam(2, (int)TFTeam.Blue);

        Assert.False(veto.TimerMayExpire(), "a point with a capping team vetoes the expiry");
    }

    [Fact]
    public void A_cap_that_just_ended_holds_the_timer_for_a_tenth_of_a_second()
    {
        var (objectives, clock, veto) = Rig();
        objectives.SetCappingTeam(2, (int)TFTeam.Blue);
        Assert.False(veto.TimerMayExpire(), "the cap itself holds it");

        objectives.SetCappingTeam(2, (int)TFTeam.Unassigned);
        clock.Now += 0.05f;
        Assert.False(veto.TimerMayExpire(), "the cap-in-progress buffer still holds it");

        clock.Now += 0.2f;
        Assert.True(veto.TimerMayExpire(), "after the buffer the timer may expire again");
    }

    [Fact]
    public void The_veto_sits_in_the_round_machine_s_chain_before_the_train_watchers()
    {
        var services = new ServiceContainer();
        services.AddInstance<ILog>(new RecordingLog());
        var (objectives, clock, _) = Rig();
        services.AddInstance<IObjectiveResource>(objectives);
        services.AddInstance<IGameClock>(clock);
        services.AddInstance<IEntityDirectory>(new ScriptedEntityRegistry(TestEngines.Server()));

        // the veto reaches the round machine only through the side's
        // registration: the scan claims the [SideService] mark, the registrar
        // names the chain, and the machine reads IHooks<ITimerExpiryVeto>
        SideServices.Register(services, Side.Server,
            [typeof(CappingTeamTimerVeto), typeof(TrainWatcherTimerVeto), typeof(TeamRoundRegistrar)]);

        var chain = services.Resolve<IHooks<ITimerExpiryVeto>>()!;
        Assert.Equal(2, chain.All.Count);
        var ordered = chain.All.Cast<IOrderedHook>().ToArray();
        Assert.IsType<CappingTeamTimerVeto>(chain.All[0]);
        Assert.IsType<TrainWatcherTimerVeto>(chain.All[1]);
        // stock runs the capping walk before the base rules' watcher walk
        Assert.True(ordered[0].HookOrder < ordered[1].HookOrder);
        Assert.InRange(ordered[0].HookOrder, 999_100, 999_199);
    }
}
