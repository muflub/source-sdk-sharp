using SourceSharp.Abi;
using SourceSharp.Tests;
using TF2.Server.Buildings;
using Xunit;

namespace TF2.Tests.Buildings;

/// <summary>
/// The entrance's arrow (<c>ShowDirectionArrow</c>, tf_obj_teleporter.cpp:1215-1241):
/// the body digit of <c>teleporter_direction</c> comes on while the match is ready
/// (the rig's hdr double carries the group), the yaw onto the match rides the
/// <c>direction</c> pose parameter, and losing the match takes the arrow off again.
/// </summary>
public class TFTeleporterArrowFacts
{
    private static RecordingServerSkins Skins(BuildingsRig rig)
        => (RecordingServerSkins)rig.Registry.Services!.Resolve(typeof(IServerSkins))!;

    /// <summary>The stubbed pair thinks one beat at its own delay.</summary>
    private static void Beat(BuildingsRig rig, TFTeleporter entrance, TFTeleporter exit)
        => TeleporterHarness.Tick(rig, entrance, exit, TFTeleporter.TeleporterThinkDelay);

    /// <summary>The rig's seams the arrow's pose push asks: a model worn and the
    /// <c>direction</c> parameter at the first slot, as the sentry's pose facts arm
    /// theirs (TFSentryGunTests.cs:760-765).</summary>
    private static void ServeArrowPose(BuildingsRig rig)
    {
        rig.Engine.Returns("EntityRender.GetModelName", "models/buildables/teleporter_entrance.mdl");
        rig.Engine.ReturnsEachCall("EntityRender.LookupPoseParameter", () =>
        {
            string name = (rig.Engine.Last("EntityRender.LookupPoseParameter") ?? default).ArgAs<string>(1);
            return name == "direction" ? 0 : -1;
        });
    }

    [Fact]
    public void TheEntranceShowsTheArrowAndPointsItAtItsMatch()
    {
        // A ready match, the stubbed world answering every origin as it stands:
        // the heading is the yaw-zero line, flipped by stock's pose-param
        // convention to anglemod(180) = 180 (:1238).
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();   // the rig's hdr double carries the arrow group
        ServeArrowPose(rig);

        Beat(rig, entrance, exit);

        var skins = Skins(rig);
        Assert.Single(skins.Bodies);                              // one flip, the entrance's own
        Assert.Equal(entrance.Handle, skins.Bodies[0].Entity);
        Assert.Equal(1, skins.Bodies[0].Body);                    // the arrow digit, group on

        var pose = rig.Engine.Last("EntityRender.SetPoseParameter");
        Assert.NotNull(pose);
        Assert.Equal(entrance.Handle, pose!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal(180f, pose!.Value.ArgAs<float>(2));          // anglemod(-0 + 180)

        // The exit is mode 1 without the bidirectional hook: stock's guard
        // (:1091) keeps it plain — its handle wrote nothing to either seam.
        Assert.DoesNotContain(skins.Bodies, b => b.Entity == exit.Handle);
    }

    [Fact]
    public void TheEntranceHidesTheArrowWhenItsMatchIsLost()
    {
        var (rig, entrance, exit, _) = TeleporterHarness.Pair();
        ServeArrowPose(rig);
        Beat(rig, entrance, exit);                                // shown

        exit.SetPlasmaDisabled(5f);                               // the match stops being ready
        Beat(rig, entrance, exit);                                // the refusal hides it (:1056-1057)

        var skins = Skins(rig);
        Assert.Equal(2, skins.Bodies.Count);
        Assert.Equal(0, skins.Bodies[^1].Body);                   // the arrow digit off
        Assert.Single(rig.Engine.AllOf("EntityRender.SetPoseParameter"));   // the yaw was not rewritten
    }
}
