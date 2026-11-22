using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Descent.Server.Horde;

using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// A robot body's pose dials reach the server's skeleton (CR11): each dial the
/// net state carries to the clients (move_x, move_y, body_yaw, body_pitch) is
/// set on the server's skeleton too, in the same units, so a shot is tested
/// against the hitboxes the pose puts where the client draws them.
/// </summary>
public sealed class RobotBodyPoseTests
{
    private const string Set = "EntityRender.SetPoseParameter";

    /// <summary>A rig whose model declares the four dials at 4, 5, 6 and 7 (or the names <paramref name="missing"/> not at all).</summary>
    private static ServerHordeRig Rig(params string[] missing)
    {
        var rig = new ServerHordeRig();
        rig.Engine.ReturnsEachCall("EntityRender.LookupPoseParameter", () =>
        {
            string name = rig.Engine.Last("EntityRender.LookupPoseParameter")!.Value.ArgAs<string>(1);
            if (missing.Contains(name))
                return -1;
            return name switch
            {
                "move_x" => 4,
                "move_y" => 5,
                "body_yaw" => 6,
                "body_pitch" => 7,
                _ => -1,
            };
        });
        return rig;
    }

    /// <summary>The (parameter, value) pairs set on <paramref name="body"/>'s server skeleton since <paramref name="from"/>.</summary>
    private static List<(int Parameter, float Value)> SetsOn(ServerHordeRig rig, RobotBody body, int from = 0) =>
        rig.Engine.AllOf(Set).Where(c => c.ArgAs<EntityHandle>(0) == body.Handle)
           .Select(c => (c.ArgAs<int>(1), c.ArgAs<float>(2))).Skip(from).ToList();

    [Fact]
    public void A_pose_sets_each_dial_on_the_server_skeleton_at_the_net_states_value()
    {
        var rig = Rig();
        var (_, body) = rig.Lent("soldier");
        int before = SetsOn(rig, body).Count;

        body.Pose(0.5f, -0.25f, 10f, -20f);

        Assert.Equal([(4, 0.5f), (5, -0.25f), (6, 10f), (7, -20f)], SetsOn(rig, body, before));
        Assert.Equal((0.5f, -0.25f, 10f, -20f), (body.State.MoveX, body.State.MoveY, body.State.BodyYaw, body.State.BodyPitch));
    }

    [Fact]
    public void A_dial_that_did_not_change_sets_nothing_on_the_server()
    {
        var rig = Rig();
        var (_, body) = rig.Lent("soldier");
        body.Pose(0.5f, -0.25f, 10f, -20f);
        int before = SetsOn(rig, body).Count;

        body.Pose(0.5f, -0.25f, 10f, -45f);

        Assert.Equal([(7, -45f)], SetsOn(rig, body, before));
    }

    /// <summary>A body lent again starts straight on the server as on the clients: the last robot's lean is not left on its hitboxes.</summary>
    [Fact]
    public void A_lend_zeroes_the_server_skeletons_dials()
    {
        var rig = Rig();
        var (id, body) = rig.Lent("soldier");
        body.Pose(0.5f, -0.25f, 10f, -20f);
        int before = SetsOn(rig, body).Count;

        body.Lend(body.Lease, id, body.Model, 1f, body.Position, body.Yaw, 100, 100, 0, 1, default, 0);

        Assert.Equal([(4, 0f), (5, 0f), (6, 0f), (7, 0f)], SetsOn(rig, body, before));
    }

    [Fact]
    public void A_model_without_a_dial_skips_it()
    {
        var rig = Rig("body_yaw");
        var (_, body) = rig.Lent("soldier");
        int before = SetsOn(rig, body).Count;

        body.Pose(0.5f, -0.25f, 10f, -20f);

        Assert.Equal([(4, 0.5f), (5, -0.25f), (7, -20f)], SetsOn(rig, body, before));
    }

    /// <summary>The dials are looked up once per model: a pose on the model worn asks no names again, a new model asks again.</summary>
    [Fact]
    public void The_dials_are_looked_up_again_only_after_a_model_change()
    {
        var rig = Rig();
        var (id, body) = rig.Lent("soldier");
        int asked = rig.Engine.CountOf("EntityRender.LookupPoseParameter");
        body.Pose(0.5f, -0.25f, 10f, -20f);
        int afterPose = rig.Engine.CountOf("EntityRender.LookupPoseParameter");

        body.Lend(body.Lease, id, RobotLooks.HeavyModel, 1f, body.Position, body.Yaw, 100, 100, 0, 1, default, 0);

        Assert.Equal((asked, asked + 4), (afterPose, rig.Engine.CountOf("EntityRender.LookupPoseParameter")));
    }

    /// <summary>The run sequence's move_x reaches the server's skeleton with the net state's.</summary>
    [Fact]
    public void Playing_the_run_sets_move_x_on_the_server()
    {
        var rig = Rig();
        var (_, body) = rig.Lent("soldier");
        int before = SetsOn(rig, body).Count;

        body.Play(3, running: true);

        Assert.Equal([(4, 1f)], SetsOn(rig, body, before));
    }
}
