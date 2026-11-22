using SourceSharp.Abi;
using SourceSharp.Server;
using TF2.Server.Buildings;
using TF2.Shared;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// <c>CBaseObject::DetermineAnimation</c> (tf_obj.cpp:3121-3169) over the base
/// think: a finished object runs, a climbing one upgrades, and the startup a
/// fresh object shows gives way to running only once its sequence has played
/// out. Each fact drives the real <see cref="TFBaseObject.OnThink"/> a standing
/// object's own think reaches.
/// </summary>
public class TFObjectAnimationTests
{
    private const int AssemblingSequence = 9;
    private const int UpgradingSequence = 10;
    private const int StartupSequence = 20;
    private const int RunningSequence = 30;

    /// <summary>
    /// Answer each activity with its own sequence, so a fact can tell which arm
    /// of DetermineAnimation played — the rig's plain <c>Returns</c> answers one
    /// number to every activity.
    /// </summary>
    private static void ArmActivities(BuildingsRig rig)
    {
        rig.Engine.ReturnsEachCall("EntityRender.LookupSequenceByActivity", () =>
        {
            string activity = (rig.Engine.Last("EntityRender.LookupSequenceByActivity") ?? default).ArgAs<string>(1);
            return activity switch
            {
                "ACT_OBJ_ASSEMBLING" => AssemblingSequence,
                "ACT_OBJ_UPGRADING" => UpgradingSequence,
                "ACT_OBJ_STARTUP" => StartupSequence,
                "ACT_OBJ_RUNNING" => RunningSequence,
                _ => -1,
            };
        });
    }

    /// <summary>A building whose construction has just finished (its startup shown), sitting at one origin.</summary>
    private static (BuildingsRig Rig, TestBuilding Building) AFinishedBuilding()
    {
        var rig = new BuildingsRig();
        ArmActivities(rig);
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);
        rig.Clock.Now = 100f;
        building.StartBuilding();
        rig.Clock.Now += TFObjectInfo.Of(TFObjectType.Sentrygun)!.BuildTime;
        building.OnThink();   // Construct reaches full time: FinishedBuilding shows the startup
        return (rig, building);
    }

    private static EngineCall LastPlay(BuildingsRig rig, TestBuilding building)
        => rig.Engine.AllOf("EntityRender.PlaySequence")
                     .Last(c => c.ArgAs<EntityHandle>(0) == building.Handle);

    [Fact]
    public void AFinishedObjectsThinkPlaysItsRunningSequence()
    {
        var (rig, building) = AFinishedBuilding();

        building.OnThink();

        // The startup has run out (the rig's clock answers a played-out
        // sequence), so DetermineAnimation's STARTUP arm moves the object to
        // ACT_OBJ_RUNNING — the sentry1 sequence whose pose blends the aim.
        EngineCall played = LastPlay(rig, building);
        Assert.Equal(RunningSequence, played.ArgAs<int>(1));
        Assert.Equal(1f, played.ArgAs<float>(2), 3);
    }

    [Fact]
    public void AStartupThatHasNotFinishedIsNotReplaced()
    {
        var (rig, building) = AFinishedBuilding();
        rig.Engine.Returns("EntityRender.SequenceState.cycle", 0.4f);   // the startup is still playing

        building.OnThink();

        // IsActivityFinished is false, so DetermineAnimation leaves the startup
        // alone (desired == current) rather than cutting it off.
        Assert.Equal(StartupSequence, LastPlay(rig, building).ArgAs<int>(1));
    }

    [Fact]
    public void AnUpgradingObjectsThinkPlaysItsUpgradingSequence()
    {
        var (rig, building) = AFinishedBuilding();

        building.StartUpgrading();
        building.OnThink();

        // Upgrading wins the default arm (tf_obj.cpp:3129-3132), so the think
        // keeps ACT_OBJ_UPGRADING rather than assembling or running.
        Assert.Equal(UpgradingSequence, LastPlay(rig, building).ArgAs<int>(1));
    }

    [Fact]
    public void APlacingObjectsThinkKeepsTheActivityItHas()
    {
        var rig = new BuildingsRig();
        ArmActivities(rig);
        var builder = rig.AddBuilder();
        var building = rig.Spawn<TestBuilding>(TestBuilding.MapClass);
        building.StartPlacement(builder);

        building.OnThink();

        // Stock's IsPlacing arm has its body commented out (tf_obj.cpp:3133-3145):
        // the blueprint keeps what it has, rather than taking the running sequence.
        Assert.DoesNotContain(rig.Engine.AllOf("EntityRender.PlaySequence"),
                              c => c.ArgAs<EntityHandle>(0) == building.Handle);
    }

    [Fact]
    public void AThinkDoesNotRestartTheSequenceAlreadyPlaying()
    {
        var (rig, building) = AFinishedBuilding();
        building.OnThink();   // startup gives way to running

        building.OnThink();
        building.OnThink();

        // DetermineAnimation runs every think; a sequence it has already chosen
        // must not be replayed, or the animation would freeze on its first frame.
        Assert.Equal(1, rig.Engine.AllOf("EntityRender.PlaySequence")
                                .Count(c => c.ArgAs<EntityHandle>(0) == building.Handle
                                            && c.ArgAs<int>(1) == RunningSequence));
    }
}
