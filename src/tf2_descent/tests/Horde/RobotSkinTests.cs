using TF2.Descent.Server.Horde;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// Robots are BLU, MvM's invaders: a lent body draws its model's BLU skin
/// (<see cref="RobotLooks.BodySkin"/>), its held weapon too, at every lend.
/// </summary>
public sealed class RobotSkinTests
{
    [Theory]
    [InlineData("scout")]
    [InlineData("soldier")]
    [InlineData("giant_soldier")]
    [InlineData("major_crits")]
    public void A_lent_body_draws_its_models_blu_skin(string template)
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent(template);

        Assert.Equal(RobotLooks.BluSkin, rig.Skins.Skin[body.Handle]);
        Assert.Equal(RobotLooks.BluSkin, body.Skin);
    }

    [Fact]
    public void A_lent_bodys_weapon_draws_blu()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("soldier");

        Assert.Equal(RobotLooks.BluSkin, rig.Skins.Skin[body.Weapon!.Handle]);
    }

    [Fact]
    public void The_tank_draws_its_first_skin_which_is_its_blu_one()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("tank");

        Assert.Equal(0, rig.Skins.Skin[body.Handle]);
    }

    [Fact]
    public void A_body_lent_again_draws_its_new_robots_skin()
    {
        var rig = new ServerHordeRig();
        var (tank, first) = rig.Lent("tank");
        Assert.Equal(0, rig.Skins.Skin[first.Handle]);
        Assert.True(rig.Controller.ReturnByHand(tank.Index, rig.Now, out _));

        var (_, again) = rig.Lent("soldier");

        Assert.Same(first, again);
        Assert.Equal(RobotLooks.BluSkin, rig.Skins.Skin[again.Handle]);
    }
}
