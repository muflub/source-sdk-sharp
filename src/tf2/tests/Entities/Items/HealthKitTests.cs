using SourceSharp.Abi;
using SourceSharp.Tests;
using TF2.Server.Combat;
using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;
using TF2.Tests.Medigun;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Entities.Items;

/// <summary>
/// item_healthkit_* (stage 2.4): <c>CHealthKit::MyTouch</c>
/// (entity_healthkit.cpp). A kit heals <c>ceil( GetMaxHealth() *
/// PackRatios[size] )</c> through <c>TakeHealth</c>, PackRatios being 0.2,
/// 0.5 and 1.0 (tf_powerup.cpp), and comes back after ITEM_RESPAWN_TIME, 10 s
/// (tf_gamerules.cpp:155).
/// </summary>
public sealed class HealthKitTests
{
    private static (MapItemRig Rig, HealthKit Kit) Spawn(string className, params (string, string)[] keys)
    {
        var rig = new MapItemRig();
        return (rig, rig.Spawn<HealthKit>(className, keys));
    }

    [Theory]
    [InlineData(HealthKitSmall.SmallClass, 40)]     // ceil(200 * 0.2)
    [InlineData(HealthKitMedium.MediumClass, 100)]  // ceil(200 * 0.5)
    [InlineData(HealthKit.MapClass, 150)]           // ceil(200 * 1.0), capped at the maximum
    public void A_kit_heals_its_share_of_the_class_maximum(string className, int healed)
    {
        var (rig, kit) = Spawn(className);
        var (player, p) = rig.Players.Add(playerClass: TFClass.Soldier);
        p.MaxHealth = 200;
        p.Health = 50;

        rig.Touch(kit, player);

        Assert.Equal(50 + healed, p.Health);
        // :104-105 — a map kit has no owner, so the event names no healer.
        Assert.Contains($"player_healed {healed} by (invalid handle)", rig.Notices.Said);
    }

    [Fact]
    public void The_share_rounds_up_as_stock_ceils_it()
    {
        // A scout's 125: medium = ceil(62.5) = 63, not 62.
        var (rig, kit) = Spawn(HealthKitMedium.MediumClass);
        var (player, p) = rig.Players.Add(playerClass: TFClass.Scout);
        p.MaxHealth = 125;
        p.Health = 1;

        rig.Touch(kit, player);

        Assert.Equal(64, p.Health);
    }

    [Fact]
    public void A_kit_extinguishes_a_burning_player_at_full_health()
    {
        var (rig, kit) = Spawn(HealthKitSmall.SmallClass);
        var (player, p) = rig.Players.Add();
        p.Health = p.MaxHealth;
        p.Conditions.Add(TFCond.Burning);

        rig.Touch(kit, player);

        Assert.DoesNotContain(TFCond.Burning, p.Conditions);
        Assert.True(kit.IsRespawning);
        Assert.Contains("ItemPickup item_healthkit_small", rig.Notices.Said);
        Assert.DoesNotContain(rig.Notices.Said, s => s.StartsWith("player_healed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_kit_also_stops_bleeding_and_cures_plague()
    {
        var (rig, kit) = Spawn(HealthKit.MapClass);
        var (player, p) = rig.Players.Add();
        p.Health = 10;
        p.Conditions.Add(TFCond.Bleeding);
        p.Conditions.Add(TFCond.Plague);

        rig.Touch(kit, player);

        Assert.Empty(p.Conditions);
    }

    [Fact]
    public void A_healthy_player_who_is_not_burning_leaves_the_kit()
    {
        var (rig, kit) = Spawn(HealthKit.MapClass);
        var (player, p) = rig.Players.Add();
        p.Health = p.MaxHealth;

        rig.Touch(kit, player);

        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_taken_kit_respawns_after_ten_seconds()
    {
        var (rig, kit) = Spawn(HealthKitSmall.SmallClass);
        var (player, p) = rig.Players.Add();
        p.Health = 1;

        Assert.True(kit.NextThinkDelay() < 0f); // nothing scheduled before the touch
        rig.Touch(kit, player);
        Assert.Equal(TFPowerup.ItemRespawnTime, kit.NextThinkDelay());
        Assert.Equal(10f, TFPowerup.ItemRespawnTime);

        // Taken: a second touch before the think gives nothing.
        int after = p.Health;
        rig.Touch(kit, player);
        Assert.Equal(after, p.Health);

        rig.Registry.Think(kit.Handle);
        Assert.False(kit.IsRespawning);
        rig.Touch(kit, player);
        Assert.True(p.Health > after);
    }

    [Fact]
    public void A_disabled_kit_heals_nobody_until_enabled()
    {
        var (rig, kit) = Spawn(HealthKit.MapClass, ("StartDisabled", "1"));
        var (player, p) = rig.Players.Add();
        p.Health = 1;

        rig.Touch(kit, player);
        Assert.Equal(1, p.Health);

        rig.Input(kit, "Enable");
        rig.Touch(kit, player);
        Assert.Equal(p.MaxHealth, p.Health);
    }

    [Fact]
    public void A_team_kit_refuses_the_other_team()
    {
        var (rig, kit) = Spawn(HealthKit.MapClass, ("TeamNum", ((int)TFTeam.Blue).ToString()));
        var (red, r) = rig.Players.Add(TFTeam.Red);
        var (blue, b) = rig.Players.Add(TFTeam.Blue);
        r.Health = 1;
        b.Health = 1;

        rig.Touch(kit, red);
        Assert.Equal(1, r.Health);

        rig.Touch(kit, blue);
        Assert.Equal(b.MaxHealth, b.Health);
    }

    [Fact]
    public void Mult_health_frompacks_scales_the_kits_heal()
    {
        // entity_healthkit.cpp:95 — CALL_ATTRIB_HOOK_FLOAT_ON_OTHER(pPlayer,
        // flHealth, mult_health_frompacks): a percentage of 0.5 on the
        // player's own manager halves the small kit's ceil(200 * 0.2) = 40 to 20.
        var rig = new MapItemRig();
        var owners = new FakeOwners();
        var def = new AttributeDefinition(30050, "mult health from packs", "mult_health_frompacks",
                                          AttributeDescriptionFormat.Percentage, AttributeEffectType.Neutral,
                                          false, false, AttributeValueType.Default, MergedKeys.Block("packs"));
        var manager = new AttributeContainer(null, AttributeProviderType.Generic);
        manager.Set(AttributeValue.OfFloat(def, 0.5f));

        var (player, p) = rig.Players.Add();
        owners.Managers[player.EntryIndex] = manager;
        rig.Services.AddInstance<ITFWeaponOwners>(owners);
        var kit = rig.Spawn<HealthKit>(HealthKitSmall.SmallClass);
        p.MaxHealth = 200;
        p.Health = 50;

        rig.Touch(kit, player);

        Assert.Equal(70, p.Health); // 50 + 20 — without the hook it would be 50 + 40
    }

    [Fact]
    public void A_dead_player_takes_nothing()
    {
        var (rig, kit) = Spawn(HealthKit.MapClass);
        var (player, p) = rig.Players.Add();
        p.Health = 1;
        p.Alive = false;

        rig.Touch(kit, player);

        Assert.Equal(1, p.Health);
    }

    /// <summary>
    /// A rig whose single player is the class given and (when told) owns a
    /// lunchbox — stock's <c>bIsAnyHeavyWithSandvichEquippedPickingUp</c>
    /// (entity_healthkit.cpp:75) is the weapon behind every arm here.
    /// </summary>
    private static (MapItemRig Rig, FakeOwners Owners, EntityHandle Player, FakeMapPlayer P)
        LunchboxRig(TFClass cls, bool lunchbox)
    {
        var rig = new MapItemRig();
        var owners = new FakeOwners();
        rig.Services.AddInstance<ITFWeaponOwners>(owners);
        var (player, p) = rig.Players.Add(playerClass: cls);
        if (lunchbox)
            owners.Items[player.EntryIndex] = new AttributeContainer(null, AttributeProviderType.Generic);
        return (rig, owners, player, p);
    }

    [Fact]
    public void The_sandvichs_owner_takes_only_the_ammo_half()
    {
        // :79-88 — "In the case of sandvich's owner, only restore ammo": the
        // wounded owner with a lunchbox takes the kit's one grenade, is not
        // healed at all, and the pickup announces as the tail's (:199-214).
        // The item charge refill (:86) is deferred (deferred.tsv).
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        p.Health = 10; // the owner arm runs BEFORE any healing: 10 stays 10
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(player);

        rig.Touch(kit, player);

        Assert.Equal(10, p.Health);
        Assert.Equal(1, p.Ammo[TFAmmo.Grenades1]);
        Assert.True(kit.IsRespawning);
        Assert.DoesNotContain(rig.Notices.Said, s => s.StartsWith("player_healed", StringComparison.Ordinal));
        Assert.Contains("ItemPickup item_healthkit_full", rig.Notices.Said);
        Assert.Contains("item_pickup medkit_large", rig.Notices.Said);
    }

    [Fact]
    public void The_sandvichs_owner_leaves_the_kit_when_the_grenade_is_full()
    {
        // :82 — GiveAmmo answers 0 with Grenades1 at the maximum, and the
        // whole owner arm fails: no pickup, nothing said, kit untouched.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        p.Health = 10;
        p.MaxAmmo[TFAmmo.Grenades1] = 1;
        p.Ammo[TFAmmo.Grenades1] = 1;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(player);

        rig.Touch(kit, player);

        Assert.Equal(10, p.Health);
        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_healthy_heavy_with_a_lunchbox_takes_an_unowned_kit_as_a_grenade()
    {
        // :182-196 — not needed and no owner: the lunchbox arm takes the kit
        // as one grenade. Stock emits no touch sound on this arm (:140-141
        // is the needed arm's), only the tail's announcements.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(1, p.Ammo[TFAmmo.Grenades1]);
        Assert.True(kit.IsRespawning);
        Assert.DoesNotContain("sound HealthKit.Touch", rig.Notices.Said);
        Assert.Contains("ItemPickup item_healthkit_full", rig.Notices.Said);
    }

    [Fact]
    public void A_healthy_heavy_with_a_lunchbox_leaves_the_kit_when_the_grenade_is_full()
    {
        // :186 — the unowned arm is stock's own GiveAmmo test: nothing taken
        // at the maximum, so no pickup at all.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Grenades1] = 1;
        p.Ammo[TFAmmo.Grenades1] = 1;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_thrown_kit_takes_only_nothing_from_a_healthy_heavy()
    {
        // :182 — a lunchbox arm is for map kits only: a thrown single
        // instance (owned by its thrower, as the lunchbox throw leaves it)
        // cannot be taken by another heavy for its grenade.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        var (thrower, _) = rig.Players.Add(playerClass: TFClass.HeavyWeapons);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(thrower);
        kit.SetThrownSingleInstance();

        rig.Touch(kit, player);

        Assert.False(p.Ammo.ContainsKey(TFAmmo.Grenades1));
        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_healthy_heavy_without_a_lunchbox_leaves_the_kit()
    {
        // :75 — the arm's weapon half: no lunchbox, no grenade, no pickup.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: false);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.False(p.Ammo.ContainsKey(TFAmmo.Grenades1));
        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_scout_with_a_borrowed_lunchbox_leaves_the_kit()
    {
        // :75's class half — IsPlayerClass(TF_CLASS_HEAVYWEAPONS): owning the
        // lunchbox is not enough for a scout.
        var (rig, _, player, p) = LunchboxRig(TFClass.Scout, lunchbox: true);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.False(p.Ammo.ContainsKey(TFAmmo.Grenades1));
        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void The_heal_names_the_kits_owner_as_the_healer()
    {
        // :104-105 — a kit owned by a player names that player as the
        // event's healer; the fake prints the handle, and the engine seam
        // resolves it to a userid (PlayerStealsSandvich's arm does the same
        // ToTFPlayer guard through the player table).
        var rig = new MapItemRig();
        var (picker, p) = rig.Players.Add();
        p.Health = 50;
        var (owner, _) = rig.Players.Add();
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(owner);

        rig.Touch(kit, picker);

        Assert.Equal(p.MaxHealth, p.Health);
        Assert.Contains($"player_healed 150 by {owner}", rig.Notices.Said);
    }

    [Fact]
    public void The_owners_sandvich_kit_fills_only_the_grenades()
    {
        // :246 — CHealthAmmoKit::MyTouch filters out the heavy picking up
        // their own sandvich-kit: no primary, secondary or metal, only the
        // health half's owner-arm grenade. Without the guard the small
        // ratio would also add ceil(30 * 0.2) = 6 primary.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Primary] = 30;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthAmmoKit>(HealthAmmoKit.MapClass);
        kit.SetOwner(player);

        rig.Touch(kit, player);

        Assert.Equal(1, p.Ammo[TFAmmo.Grenades1]);
        Assert.False(p.Ammo.ContainsKey(TFAmmo.Primary));
        Assert.True(kit.IsRespawning);
    }

    // The gamestats calls — Event_PlayerHealthkitPickup (entity_healthkit.cpp:180)
    // sits in the needed arm only: the two lunchbox arms' grenades are not
    // healthkit pickups, and a refused kit counts nothing.

    [Fact]
    public void A_taken_kit_counts_the_healthkit_stat()
    {
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var (player, p) = rig.Players.Add();
        p.Health = 50;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(p.MaxHealth, p.Health);
        Assert.Equal([$"Healthkits {player.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void A_refused_kit_counts_no_stat()
    {
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var (player, p) = rig.Players.Add();
        p.Health = p.MaxHealth; // full and not burning: the needed arm never runs
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.False(kit.IsRespawning);
        Assert.Empty(stats.Heals);
    }

    [Fact]
    public void The_sandvich_owners_grenade_is_no_healthkit_pickup()
    {
        // :180 lives in the needed arm; the :79-88 owner arm announces the
        // pickup but never counts one.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        p.Health = 10;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(player);

        rig.Touch(kit, player);

        Assert.Equal(1, p.Ammo[TFAmmo.Grenades1]);
        Assert.True(kit.IsRespawning);
        Assert.Empty(stats.Heals);
    }

    [Fact]
    public void The_healthy_heavys_grenade_is_no_healthkit_pickup()
    {
        // :182-196 — the unowned lunchbox arm, likewise outside :180.
        var (rig, _, player, p) = LunchboxRig(TFClass.HeavyWeapons, lunchbox: true);
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        p.Health = p.MaxHealth;
        p.MaxAmmo[TFAmmo.Grenades1] = 10;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(1, p.Ammo[TFAmmo.Grenades1]);
        Assert.True(kit.IsRespawning);
        Assert.Empty(stats.Heals);
    }

    // The owner heal-assist — entity_healthkit.cpp:143-168: a teammate owner
    // of the kit takes Event_PlayerAwardBonusPoints(owner, picker, 10) and
    // Event_PlayerHealedOtherAssist(owner, nHealthGiven) when the picker is
    // not hurting themselves (stock's GetLastEntityDamaged arm, answered here
    // by the last-hit seam).

    [Fact]
    public void A_teammate_owner_is_credited_with_the_heal_assist()
    {
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var (picker, p) = rig.Players.Add();
        p.Health = 50;
        var (owner, _) = rig.Players.Add();
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(owner);

        rig.Touch(kit, picker);

        // 150 = the full kit's ceil(200 * 1.0) capped at the missing health,
        // counted as the assist; the kit's own pickup counts after (:180).
        Assert.Equal([$"BonusPoints {owner.EntryIndex} 10", $"HealingAssist {owner.EntryIndex} 150",
                      $"Healthkits {picker.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void The_self_healer_owner_is_no_creditor()
    {
        // :144 — pOwner != pTFPlayer: a soldier healing with their own kit
        // credits nobody. (The heavy's own kit is the owner arm's ammo half.)
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var (picker, p) = rig.Players.Add();
        p.Health = 50;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(picker);

        rig.Touch(kit, picker);

        Assert.Equal([$"Healthkits {picker.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void An_enemy_owner_is_no_creditor()
    {
        // :146 — the same-team arm: the kit itself carries no team, so the
        // blue owner's throw is pickable by a red — and credits no one.
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var (picker, p) = rig.Players.Add(TFTeam.Red);
        p.Health = 50;
        var (owner, _) = rig.Players.Add(TFTeam.Blue);
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(owner);

        rig.Touch(kit, picker);

        Assert.Equal([$"Healthkits {picker.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void A_self_damaged_picked_player_credits_nobody()
    {
        // :148 — the picker's last entity damaged (here: the attacker of
        // their last hit) is themselves — a rocket-jumper who took a thrown
        // kit mid-trick gets the heal and the kit's own count, no assist.
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var records = new TFDamageRecords();
        rig.Services.AddInstance(records);
        var (picker, p) = rig.Players.Add();
        p.Health = 50;
        records.Note(picker.EntryIndex, new TFHitRecord(picker, 20f, DamageType.Blast, TFCritType.None,
                                                        TFDamageCustom.None, TFWeaponId.Rocketlauncher,
                                                        "tf_weapon_rocketlauncher", 0f),
                     damager: false); // a self hit: same team, no history entry
        var (owner, _) = rig.Players.Add();
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(owner);

        rig.Touch(kit, picker);

        Assert.Equal(p.MaxHealth, p.Health);
        Assert.Equal([$"Healthkits {picker.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void The_heal_assist_refuses_a_bogus_amount()
    {
        // tf_gamestats.cpp:755-763 — Event_PlayerHealedOtherAssist ignores a
        // value beyond 1000: an 1100-max soldier missing 1050 heals 1050,
        // the bonus points are awarded and the assist is not.
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var (picker, p) = rig.Players.Add();
        p.MaxHealth = 1100;
        p.Health = 50;
        var (owner, _) = rig.Players.Add();
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);
        kit.SetOwner(owner);

        rig.Touch(kit, picker);

        Assert.Equal(1100, p.Health);
        Assert.Equal([$"BonusPoints {owner.EntryIndex} 10", $"Healthkits {picker.EntryIndex} 1"], stats.Heals);
    }

    // The disguise-health arm — entity_healthkit.cpp:115-130: a disguised
    // spy's fake health (the carrier's m_iDisguiseHealth) takes the kit's
    // share of GetDisguiseMaxHealth (:8358-8369, the disguise class's script
    // maximum, the fixture's soldier 200), capped at the fake's own ceiling.

    /// <summary>
    /// A rig whose single player is a disguised spy (the <c>Disguised</c>
    /// condition, unless told otherwise) whose carrier carries a fake health
    /// and a disguise class; the class table is the shipped fixtures, so the
    /// soldier disguise's maximum is 200 (TFClassDataTests).
    /// </summary>
    private static (MapItemRig Rig, EntityHandle Player, FakeMapPlayer P, TFPlayerNetState Carrier)
        DisguiseRig(TFClass disguiseClass = TFClass.Soldier, int disguiseHealth = 30, bool disguised = true)
    {
        var rig = new MapItemRig();
        rig.Services.AddInstance(new TFClassTable(new RecordingLog(), TFClassTableTests.Files.Shipped()));
        var data = new TFMovementFixtures.Players();
        rig.Services.AddInstance<ITFPlayerData>(data);
        var (player, p) = rig.Players.Add(playerClass: TFClass.Spy);
        var carrier = data.Add(player.EntryIndex, TFClass.Spy);
        carrier.DisguiseClass = (int)disguiseClass;
        carrier.DisguiseHealth = disguiseHealth;
        if (disguised)
            p.Conditions.Add(TFCond.Disguised);
        return (rig, player, p, carrier);
    }

    [Fact]
    public void A_disguised_spy_takes_the_kit_silently_for_its_fake_health()
    {
        // :115-130 — the fake health takes the kit's share of the disguise
        // class's max, and :129 sets bSuccess without bPerformPickup: the
        // full-health spy takes the kit with no sound, no event, no stat —
        // the announcements and the :180 count belong to the :132 arm.
        var (rig, player, p, carrier) = DisguiseRig();
        p.Health = p.MaxHealth;
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(200, carrier.DisguiseHealth); // ceil(200 * 1.0), capped at the fake's own ceiling
        Assert.True(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
        Assert.Empty(stats.Heals);
    }

    [Fact]
    public void A_wounded_disguised_spy_heals_and_takes_the_fake_health_too()
    {
        // The arm is beside the heal, not instead of it: the :132 arm also
        // runs, so the pickup announces and counts as any other.
        var (rig, player, p, carrier) = DisguiseRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        p.Health = 50;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(p.MaxHealth, p.Health);
        Assert.Equal(200, carrier.DisguiseHealth);
        Assert.Contains("sound HealthKit.Touch", rig.Notices.Said);
        Assert.Equal([$"Healthkits {player.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void The_fake_health_is_capped_at_the_disguise_classs_max()
    {
        // :122-125 — the add is pulled to the room up to the class max;
        // when the fake is already over that (a medigun's boost), the room
        // is negative and the fake is brought back to the maximum. :129
        // still takes the kit.
        var (rig, player, p, carrier) = DisguiseRig(disguiseHealth: 250);
        p.Health = p.MaxHealth;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(200, carrier.DisguiseHealth);
        Assert.True(kit.IsRespawning);
    }

    [Fact]
    public void The_fake_health_takes_the_kits_own_share()
    {
        // :119 — the multiplier is PackRatios[GetPowerupSize()]: the medium
        // kit adds ceil(200 * 0.5) = 100, so 30 becomes 130.
        var (rig, player, p, carrier) = DisguiseRig();
        p.Health = p.MaxHealth;
        var kit = rig.Spawn<HealthKit>(HealthKitMedium.MediumClass);

        rig.Touch(kit, player);

        Assert.Equal(130, carrier.DisguiseHealth);
        Assert.True(kit.IsRespawning);
    }

    [Fact]
    public void An_undisguised_player_with_fake_health_still_leaves_the_kit()
    {
        // :115 — the arm's gate is TF_COND_DISGUISED: a fake health riding
        // on the carrier (stock clears it at the disguise's end; it stays
        // here to show the kit never touches it undisguised) is left alone
        // and the full-health player leaves the kit.
        var (rig, player, p, carrier) = DisguiseRig(disguised: false);
        p.Health = p.MaxHealth;
        var kit = rig.Spawn<HealthKit>(HealthKit.MapClass);

        rig.Touch(kit, player);

        Assert.Equal(30, carrier.DisguiseHealth);
        Assert.False(kit.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }
}
