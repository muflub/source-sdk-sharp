using TF2.Server.Entities.Items;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Tests.Medigun;

using Xunit;

namespace TF2.Tests.Entities.Items;

/// <summary>
/// item_ammopack_* and item_healthammokit (stage 2.4): <c>CAmmoPack::MyTouch</c>
/// (entity_ammopack.cpp) gives <c>ceil( GetMaxAmmo( type ) * PackRatios[size] )</c>
/// of primary, secondary and metal, and an engineer's grenades1.
/// </summary>
public sealed class AmmoPackTests
{
    private static FakeMapPlayer Soldier(MapItemRig rig, out SourceSharp.Abi.EntityHandle handle, TFClass playerClass = TFClass.Soldier)
    {
        var (h, p) = rig.Players.Add(playerClass: playerClass);
        handle = h;
        p.MaxAmmo[TFAmmo.Primary] = 20;
        p.MaxAmmo[TFAmmo.Secondary] = 32;
        p.MaxAmmo[TFAmmo.Metal] = 100;
        p.MaxAmmo[TFAmmo.Grenades1] = 1;
        return p;
    }

    [Theory]
    [InlineData(AmmoPackSmall.SmallClass, 4, 7, 20)]      // ceil(20*.2), ceil(32*.2) = ceil(6.4), ceil(100*.2)
    [InlineData(AmmoPackMedium.MediumClass, 10, 16, 50)]
    [InlineData(AmmoPack.MapClass, 20, 32, 100)]
    public void A_pack_gives_its_share_of_each_maximum_and_metal(string className, int primary, int secondary, int metal)
    {
        var rig = new MapItemRig();
        var pack = rig.Spawn<AmmoPack>(className);
        var p = Soldier(rig, out var player);

        rig.Touch(pack, player);

        Assert.Equal(primary, p.Ammo[TFAmmo.Primary]);
        Assert.Equal(secondary, p.Ammo[TFAmmo.Secondary]);
        Assert.Equal(metal, p.Ammo[TFAmmo.Metal]);
        Assert.False(p.Ammo.ContainsKey(TFAmmo.Grenades1));
        Assert.Equal(["sound AmmoPack.Touch", $"item_pickup {((AmmoPack)pack).AmmoPackName}"], rig.Notices.Said);
    }

    [Fact]
    public void An_engineer_also_gets_grenades1()
    {
        var rig = new MapItemRig();
        var pack = rig.Spawn<AmmoPack>(AmmoPackSmall.SmallClass);
        var p = Soldier(rig, out var player, TFClass.Engineer);

        rig.Touch(pack, player);

        Assert.Equal(1, p.Ammo[TFAmmo.Grenades1]); // ceil(1 * 0.2)
    }

    [Fact]
    public void A_player_with_full_ammo_leaves_the_pack()
    {
        var rig = new MapItemRig();
        var pack = rig.Spawn<AmmoPack>(AmmoPack.MapClass);
        var p = Soldier(rig, out var player);
        foreach (var (ammo, max) in p.MaxAmmo)
            p.Ammo[ammo] = max;

        rig.Touch(pack, player);

        Assert.False(pack.IsRespawning);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_taken_pack_respawns_after_ten_seconds()
    {
        var rig = new MapItemRig();
        var pack = rig.Spawn<AmmoPack>(AmmoPackMedium.MediumClass);
        Soldier(rig, out var player);

        rig.Touch(pack, player);

        Assert.True(pack.IsRespawning);
        Assert.Equal(10f, pack.NextThinkDelay());
    }

    [Fact]
    public void A_health_ammo_kit_announces_ammo_only_when_it_gave_no_health()
    {
        var rig = new MapItemRig();
        var kit = rig.Spawn<HealthAmmoKit>(HealthAmmoKit.MapClass);
        var p = Soldier(rig, out var player);
        p.Health = p.MaxHealth;

        rig.Touch(kit, player);

        Assert.Equal(4, p.Ammo[TFAmmo.Primary]); // the small pack's share
        Assert.Equal(["sound AmmoPack.Touch", "item_pickup healthammokit"], rig.Notices.Said);
    }

    [Fact]
    public void A_spy_with_full_ammo_still_takes_the_pack_for_its_cloak()
    {
        // entity_ammopack.cpp:86-89 — AddToSpyCloakMeter(100 * ratio) makes a
        // full pack taken: the ammo arms fit nothing, the cloak moves, and
        // the pickup sounds and the event go as if the ammo had arrived.
        var rig = new MapItemRig();
        var pack = rig.Spawn<AmmoPack>(AmmoPackMedium.MediumClass);
        var p = Soldier(rig, out var player, TFClass.Spy);
        foreach (var (ammo, max) in p.MaxAmmo)
            p.Ammo[ammo] = max;

        rig.Touch(pack, player);

        Assert.Equal(50f, p.CloakMeter); // 100 * the medium's 0.5
        Assert.True(pack.IsRespawning);
        Assert.Equal(["sound AmmoPack.Touch", "item_pickup ammopack_medium"], rig.Notices.Said);
    }

    [Fact]
    public void A_spy_at_a_full_cloak_meter_leaves_the_pack()
    {
        // :9753 — a meter that cannot move answers false: the pack is not
        // taken and nothing is announced.
        var rig = new MapItemRig();
        var pack = rig.Spawn<AmmoPack>(AmmoPack.MapClass);
        var p = Soldier(rig, out var player, TFClass.Spy);
        foreach (var (ammo, max) in p.MaxAmmo)
            p.Ammo[ammo] = max;
        p.CloakMeter = 100f;

        rig.Touch(pack, player);

        Assert.False(pack.IsRespawning);
        Assert.Equal(100f, p.CloakMeter);
        Assert.Empty(rig.Notices.Said);
    }

    [Fact]
    public void A_health_ammo_kit_heals_a_small_kits_share_and_gives_ammo()
    {
        var rig = new MapItemRig();
        var kit = rig.Spawn<HealthAmmoKit>(HealthAmmoKit.MapClass);
        var p = Soldier(rig, out var player);
        p.Health = 100;

        rig.Touch(kit, player);

        Assert.Equal(140, p.Health);
        Assert.Equal(20, p.Ammo[TFAmmo.Metal]);
        Assert.DoesNotContain("sound AmmoPack.Touch", rig.Notices.Said);
    }

    // The gamestats calls: Event_PlayerAmmokitPickup at entity_ammopack.cpp:127
    // and entity_healthkit.cpp:295 — a pack taken counts one, and the
    // health-ammo kit counts the side that actually announced the pickup.

    [Fact]
    public void A_taken_pack_counts_the_ammokit_stat()
    {
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var pack = rig.Spawn<AmmoPack>(AmmoPack.MapClass);
        var p = Soldier(rig, out var player);

        rig.Touch(pack, player);

        Assert.Equal([$"Ammokits {player.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void A_refused_pack_counts_no_stat()
    {
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var pack = rig.Spawn<AmmoPack>(AmmoPack.MapClass);
        var p = Soldier(rig, out var player);
        foreach (var (ammo, max) in p.MaxAmmo)
            p.Ammo[ammo] = max;

        rig.Touch(pack, player);

        Assert.False(pack.IsRespawning);
        Assert.Empty(stats.Heals);
    }

    [Fact]
    public void The_health_ammo_kit_counts_the_healthkit_when_it_healed()
    {
        // :295 only fires on the ammo announcement, which the healed kit
        // skips; the heal counted as the health kit it also is.
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var kit = rig.Spawn<HealthAmmoKit>(HealthAmmoKit.MapClass);
        var p = Soldier(rig, out var player);
        p.Health = 100;

        rig.Touch(kit, player);

        Assert.Equal([$"Healthkits {player.EntryIndex} 1"], stats.Heals);
    }

    [Fact]
    public void The_health_ammo_kit_counts_the_ammokit_when_it_only_gave_ammo()
    {
        var rig = new MapItemRig();
        var stats = new TFHealingStats();
        rig.Services.AddInstance<ITFPlayerStats>(stats);
        var kit = rig.Spawn<HealthAmmoKit>(HealthAmmoKit.MapClass);
        var p = Soldier(rig, out var player);
        p.Health = p.MaxHealth; // full: the health half fails, the ammo half announces

        rig.Touch(kit, player);

        Assert.Equal([$"Ammokits {player.EntryIndex} 1"], stats.Heals);
    }
}
