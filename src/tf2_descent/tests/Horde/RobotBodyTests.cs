using SourceSharp.Abi;
using SourceSharp.Ai;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using TF2.Descent.Shared;
using Xunit;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The robot body (plan_descent_logic HO2, 3.7): a lend puts it at the robot's
/// state with its template's model, its rolled scale and the hull scaled to
/// match; a return hands that state back and parks it.
/// </summary>
public sealed class RobotBodyTests
{
    [Fact]
    public void A_lent_body_stands_where_the_robot_stands_facing_its_way()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier", 1.25);

        Assert.Equal(rig.Roster.Position(id.Index), body.Position);
        Assert.Equal(rig.Roster.Yaw(id.Index), body.Yaw);
        Assert.Equal(HordeVectors.ToEngine(body.Position), rig.LastFor<Vector>("Entities.SetOrigin", body.Handle, 1));
        Assert.Equal(new Vector(0f, 90f, 0f), rig.LastFor<Vector>("Entities.SetAngles", body.Handle, 1));
    }

    [Fact]
    public void A_lent_body_wears_its_templates_model_at_its_rolled_scale()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("pyro", 0.9);

        Assert.Equal(RobotLooks.PyroModel, body.Model);
        Assert.Equal(RobotLooks.PyroModel, rig.LastFor<string>("EntityRender.SetModel", body.Handle, 1));
        Assert.Equal(0.9f, rig.LastFor<float>("EntityRender.SetModelScale", body.Handle, 1));
        Assert.Equal(0.9f, body.State.ModelScale);
    }

    [Fact]
    public void A_lent_bodys_hull_is_the_players_scaled_by_the_robot()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("giant_heavy", 1.75);

        var bounds = rig.Engine.AllOf("Entities.SetBounds").Last(c => c.ArgAs<EntityHandle>(0) == body.Handle);
        Assert.Equal(new Vector(-42f, -42f, 0f), bounds.ArgAs<Vector>(1));
        Assert.Equal(new Vector(42f, 42f, 143.5f), bounds.ArgAs<Vector>(2));
    }

    [Fact]
    public void A_giant_wears_the_giant_model_where_its_class_has_one_and_the_ordinary_one_where_not()
    {
        var rig = new ServerHordeRig();

        Assert.Equal(RobotLooks.GiantHeavyModel, rig.Lent("giant_heavy").Body.Model);
        Assert.Equal(RobotLooks.MedicModel, rig.Lent("giant_medic").Body.Model);
    }

    [Fact]
    public void A_lent_body_is_solid_and_drawn_with_its_weapon_in_hand()
    {
        var rig = new ServerHordeRig();
        var (_, body) = rig.Lent("demoman");

        Assert.Equal(SolidType.BoundingBox, rig.LastFor<SolidType>("Entities.SetSolidity", body.Handle, 1));
        Assert.True(rig.LastFor<bool>("EntityRender.SetVisible", body.Handle, 1));
        Assert.True(body.Weapon!.Shown);
        Assert.Equal(RobotLooks.GrenadeLauncher, body.Weapon.Model);
        Assert.Equal(body.Handle, rig.Engine.AllOf("Entities.SetParent").Last(c => c.ArgAs<EntityHandle>(0) == body.Weapon.Handle).ArgAs<EntityHandle>(1));
    }

    [Fact]
    public void A_lent_bodys_health_is_its_robots_life_rounded_up()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("scout", 0.95);
        int expected = (int)Math.Ceiling(rig.Roster.Life(id.Index));

        Assert.Equal(expected, body.EngineHealth);
        Assert.Equal(expected, rig.LastFor<int>("Entities.SetHealth", body.Handle, 1));
        Assert.Equal(255, body.State.Life);
    }

    [Fact]
    public void A_lent_bodys_net_state_names_it_lent_and_its_template_and_level()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("heavy");
        int row = HordeFixtures.TheRules.Content.Robots.Rows.ToList().FindIndex(t => t.Id == "heavy");

        Assert.Equal((byte)RobotFlags.Lent, body.State.Flags);
        Assert.Equal(row + 1, body.State.Template);
        Assert.Equal(rig.Roster.MonsterLevel(id.Index), body.State.Level);
    }

    [Fact]
    public void A_return_hands_back_the_rows_state_unchanged()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier", 1.1);
        var before = rig.Roster.BodyState(id.Index);

        var end = rig.Bodies.End(body.Lease, id);

        Assert.Equal(before, end);
    }

    [Fact]
    public void A_return_hands_back_where_the_body_was_moved_to()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        var to = HordeFixtures.Centre(1) + new AiVector(5, 6, 0);

        body.MoveTo(to, 45f);
        var end = rig.Bodies.End(body.Lease, id);

        Assert.Equal(to, end.Position);
        Assert.Equal(45f, end.Yaw);
        Assert.Equal(1, end.Room);
    }

    [Fact]
    public void A_returned_body_is_parked_hidden_and_not_solid()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");

        Assert.True(rig.Controller.ReturnByHand(id.Index, rig.Now, out _));

        Assert.True(body.Parked);
        Assert.False(body.TakesDamage);
        Assert.Equal(SolidType.None, rig.LastFor<SolidType>("Entities.SetSolidity", body.Handle, 1));
        Assert.False(rig.LastFor<bool>("EntityRender.SetVisible", body.Handle, 1));
        Assert.False(body.Weapon!.Shown);
        Assert.Equal(RobotState.Far, rig.Roster.State(id.Index));
    }

    [Fact]
    public void A_sync_puts_each_lent_bodys_place_on_its_roster_row()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        var to = HordeFixtures.Centre(2);

        body.MoveTo(to, 10f);
        rig.Bodies.Sync();

        Assert.Equal(to, rig.Roster.Position(id.Index));
        Assert.Equal(2, rig.Roster.Room(id.Index));
    }

    [Fact]
    public void The_body_net_state_is_small()
    {
        // The blob holds the engine's own block first; the body's fields follow it.
        var body = RobotNetState.NetLayout;
        var weapon = RobotWeaponNetState.NetLayout;
        Assert.Equal(12, body.Fields.Count);
        Assert.Equal(48, body.Fields.Sum(f => f.Bytes));   // a word a field
        Assert.Equal(516, body.Size);
        Assert.Equal(4, weapon.Fields.Sum(f => f.Bytes));
    }
}
