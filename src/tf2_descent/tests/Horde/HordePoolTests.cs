using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Ai;
using SourceSharp.Shared;
using TF2.Descent.Ai.Bodies;
using TF2.Descent.Ai.Roster;
using TF2.Descent.Server.Horde;
using Xunit;
using static TF2.Descent.Tests.Horde.HordeFixtures;

namespace TF2.Descent.Tests.Horde;

/// <summary>
/// The controller and the pool (plan_descent_logic HO2): the controller takes
/// no edict; the pool makes 49 bodies and 49 weapons once, at level ready, and
/// none after however often they are lent; a parked body and its weapon are
/// never sent.
/// </summary>
public sealed class HordePoolTests
{
    [Fact]
    public void The_controller_is_a_logical_entity_and_takes_no_edict()
    {
        var attr = typeof(HordeController).GetCustomAttribute<ScriptedEntityAttribute>()!;

        Assert.Equal("tf2_descent_horde", attr.ClassName);
        Assert.Equal(ScriptedEntityKind.Logical, attr.Kind);
    }

    [Fact]
    public void The_pool_makes_49_bodies_and_49_weapons_at_level_ready()
    {
        var rig = new ServerHordeRig();

        Assert.Equal(49, rig.Spawner.Bodies);
        Assert.Equal(49, rig.Spawner.Weapons);
        Assert.Equal(49, rig.Controller.BodiesMade);
        Assert.Equal(49, rig.Bodies.LastBody);
        Assert.Equal(48, rig.Controller.Pool!.PoolBodies);
    }

    [Fact]
    public void Every_body_starts_parked_with_its_weapon_and_both_are_never_sent()
    {
        var rig = new ServerHordeRig();
        var transmit = new HordeTransmit(rig.Parking);

        for (int b = 1; b <= rig.Bodies.LastBody; b++)
        {
            var body = rig.Bodies.Body(b);
            Assert.True(body.Parked);
            Assert.Equal(TransmitAnswer.Never, transmit.ShouldTransmit(body.Handle, 1));
            Assert.Equal(TransmitAnswer.Never, transmit.ShouldTransmit(body.Weapon!.Handle, 1));
        }

        Assert.Equal(98, rig.Parking.Count);
    }

    [Fact]
    public void A_thousand_lends_and_returns_make_no_entity()
    {
        var rig = new ServerHordeRig();
        var (id, _) = rig.Lent("soldier");
        int made = rig.Spawner.Bodies + rig.Spawner.Weapons + rig.Spawner.Controllers;
        Assert.Equal(1 + 98, made);

        for (int k = 0; k < 1000; k++)
        {
            Assert.True(rig.Controller.ReturnByHand(id.Index, rig.Now, out string why), why);
            Assert.True(rig.Controller.LendByHand(id.Index, rig.Now, out why), why);
        }

        Assert.Equal(made, rig.Spawner.Bodies + rig.Spawner.Weapons + rig.Spawner.Controllers);
        Assert.Equal(1001, rig.Controller.HandLends);
        Assert.Equal(1000, rig.Controller.HandReturns);
    }

    [Fact]
    public void A_lent_body_and_its_weapon_are_left_to_the_ordinary_check()
    {
        var rig = new ServerHordeRig();
        var transmit = new HordeTransmit(rig.Parking);
        var (_, body) = rig.Lent("soldier");

        Assert.Equal(TransmitAnswer.Default, transmit.ShouldTransmit(body.Handle, 1));
        Assert.Equal(TransmitAnswer.Default, transmit.ShouldTransmit(body.Weapon!.Handle, 1));
        Assert.Equal(96, rig.Parking.Count);
    }

    [Fact]
    public void A_returned_body_and_its_weapon_are_never_sent_again()
    {
        var rig = new ServerHordeRig();
        var transmit = new HordeTransmit(rig.Parking);
        var (id, body) = rig.Lent("soldier");

        Assert.True(rig.Controller.ReturnByHand(id.Index, rig.Now, out _));

        Assert.Equal(TransmitAnswer.Never, transmit.ShouldTransmit(body.Handle, 1));
        Assert.Equal(TransmitAnswer.Never, transmit.ShouldTransmit(body.Weapon!.Handle, 1));
    }

    [Fact]
    public void Only_the_parked_handle_itself_is_refused_not_a_later_entity_in_its_slot()
    {
        var parking = new HordeParking();
        var parked = new EntityHandle((1u << 16) | 40u);
        var reused = new EntityHandle((2u << 16) | 40u);
        parking.Park(parked);

        Assert.True(parking.IsParked(parked));
        Assert.False(parking.IsParked(reused));
    }

    [Fact]
    public void A_body_already_out_is_never_put_out_again()
    {
        var rig = new ServerHordeRig();
        var (id, body) = rig.Lent("soldier");
        var other = rig.Roster.Add("pyro", 1.0, 0, new TF2.Descent.Ai.Level.PackMarker(Centre(0), 0), 9);

        Assert.Throws<InvalidOperationException>(() =>
            rig.Bodies.Begin(new BodyLease(body.Index, body.Lease.Serial + 1), other, rig.Roster.BodyState(other.Index)));
        Assert.Equal(id, body.Robot);
    }

    [Fact]
    public void A_level_whose_engine_refuses_a_body_has_no_horde()
    {
        var rig = new ServerHordeRig(begin: false);
        rig.Spawner.RefuseBodyAfter = 10;
        var controller = rig.Spawner.Controller()!;

        Assert.False(controller.Begin(rig.Level, ServerHordeRig.Tps, 1, rig.Now));
        Assert.False(controller.Begun);
        Assert.Equal(10, rig.Spawner.Bodies);
    }
}
