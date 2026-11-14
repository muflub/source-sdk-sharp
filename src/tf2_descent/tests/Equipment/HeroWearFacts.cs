using SourceSharp.Tests;
using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Descent.Rules.Inventory;
using TF2.Descent.Rules.Items;
using TF2.Descent.Server.Gear;
using TF2.Descent.Server.Items;
using TF2.Descent.Tests.Hero;
using TF2.Server.Econ;
using TF2.Shared;
using TF2.Shared.Econ;

using Xunit;

namespace TF2.Descent.Tests.Gear;

/// <summary>
/// Wear in play (HE9): a fired weapon and a struck armour piece wear by the rules, batched so
/// a ledger write is a point lost (not a shot), the rest written at a level's end, a
/// disconnect or a take-off, no wear lost; a launcher worn to 0 is handed to TF2 as its
/// Stock self, so its damage is the Stock number, and a repair gives its attribute back.
/// </summary>
public sealed class HeroWearFacts
{
    private sealed class Rig
    {
        public readonly EquipmentRig Gear = new();
        public readonly HeroWear Wear;
        public readonly HeroWearFlush Flush;

        public Rig()
        {
            Wear = new HeroWear(Gear.Ledger, Gear.Keys, Gear.Rules, Gear.Schema, Gear.Log);
            Flush = new HeroWearFlush(Wear);
            Gear.LedgerHooks.Add(Flush);
        }

        public Held Primary => Gear.Held.Inventory.Equipped["primary"];

        public ItemDefinition Launcher => Gear.Equipment.TFItemOf(Gear.Equipment.Of(EquipmentRig.Index)!, "primary")!.Definition;

        /// <summary><paramref name="shots"/> of the launcher, the frame's end after each (a write only when one is due).</summary>
        public void Fire(int shots)
        {
            var launcher = Launcher;
            for (int i = 0; i < shots; i++)
            {
                Wear.Fired(EquipmentRig.Index, launcher);
                ((IFrameLifecycle)Flush).OnPostThink(0.015f);
            }
        }

        public float Damage() =>
            new AttributeHooks().Float(100f, "mult_dmg", TFLoadoutProvider.Attributes(Gear.Answer(TFLoadoutSlot.Primary).Item!));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_shot_is_pending_wear_and_writes_nothing_until_it_costs_a_point()
    {
        var rig = new Rig();

        rig.Fire(3);

        Assert.Equal((3, 0), rig.Wear.Pending(EquipmentRig.Key, rig.Primary.Id));
        Assert.Equal(0, rig.Wear.Writes);
        Assert.Equal(60, rig.Primary.Item.Durability);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_shot_that_costs_a_point_writes_it_through_the_ledger()
    {
        var rig = new Rig();

        rig.Fire(4);

        Assert.Equal(59, rig.Primary.Item.Durability);
        Assert.Equal(1, rig.Wear.Writes);
        Assert.Equal((0, 0), rig.Wear.Pending(EquipmentRig.Key, rig.Primary.Id));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hundred_shots_are_twenty_five_writes_one_a_point_lost()
    {
        var rig = new Rig();

        rig.Fire(100);

        Assert.Equal(35, rig.Primary.Item.Durability);
        Assert.Equal(25, rig.Wear.Writes);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_levels_end_writes_the_progress_below_a_point_and_no_wear_is_lost_across_the_change()
    {
        var rig = new Rig();
        rig.Fire(3);

        ((IGameLifecycle)rig.Flush).OnLevelShutdown();
        int afterLevel = rig.Primary.Item.WearUses;
        rig.Fire(1);

        Assert.Equal(300, afterLevel);
        Assert.Equal(59, rig.Primary.Item.Durability);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_disconnect_writes_the_heroes_pending_wear()
    {
        var rig = new Rig();
        rig.Fire(2);

        ((IClientLifecycle)rig.Flush).OnClientDisconnected(EquipmentRig.Index);

        Assert.Equal(200, rig.Primary.Item.WearUses);
        Assert.Equal((0, 0), rig.Wear.Pending(EquipmentRig.Key, rig.Primary.Id));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Taking_the_weapon_off_writes_its_pending_wear_to_it_in_the_backpack()
    {
        var rig = new Rig();
        long id = rig.Primary.Id;
        rig.Fire(3);

        Assert.True(rig.Gear.Unequip("primary").Ok);

        Assert.Equal(300, rig.Gear.Held.Inventory.Backpack.Values.Single(h => h.Id == id).Item.WearUses);
        Assert.False(rig.Wear.HasPending(EquipmentRig.Key, id));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Hits_taken_wear_the_armour_worn_and_not_the_weapons()
    {
        var rig = new Rig();
        int from = rig.Gear.Give(EquipmentRig.Item("cap") with { Durability = 40, MaxDurability = 40 });
        Assert.True(rig.Gear.Equip(from, "hat").Ok);

        for (int i = 0; i < 3; i++)
        {
            rig.Wear.Hit(EquipmentRig.Index);
            ((IFrameLifecycle)rig.Flush).OnPostThink(0.015f);
        }

        Assert.Equal(39, rig.Gear.Held.Inventory.Equipped["hat"].Item.Durability);
        Assert.Equal(60, rig.Primary.Item.Durability);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_shot_of_a_weapon_the_hero_does_not_wear_costs_nothing()
    {
        var rig = new Rig();

        rig.Wear.Fired(EquipmentRig.Index, null);
        rig.Wear.Fired(EquipmentRig.Index, rig.Gear.Schema.Item("The Direct Hit"));

        Assert.False(rig.Wear.HasPending(EquipmentRig.Key, rig.Primary.Id));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_launcher_worn_to_zero_by_its_shots_does_the_stock_damage_and_a_repair_restores_its_bonus()
    {
        var rig = new Rig();
        int from = rig.Gear.Give(EquipmentRig.Item("rocket_launcher", new RolledAffix("devastating", 0)));
        Assert.True(rig.Gear.Equip(from, "primary").Ok);
        float rolled = rig.Damage();

        rig.Fire(60 * 4);
        var state = rig.Gear.Equipment.Of(EquipmentRig.Index)!.Result.State("primary");
        float broken = rig.Damage();
        int rebuilt = rig.Gear.Refresh.Rebuilt;
        Assert.True((await rig.Gear.Ledger.RepairWorn(EquipmentRig.Key, "primary", WornRepair.TownUncharged)).Ok);
        float repaired = rig.Damage();

        Assert.Equal(120f, rolled, 0.01f);
        Assert.Equal((InactiveReason.Broken, true), (state.Reason, state.FiresAsStock));
        Assert.Equal(100f, broken, 0.01f);
        Assert.Equal(120f, repaired, 0.01f);
        Assert.Equal(rebuilt + 1, rig.Gear.Refresh.Rebuilt);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public async Task A_broken_weapon_gathers_no_more_wear()
    {
        var rig = new Rig();
        Assert.True((await rig.Gear.Ledger.SetDurability(EquipmentRig.Key, "primary", 0, 60)).Ok);

        rig.Fire(8);

        Assert.False(rig.Wear.HasPending(EquipmentRig.Key, rig.Primary.Id));
        Assert.Equal(0, rig.Wear.Writes);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Broken_armour_contributes_nothing()
    {
        var rig = new Rig();
        int from = rig.Gear.Give(EquipmentRig.Item("cap") with { Durability = 1, MaxDurability = 40 });
        Assert.True(rig.Gear.Equip(from, "hat").Ok);
        double before = rig.Gear.HeroComponent.Stats!.Armour;

        for (int i = 0; i < 3; i++)
        {
            rig.Wear.Hit(EquipmentRig.Index);
            ((IFrameLifecycle)rig.Flush).OnPostThink(0.015f);
        }

        Assert.Equal(0, rig.Gear.Held.Inventory.Equipped["hat"].Item.Durability);
        Assert.Equal(before - 6, rig.Gear.HeroComponent.Stats!.Armour, 6);
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void An_item_field_repaired_to_a_maximum_of_zero_cannot_be_equipped_and_stays_in_the_backpack()
    {
        var rig = new Rig();
        int from = rig.Gear.Give(EquipmentRig.Item("rocket_launcher") with { Durability = 0, MaxDurability = 0 });

        var r = rig.Gear.Equip(from, "primary");

        Assert.Equal(TF2.Descent.Rules.Inventory.InventoryRefusals.Unusable, r.Refusal);
        Assert.Equal("rocket_launcher", rig.Gear.Held.Inventory.Backpack[from].Item.BaseId);
    }
    //-------------------------------------------------------------------------
    // The taps
    //-------------------------------------------------------------------------

    private sealed class Mode(bool active) : TF2.Descent.Server.Mode.IDescentModeState
    {
        public bool Active => active;
    }

    private static TFWeaponHookContext Shot(ItemDefinition weapon) =>
        default(TFWeaponHookContext) with { OwnerIndex = EquipmentRig.Index, Item = new AttributeContainer(weapon) };

    private static SourceSharp.Shared.WeaponAttackContext Live(bool live) => default(SourceSharp.Shared.WeaponAttackContext) with { EffectsLive = live };

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void The_weapon_chains_after_fire_of_a_live_shot_is_a_use_and_a_replayed_one_is_not()
    {
        var rig = new Rig();
        var taps = new HeroWearTaps(rig.Wear, new Mode(true), rig.Gear.Hero.Engine);

        taps.AfterFire(Shot(rig.Launcher), Live(false));
        var replayed = rig.Wear.Pending(EquipmentRig.Key, rig.Primary.Id);
        taps.AfterFire(Shot(rig.Launcher), Live(true));

        Assert.Equal((0, 0), replayed);
        Assert.Equal((1, 0), rig.Wear.Pending(EquipmentRig.Key, rig.Primary.Id));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void Outside_the_descent_mode_a_shot_wears_nothing()
    {
        var rig = new Rig();
        var taps = new HeroWearTaps(rig.Wear, new Mode(false), rig.Gear.Hero.Engine);

        taps.AfterFire(Shot(rig.Launcher), Live(true));

        Assert.False(rig.Wear.HasPending(EquipmentRig.Key, rig.Primary.Id));
    }

    [NotOnCiFact("reads the shipped TF2 schema: the Steam install under bin/<config>/content, which a runner has not")]
    public void A_hit_from_another_wears_armour_and_the_heroes_own_blast_does_not()
    {
        var rig = new Rig();
        int from = rig.Gear.Give(EquipmentRig.Item("cap") with { Durability = 40, MaxDurability = 40 });
        Assert.True(rig.Gear.Equip(from, "hat").Ok);
        long cap = rig.Gear.Held.Inventory.Equipped["hat"].Id;
        var taps = new HeroWearTaps(rig.Wear, new Mode(true), rig.Gear.Hero.Engine);
        var hero = EquipmentRig.Player;

        taps.OnPlayerHurt(new PlayerHurt(hero, hero, 20, 80));
        var own = rig.Wear.Pending(EquipmentRig.Key, cap);
        taps.OnPlayerHurt(new PlayerHurt(hero, HeroRig.PlayerOf(9), 20, 60));

        Assert.Equal((0, 0), own);
        Assert.Equal((0, 1), rig.Wear.Pending(EquipmentRig.Key, cap));
    }
}
