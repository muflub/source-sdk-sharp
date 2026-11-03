using SourceSharp.Abi;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Rules;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The map items' seam (ITFMapPlayers) answered by the TF player: the class
/// health, the carrier's conditions with their arms, the full resupply.
/// </summary>
public sealed class TFPlayerMapPlayersTests
{
    private sealed class Hook : ITFConditionHook
    {
        public List<string> Heard { get; } = [];
        public void OnAdded(EntityHandle player, TFCond cond) => Heard.Add("+" + cond);
        public void OnRemoved(EntityHandle player, TFCond cond) => Heard.Add("-" + cond);
    }

    private static (PlayerRig Rig, TFPlayerMapPlayers Players, Hook Hook) Make(TFClass cls, int health)
    {
        var rig = new PlayerRig().As(cls);
        rig.Engine.Returns("Entities.GetHealth", health);
        var hook = new Hook();
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(hook));
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        return (rig, new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers), hook);
    }

    /// <summary>The same player for the cloak facts, with the owners seam the watch's hooks ride.</summary>
    private static (PlayerRig Rig, TFPlayerMapPlayers Players, TFPlayerConditions Conditions) MakeCloak(
        TFClass cls, Weapons.FakeOwners owners)
    {
        var rig = new PlayerRig().As(cls);
        rig.Engine.Returns("Entities.GetHealth", 100);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>());
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine);
        return (rig, new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, owners), conditions);
    }

    [Fact]
    public void Max_health_is_the_classs()
    {
        Assert.Equal(200, Make(TFClass.Soldier, 100).Players.MaxHealth(PlayerRig.Player));
    }

    [Fact]
    public void Take_health_adds_up_to_the_maximum_and_answers_what_it_added()
    {
        var (rig, players, _) = Make(TFClass.Soldier, 180);

        Assert.Equal(20, players.TakeHealth(PlayerRig.Player, 40f));
        Assert.Equal(200, rig.Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1));
    }

    [Fact]
    public void A_condition_goes_on_the_carrier_and_runs_its_arms()
    {
        var (rig, players, hook) = Make(TFClass.Pyro, 100);

        players.AddCond(PlayerRig.Player, TFCond.SpeedBoost, 5f);
        Assert.True(rig.State.InCond(TFCond.SpeedBoost));
        Assert.True(players.InCond(PlayerRig.Player, TFCond.SpeedBoost));

        players.RemoveCond(PlayerRig.Player, TFCond.SpeedBoost);
        Assert.False(rig.State.InCond(TFCond.SpeedBoost));
        Assert.Equal(["+SpeedBoost", "-SpeedBoost"], hook.Heard);
    }

    [Fact]
    public void A_dead_player_is_not_set_alight()
    {
        var (rig, players, _) = Make(TFClass.Pyro, 100);
        rig.Engine.Returns("Players.IsAlive", false);

        players.SelfBurn(PlayerRig.Player, 10f);

        Assert.False(rig.State.InCond(TFCond.Burning));
    }

    [Fact]
    public void The_lockers_regenerate_is_the_full_resupply()
    {
        var (rig, players, _) = Make(TFClass.Soldier, 20);
        players.SelfBurn(PlayerRig.Player, 10f);

        players.Regenerate(PlayerRig.Player);

        Assert.Equal(200, rig.Engine.AllOf("Entities.SetHealth").Last().ArgAs<int>(1));
        Assert.False(rig.State.InCond(TFCond.Burning));
        Assert.Equal(1, rig.Engine.CountOf("Players.RemoveAllItems"));
        Assert.Contains("tf_weapon_shotgun_soldier", rig.Engine.AllOf("Players.GiveItem").Select(c => c.ArgAs<string>(1)));
    }

    [Fact]
    public void Invulnerable_is_any_of_the_four()
    {
        var (_, players, _) = Make(TFClass.Medic, 100);
        Assert.False(players.IsInvulnerable(PlayerRig.Player));

        players.AddCond(PlayerRig.Player, TFCond.InvulnerableUserBuff, 5f);

        Assert.True(players.IsInvulnerable(PlayerRig.Player));
    }

    // ---- the spy's cloak meter (shared/tf/tf_player_shared.cpp:9725-9758) ----

    /// <summary>A hook of the player's own manager, in the seed-and-add shape
    /// the spy lane's disguise facts use: an int hook rides a seed of 0, so an
    /// additive 1 reads as set; the float hook scales multiplicatively.</summary>
    private static AttributeContainer ManagerHook(string hook, AttributeDescriptionFormat format, float value)
    {
        var def = new AttributeDefinition(30040 + hook.Length, hook, hook,
                                          format, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("cloak"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, value));
        return manager;
    }

    [Fact]
    public void The_spy_takes_cloak_and_a_moved_meter_is_the_answer()
    {
        var (rig, players, _) = MakeCloak(TFClass.Spy, new Weapons.FakeOwners());

        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 40f));   // :9753 — a positive amount under the cap
        Assert.Equal(40f, rig.State.CloakMeter);                          // :9755

        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 90f));
        Assert.Equal(100f, rig.State.CloakMeter);                         // :9755 — the clamp

        Assert.False(players.AddToSpyCloakMeter(PlayerRig.Player, 10f));  // :9753 — the full meter does not move
        Assert.Equal(100f, rig.State.CloakMeter);
    }

    [Fact]
    public void A_non_spy_takes_no_cloak()
    {
        // :9727-9729 — Weapon_OwnsThisID(TF_WEAPON_INVIS): the watch is the spy's own item.
        var (rig, players, _) = MakeCloak(TFClass.Soldier, new Weapons.FakeOwners());

        Assert.False(players.AddToSpyCloakMeter(PlayerRig.Player, 100f));
        Assert.Equal(0f, rig.State.CloakMeter);
    }

    [Fact]
    public void A_watch_that_refuses_regen_from_items_takes_no_cloak()
    {
        // :9733-9736 — mod_cloak_no_regen_from_items on the watch.
        var owners = new Weapons.FakeOwners();
        owners.Managers[PlayerRig.Index] = ManagerHook("mod_cloak_no_regen_from_items", AttributeDescriptionFormat.Additive, 1f);
        var (rig, players, _) = MakeCloak(TFClass.Spy, owners);

        Assert.False(players.AddToSpyCloakMeter(PlayerRig.Player, 50f));
        Assert.Equal(0f, rig.State.CloakMeter);
    }

    [Fact]
    public void A_watch_that_refuses_cloak_while_cloaked_takes_none_when_stealthed()
    {
        // :9740-9746 — NoCloakWhenCloaked and InCond(STEALTHED).
        var owners = new Weapons.FakeOwners();
        owners.Managers[PlayerRig.Index] = ManagerHook("NoCloakWhenCloaked", AttributeDescriptionFormat.Additive, 1f);
        var (rig, players, conditions) = MakeCloak(TFClass.Spy, owners);
        conditions.Add(PlayerRig.Player, TFCond.Stealthed, 5f);

        Assert.False(players.AddToSpyCloakMeter(PlayerRig.Player, 50f));
        Assert.Equal(0f, rig.State.CloakMeter);

        conditions.Remove(PlayerRig.Player, TFCond.Stealthed);
        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 50f));   // :9747-9750 — the else arm: not stealthed, cloak rides
        Assert.Equal(50f, rig.State.CloakMeter);
    }

    [Fact]
    public void A_stealthed_watch_without_the_refusal_still_takes_cloak()
    {
        // :9743 — the refusal needs BOTH arms; a plain watch cloaks on regardless.
        var (rig, players, conditions) = MakeCloak(TFClass.Spy, new Weapons.FakeOwners());
        conditions.Add(PlayerRig.Player, TFCond.Stealthed, 5f);

        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 50f));
        Assert.Equal(50f, rig.State.CloakMeter);
    }

    [Fact]
    public void ReducedCloakFromAmmo_shrinks_the_cloak_a_pack_offers()
    {
        // :9749 — CALL_ATTRIB_HOOK_FLOAT(pWpn, val, ReducedCloakFromAmmo):
        // a percentage of 0.5 leaves the pack's 40 arriving as 20.
        var owners = new Weapons.FakeOwners();
        owners.Managers[PlayerRig.Index] = ManagerHook("ReducedCloakFromAmmo", AttributeDescriptionFormat.Percentage, 0.5f);
        var (rig, players, _) = MakeCloak(TFClass.Spy, owners);

        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 40f));
        Assert.Equal(20f, rig.State.CloakMeter);
    }
}
