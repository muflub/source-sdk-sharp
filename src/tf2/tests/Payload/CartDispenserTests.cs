using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Entities;
using TF2.Shared;

using Xunit;

namespace TF2.Tests.Payload;

/// <summary>
/// mapobj_cart_dispenser's dispense pass (tf_obj_dispenser.cpp, pared to the
/// cart's): healing and resupply to the cart's team within the dispenser
/// radius, on the level-1 rates and the ammo clock.
/// </summary>
public sealed class CartDispenserTests
{
    private static (PayloadRig Rig, CartDispenser Dispenser) Dispenser()
    {
        var rig = new PayloadRig();
        var dispenser = rig.Spawn<CartDispenser>(CartDispenser.MapClass);
        return (rig, dispenser);
    }

    [Fact]
    public void The_dispenser_heals_its_teams_players_in_radius()
    {
        var (rig, dispenser) = Dispenser();
        var blue = rig.Players.Add(1, TFTeam.Blue);
        var red = rig.Players.Add(2, TFTeam.Red);
        rig.Engine.FillsSphere(blue, red);

        rig.ThinkAt(dispenser, 0f);

        // the level-1 heal rate for a tenth of a second, and never the enemy
        var heal = Assert.Single(rig.Resupply.Heals);
        Assert.Equal(blue, heal.Player);
        Assert.Equal(CartDispenser.HealRate * CartDispenser.DispenseThinkTime, heal.Amount, 3);
    }

    [Fact]
    public void The_dispenser_resupplies_on_its_ammo_clock()
    {
        var (rig, dispenser) = Dispenser();
        var blue = rig.Players.Add(1, TFTeam.Blue);
        rig.Engine.FillsSphere(blue);

        rig.ThinkAt(dispenser, 0f);

        // 20% of the class max for primary and secondary, flat metal
        Assert.Contains(rig.Resupply.Grants, g => g.Player == blue && g.Ammo == TFAmmo.Primary && g.Count == 20);
        Assert.Contains(rig.Resupply.Grants, g => g.Player == blue && g.Ammo == TFAmmo.Secondary && g.Count == 20);
        Assert.Contains(rig.Resupply.Grants, g => g.Player == blue && g.Ammo == TFAmmo.Metal && g.Count == CartDispenser.DropMetal);
        int afterFirst = rig.Resupply.Grants.Count;

        // inside the 1 s window nobody is served again
        rig.ThinkAt(dispenser, 0.5f);
        Assert.Equal(afterFirst, rig.Resupply.Grants.Count);

        // and then the clock opens
        rig.ThinkAt(dispenser, 1f);
        Assert.True(rig.Resupply.Grants.Count > afterFirst);
    }

    [Fact]
    public void The_quick_ammo_clock_arms_when_nobody_received()
    {
        var (rig, dispenser) = Dispenser();
        rig.Engine.FillsSphere(); // nobody near the cart

        rig.ThinkAt(dispenser, 0f);
        Assert.Empty(rig.Resupply.Grants);

        // 0.1 s later the cart serves a player who has just walked up
        var blue = rig.Players.Add(1, TFTeam.Blue);
        rig.Engine.FillsSphere(blue);
        rig.ThinkAt(dispenser, 0.1f);

        Assert.Contains(rig.Resupply.Grants, g => g.Player == blue && g.Ammo == TFAmmo.Metal);
    }

    [Fact]
    public void A_map_named_touch_trigger_serves_only_players_inside_the_brush()
    {
        var rig = new PayloadRig();
        var trigger = rig.Spawn<DispenserTouchTrigger>(DispenserTouchTrigger.MapClass, ("targetname", "disp_vol"));
        var dispenser = rig.Spawn<CartDispenser>(CartDispenser.MapClass, ("touch_trigger", "disp_vol"));
        var inside = rig.Players.Add(1, TFTeam.Blue);
        var outside = rig.Players.Add(2, TFTeam.Blue);
        var enemy = rig.Players.Add(3, TFTeam.Red);

        // only the first blue player has entered the brush
        rig.Registry.Touch(trigger.Handle, inside);
        dispenser.OnServerActivate();

        rig.ThinkAt(dispenser, 0f);

        // the toucher is served, the player outside the brush and the red
        // toucher are not, and the radius sphere was never asked
        var heal = Assert.Single(rig.Resupply.Heals);
        Assert.Equal(inside, heal.Player);
        Assert.DoesNotContain(rig.Resupply.Grants, g => g.Player == outside || g.Player == enemy);
        Assert.DoesNotContain(rig.Engine.Calls, c => c.Member == "WorldQuery.EntitiesInSphere");
    }

    [Fact]
    public void A_touch_trigger_named_but_not_created_leaves_the_radius_sphere()
    {
        var rig = new PayloadRig();
        var dispenser = rig.Spawn<CartDispenser>(CartDispenser.MapClass, ("touch_trigger", "missing_vol"));
        var blue = rig.Players.Add(1, TFTeam.Blue);
        rig.Engine.FillsSphere(blue);

        dispenser.OnServerActivate();
        rig.ThinkAt(dispenser, 0f);

        // the fall-through of stock's OnGoActive: no brush by that name, so
        // the sphere stands in
        Assert.Single(rig.Resupply.Heals);
    }
}
