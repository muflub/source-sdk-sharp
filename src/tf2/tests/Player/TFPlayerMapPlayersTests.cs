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
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(hook), rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine,
                                            rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog,
                                            arsenal: new SourceSharp.Server.Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                                    rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                                    new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                                    new SourceSharp.Server.DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine, equipHooks: new ListHooks<ITFWeaponEquipHook>());
        return (rig, new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, rig.Items, new AttributeHooks()), hook);
    }

    /// <summary>The same player for the cloak facts, with the items table the watch's hooks ride.</summary>
    private static (PlayerRig Rig, TFPlayerMapPlayers Players, TFPlayerItems Items, TFPlayerConditions Conditions) MakeCloak(
        TFClass cls, TFPlayerItems? items = null)
    {
        var rig = new PlayerRig().As(cls);
        rig.Engine.Returns("Entities.GetHealth", 100);
        var conditions = new TFPlayerConditions(rig.Carriers, rig.Engine, new ListHooks<ITFConditionHook>(), rig.Engine);
        var resupply = new TFPlayerResupply(rig.Carriers, rig.Classes, rig.Engine, rig.Engine, conditions, rig.Loadout, rig.Engine,
                                            rig.Items, new AttributeHooks(),
                                            scripts: new TFWeaponScripts(rig.Log, TF2.Tests.Weapons.WeaponFixtures.ShippedWithClasses()),
                                            catalog: rig.Catalog,
                                            arsenal: new SourceSharp.Server.Arsenal(rig.Log, rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                                    rig.Engine, rig.Engine, rig.Engine, rig.Engine,
                                                                                    new ScriptedEntityRegistry(TestEngines.Server()), rig.Engine,
                                                                                    new SourceSharp.Server.DamageHitGroups()),
                                            hpMin: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainHpMinConVar>(null),
                                            deployCost: DeclaredConVars.Holding<TF2.Server.TFMaxHealthDrainDeployCostConVar>(null),
                                            clock: rig.Engine, equipHooks: new ListHooks<ITFWeaponEquipHook>());
        var table = items ?? rig.Items;
        return (rig, new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, resupply, conditions, rig.Carriers, table, new AttributeHooks()), table, conditions);
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
        Assert.Equal(0, rig.Engine.CountOf("Players.RemoveAllItems"));
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
        var (rig, players, _, _) = MakeCloak(TFClass.Spy);

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
        var (rig, players, _, _) = MakeCloak(TFClass.Soldier);

        Assert.False(players.AddToSpyCloakMeter(PlayerRig.Player, 100f));
        Assert.Equal(0f, rig.State.CloakMeter);
    }

    [Fact]
    public void A_watch_that_refuses_regen_from_items_takes_no_cloak()
    {
        // :9733-9736 — mod_cloak_no_regen_from_items on the watch.
        var items = new TFPlayerItems();
        items.SetManager(PlayerRig.Index, ManagerHook("mod_cloak_no_regen_from_items", AttributeDescriptionFormat.Additive, 1f));
        var (rig, players, _, _) = MakeCloak(TFClass.Spy, items);

        Assert.False(players.AddToSpyCloakMeter(PlayerRig.Player, 50f));
        Assert.Equal(0f, rig.State.CloakMeter);
    }

    [Fact]
    public void A_watch_that_refuses_cloak_while_cloaked_takes_none_when_stealthed()
    {
        // :9740-9746 — NoCloakWhenCloaked and InCond(STEALTHED).
        var items = new TFPlayerItems();
        items.SetManager(PlayerRig.Index, ManagerHook("NoCloakWhenCloaked", AttributeDescriptionFormat.Additive, 1f));
        var (rig, players, _, conditions) = MakeCloak(TFClass.Spy, items);
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
        var (rig, players, _, conditions) = MakeCloak(TFClass.Spy);
        conditions.Add(PlayerRig.Player, TFCond.Stealthed, 5f);

        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 50f));
        Assert.Equal(50f, rig.State.CloakMeter);
    }

    [Fact]
    public void ReducedCloakFromAmmo_shrinks_the_cloak_a_pack_offers()
    {
        // :9749 — CALL_ATTRIB_HOOK_FLOAT(pWpn, val, ReducedCloakFromAmmo):
        // a percentage of 0.5 leaves the pack's 40 arriving as 20.
        var items = new TFPlayerItems();
        items.SetManager(PlayerRig.Index, ManagerHook("ReducedCloakFromAmmo", AttributeDescriptionFormat.Percentage, 0.5f));
        var (rig, players, _, _) = MakeCloak(TFClass.Spy, items);

        Assert.True(players.AddToSpyCloakMeter(PlayerRig.Player, 40f));
        Assert.Equal(20f, rig.State.CloakMeter);
    }

    // ---- the demoman's charge from ammo (entity_ammopack.cpp:96-110) ----

    [Fact]
    public void The_persuaders_wearer_takes_charge_from_a_pack()
    {
        var items = new TFPlayerItems();
        items.SetManager(PlayerRig.Index, ManagerHook("ammo_gives_charge", AttributeDescriptionFormat.Additive, 1f));
        var (rig, players, _, _) = MakeCloak(TFClass.Demoman, items);
        rig.State.ChargeMeter = 30f;

        Assert.True(players.AddToDemoChargeMeterFromAmmo(PlayerRig.Player, 0.5f)); // :98-108 — the pack's half
        Assert.Equal(80f, rig.State.ChargeMeter);
    }

    [Fact]
    public void A_full_charge_meter_takes_no_charge_from_a_pack()
    {
        var items = new TFPlayerItems();
        items.SetManager(PlayerRig.Index, ManagerHook("ammo_gives_charge", AttributeDescriptionFormat.Additive, 1f));
        var (rig, players, _, _) = MakeCloak(TFClass.Demoman, items);
        rig.State.ChargeMeter = 100f;

        Assert.False(players.AddToDemoChargeMeterFromAmmo(PlayerRig.Player, 0.5f)); // :101's gate
        Assert.Equal(100f, rig.State.ChargeMeter);
    }

    [Fact]
    public void A_player_without_the_attribute_takes_no_charge()
    {
        var (rig, players, _, _) = MakeCloak(TFClass.Demoman);
        rig.State.ChargeMeter = 30f;

        Assert.False(players.AddToDemoChargeMeterFromAmmo(PlayerRig.Player, 0.5f));
        Assert.Equal(30f, rig.State.ChargeMeter);
    }

    // ---- the spy's melted knife (entity_ammopack.cpp:91-94) ----

    /// <summary>One spy over the real spy lane: the map-players arm answers
    /// through the spy's own slice, as the server registers it.</summary>
    private static (TF2.Tests.Spy.SpyRig Rig, TFPlayerMapPlayers Players) MakeKnife()
    {
        var rig = new TF2.Tests.Spy.SpyRig();
        var players = new TFPlayerMapPlayers(rig.Engine, rig.Engine, rig.Carriers, rig.Resupply, rig.Conditions,
                                             rig.Carriers, new TFPlayerItems(), new AttributeHooks(),
                                             new SpyServices(rig));
        return (rig, players);
    }

    /// <summary>The container the knife facts run under: only the spy's slice,
    /// which is what the map players looks up on the ask.</summary>
    private sealed class SpyServices(TF2.Tests.Spy.SpyRig rig) : IServiceLocator
    {
        public object? Resolve(Type serviceType) => serviceType == typeof(ITFSpyState) ? rig.Spies : null;
        public object? ResolveNamed(string name) => null;
        public bool IsRegistered(Type serviceType) => serviceType == typeof(ITFSpyState);
        public IReadOnlyCollection<Type> ServiceTypes => [typeof(ITFSpyState)];
        public IReadOnlyCollection<string> Names => [];
    }

    [Fact]
    public void APackShrinksTheMeltedKnifesWaitByItsShare()
    {
        // entity_ammopack.cpp:91-94 — AddToSpyKnife(100 * ratio, false): the half pack's
        // 50 pulls the melt stamp earlier by 50 * 0.005 of the window
        // (tf_weapon_knife.cpp:166-167), half a window's worth: ready at 107.5, not 110.
        var (rig, players) = MakeKnife();
        rig.Spies.MeltKnife(TF2.Tests.Spy.SpyRig.Spy.EntryIndex, now: rig.Clock.Now, seconds: 10f);

        Assert.False(rig.Spies.KnifeReady(TF2.Tests.Spy.SpyRig.Spy.EntryIndex, now: 107f));   // :523 — mid-window without a pack
        Assert.True(players.AddToSpyKnife(TF2.Tests.Spy.SpyRig.Spy, amount: 50f));             // :168 — the stamp moved
        Assert.True(rig.Spies.KnifeReady(TF2.Tests.Spy.SpyRig.Spy.EntryIndex, now: 107.6f));   // two and a half seconds earlier
        Assert.Equal(97.5f, rig.State.KnifeMeltTimestamp, 4);                                  // :167 — and the carrier mirror rides along (:523)
    }

    [Fact]
    public void AFullPackEndsTheRemainingWaitOfAMeltedKnife()
    {
        // :166-168 at the touch: half the 10 seconds are gone by 105.1, and the full pack's
        // 100 * 0.005 * the window takes the rest — the knife is ready on the press itself.
        var (rig, players) = MakeKnife();
        rig.Spies.MeltKnife(TF2.Tests.Spy.SpyRig.Spy.EntryIndex, now: rig.Clock.Now, seconds: 10f);
        rig.Clock.Now = 105.1f;

        Assert.False(rig.Spies.KnifeReady(TF2.Tests.Spy.SpyRig.Spy.EntryIndex, now: 105.1f)); // without the pack, still melting
        Assert.True(players.AddToSpyKnife(TF2.Tests.Spy.SpyRig.Spy, amount: 100f));
        Assert.True(rig.Spies.KnifeReady(TF2.Tests.Spy.SpyRig.Spy.EntryIndex, now: 105.1f));   // the pack ends it
        Assert.Equal(95f, rig.State.KnifeMeltTimestamp, 4);
    }

    [Fact]
    public void AKnifeThatExistsTakesNothingFromThePack()
    {
        // :163-164 — the exists gate: an unmelted knife refuses the stamp pull and
        // answers no, so the pack on its own is not taken (:94); the stamp never moves.
        var (rig, players) = MakeKnife();

        Assert.False(players.AddToSpyKnife(TF2.Tests.Spy.SpyRig.Spy, amount: 100f));
        Assert.Equal(0f, rig.State.KnifeMeltTimestamp, 4);                                     // the untouched initial (:77)
    }
}
