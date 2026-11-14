extern alias client;

using System.Reflection;

using SourceSharp.Abi;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientRobot = client::TF2.Descent.Client.Robots.ClientRobot;
using ClientRobotPoses = client::TF2.Descent.Client.Robots.ClientRobotPoses;
using ClientStandaloneEntity = client::SourceSharp.Client.Standalone.ClientStandaloneEntity;
using Flags = client::TF2.Descent.Shared.RobotFlags;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// What the client draws of HO12's flags on a robot body: a cloaked spy all but invisible, and a
/// champion's über with TF2's invulnerable sheen; each taken off when its flag goes.
/// </summary>
public sealed class ClientRobotLooksTests
{
    private static ClientRobot Robot(ClientEngine engine)
    {
        var r = new ClientRobot(engine, new ClientRobotPoses());
        typeof(ClientStandaloneEntity).GetProperty("Index")!.SetValue(r, 90);
        typeof(ClientStandaloneEntity).GetProperty("Handle")!.SetValue(r, new EntityHandle((uint)((7 << 16) | 90)));
        typeof(ClientStandaloneEntity).GetProperty("Entities", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(r, engine);
        r.State.BindLocal();
        engine.Returns("EntityRender.LookupPoseParameter", -1);
        return r;
    }

    [Fact]
    public void A_cloaked_robot_is_drawn_all_but_invisible_and_whole_again_uncloaked()
    {
        var engine = new ClientEngine();
        var robot = Robot(engine);
        robot.State.Flags = (byte)(Flags.Lent | Flags.Cloaked);

        robot.OnDataChanged(default, DataUpdateType.DataTableChanged);
        var on = Assert.Single(engine.AllOf("EntityRender.SetBlending"));
        Assert.Equal((true, ClientRobot.CloakAlpha), (on.ArgAs<bool>(1), on.ArgAs<int>(3)));
        Assert.True(robot.DrawnCloaked);

        robot.State.Flags = (byte)Flags.Lent;
        robot.OnDataChanged(default, DataUpdateType.DataTableChanged);
        var off = engine.AllOf("EntityRender.SetBlending").Last();
        Assert.Equal((false, 255), (off.ArgAs<bool>(1), off.ArgAs<int>(3)));
    }

    [Fact]
    public void An_ubered_robot_wears_the_invulnerable_sheen_till_the_flag_goes()
    {
        var engine = new ClientEngine();
        var robot = Robot(engine);
        robot.State.Flags = (byte)(Flags.Lent | Flags.Uber);

        robot.OnDataChanged(default, DataUpdateType.DataTableChanged);
        Assert.Equal(ClientRobot.UberMaterial, Assert.Single(engine.AllOf("EntityRender.SetMaterialOverride")).ArgAs<string>(1));

        robot.State.Flags = (byte)Flags.Lent;
        robot.OnDataChanged(default, DataUpdateType.DataTableChanged);
        Assert.Equal("", engine.AllOf("EntityRender.SetMaterialOverride").Last().ArgAs<string>(1));
    }

    [Fact]
    public void A_plain_robot_sets_neither()
    {
        var engine = new ClientEngine();
        var robot = Robot(engine);
        robot.State.Flags = (byte)Flags.Lent;

        robot.OnDataChanged(default, DataUpdateType.DataTableChanged);

        Assert.Empty(engine.AllOf("EntityRender.SetBlending"));
        Assert.Empty(engine.AllOf("EntityRender.SetMaterialOverride"));
    }
}
