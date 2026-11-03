using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Tests;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Server.Rules;
using TF2.Shared;
using TF2.Shared.Econ;
using TF2.Shared.Rules;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The loadout as TF weapons: each item's item_class as the class's own
/// weapon entity (stock's TranslateWeaponEntForClass), which is a 1.9 weapon
/// row, and the ammo column filled to the class's AmmoMax (GiveDefaultItems).
/// </summary>
public sealed class TFPlayerLoadoutTests
{
    private static (PlayerRig Rig, TFLoadoutProvider Loadout, TFPlayerItems Items) Make(TFClass cls,
        ITeamRoundState? round = null, bool meleeOnly = false)
    {
        var rig = new PlayerRig().As(cls);
        var items = new TFPlayerItems();
        var inventory = new LocalInventory(new InventoryStore(new ModInfo(_ => null), rig.Schema, rig.Log),
                                           rig.Schema, new TFWeaponOwnersTests.NoClients(), rig.Log);
        ITFStalemateSettings? stalemate = null;
        if (meleeOnly)
        {
            var convars = ConVarRegistry.For(TestServices.UnattachedConsole());
            convars.RegisterAll(typeof(TFStalemateConVars).Assembly);
            var melee = convars.Get<MpStalemateMeleeOnlyConVar>()!;
            melee.Bind(-1, "mp_stalemate_meleeonly", "1");
            var changeTime = convars.Get<TFStalemateChangeClassTimeConVar>()!;
            changeTime.Bind(-1, "tf_stalematechangeclasstime", "20");
            stalemate = new TFStalemateConVars(melee, changeTime);
        }

        return (rig, new TFLoadoutProvider(inventory, rig.Schema, rig.Carriers, items, rig.Classes, round, stalemate), items);
    }

    /// <summary>The round the loadout reads, fixed at one state.</summary>
    private sealed class FixedRound(RoundState state) : ITeamRoundState
    {
        public RoundState State { get; } = state;
        public bool InWaitingForPlayers => false;
        public bool InSetup => false;
        public float MapResetTime => 0f;
        public float StalemateStartTime => -1f;
        public TFTeam WinningTeam => TFTeam.Unassigned;
    }

    [Theory]
    [InlineData("tf_weapon_shotgun", TFClass.Soldier, "tf_weapon_shotgun_soldier")]
    [InlineData("tf_weapon_shotgun", TFClass.HeavyWeapons, "tf_weapon_shotgun_hwg")]
    [InlineData("tf_weapon_shotgun", TFClass.Pyro, "tf_weapon_shotgun_pyro")]
    [InlineData("tf_weapon_shotgun", TFClass.Engineer, "tf_weapon_shotgun_primary")]
    [InlineData("tf_weapon_pistol", TFClass.Scout, "tf_weapon_pistol_scout")]
    [InlineData("tf_weapon_pistol", TFClass.Engineer, "tf_weapon_pistol")]
    [InlineData("tf_weapon_scattergun", TFClass.Scout, "tf_weapon_scattergun")]
    // The builder family's retarget: the spy's builder item is the sapper row
    // (ManageBuilderWeapons's SetSubType, tf_player.cpp:4627-4631).
    [InlineData("tf_weapon_builder", TFClass.Spy, "tf_weapon_sapper")]
    [InlineData("tf_weapon_builder", TFClass.Engineer, "tf_weapon_builder")]
    [InlineData("saxxy", TFClass.Scout, "saxxy")]
    public void An_item_class_becomes_the_classs_own_weapon(string itemClass, TFClass cls, string expected)
    {
        var rig = new PlayerRig();

        Assert.Equal(expected, TFItemClassTranslation.Translate(itemClass, rig.Classes[cls]));
    }

    [Fact]
    public void The_scout_spawns_with_the_scattergun_the_scout_pistol_and_the_bat()
    {
        var (_, loadout, _) = Make(TFClass.Scout);

        Assert.Equal(["tf_weapon_scattergun", "tf_weapon_pistol_scout", "tf_weapon_bat"], loadout.Weapons(PlayerRig.Player));
    }

    [Fact]
    public void The_heavy_spawns_with_the_minigun_the_heavys_shotgun_and_the_fists()
    {
        var (_, loadout, _) = Make(TFClass.HeavyWeapons);

        Assert.Equal(["tf_weapon_minigun", "tf_weapon_shotgun_hwg", "tf_weapon_fists"], loadout.Weapons(PlayerRig.Player));
    }

    [Fact]
    public void The_kit_enters_the_players_attribute_manager_with_its_items_as_providers()
    {
        // The stock equip: CEconEntity::ReapplyProvision (econ_entity.cpp:285-308,
        // ProvideTo(pNewOwner) at :304) gives the owner each carried item as an
        // attribute provider, so IsAllowedToPickUpFlag's hook walks the kit.
        // A new kit's grants replace the old (m_AttributeManager, tf_player.cpp:3301).
        var (_, loadout, items) = Make(TFClass.Scout);

        loadout.Weapons(PlayerRig.Player);
        var first = Assert.IsType<AttributeContainer>(items.ManagerOf(PlayerRig.Index));

        loadout.Weapons(PlayerRig.Player);
        var second = Assert.IsType<AttributeContainer>(items.ManagerOf(PlayerRig.Index));

        Assert.NotSame(first, second);
        Assert.Equal(3, second.Providers.Count);   // the scattergun, the pistol and the bat
    }

    [Fact]
    public void A_hook_walked_on_an_item_reaches_the_manager_the_item_provides_to()
    {
        // The ON_OTHER half of the same equip: the item's GetAttributeOwner is
        // its holder (econ_entity.cpp), so CALL_ATTRIB_HOOK_*_ON_OTHER of the
        // weapon ends in the player manager — the initiator is then skipped in
        // the providers round, so nothing applies twice.
        var (rig, loadout, items) = Make(TFClass.Scout);

        _ = loadout.Weapons(PlayerRig.Player);
        var scattergun = items.Of(PlayerRig.Index, "tf_weapon_scattergun");
        var manager = Assert.IsType<AttributeContainer>(items.ManagerOf(PlayerRig.Index));
        var hooks = new AttributeHooks();

        float before = hooks.Float(4f, "mult_crit_chance", scattergun);
        manager.Set(AttributeValue.OfFloat(rig.Schema.Attribute("crit mod disabled")!, 0.5f));
        float after = hooks.Float(4f, "mult_crit_chance", scattergun);

        Assert.Equal(before * 0.5f, after);
    }

    /// <summary>Every class's base kit names weapon rows: none falls to the core's "no weapon row".</summary>
    [Fact]
    public void Every_classs_kit_is_made_of_weapon_rows()
    {
        var rows = WeaponFixtures.Types().Names().ToHashSet(StringComparer.Ordinal);
        int named = 0;
        for (var c = TFClass.Scout; c < TFClass.Civilian; c++)
        {
            var (_, loadout, _) = Make(c);
            foreach (string weapon in loadout.Weapons(PlayerRig.Player))
            {
                Assert.True(rows.Contains(weapon), $"{c}'s {weapon} is not a weapon row");
                named++;
            }
        }

        Assert.True(named >= 27, $"only {named} weapons named across the nine classes");
    }

    [Fact]
    public void The_items_are_recorded_under_the_translated_rows()
    {
        var (_, loadout, items) = Make(TFClass.Scout);

        _ = loadout.Weapons(PlayerRig.Player);

        Assert.NotNull(items.Of(PlayerRig.Index, "tf_weapon_pistol_scout"));
        Assert.Null(items.Of(PlayerRig.Index, "tf_weapon_pistol"));
    }

    [Fact]
    public void The_ammo_column_is_the_classs_ammo_max()
    {
        var (_, loadout, _) = Make(TFClass.Scout);

        var ammo = loadout.Ammo(PlayerRig.Player).ToDictionary(a => a.Ammo, a => a.Count);

        Assert.Equal(32, ammo["TF_AMMO_PRIMARY"]);
        Assert.Equal(36, ammo["TF_AMMO_SECONDARY"]);
        Assert.Equal(1, ammo["TF_AMMO_GRENADES3"]);
        Assert.False(ammo.ContainsKey("DUMMY AMMO"));
    }

    [Fact]
    public void A_classless_player_names_no_ammo()
    {
        var (_, loadout, _) = Make(TFClass.Undefined);

        Assert.Empty(loadout.Ammo(PlayerRig.Player));
        Assert.Empty(loadout.Weapons(PlayerRig.Player));
    }

    // ---- the melee-only sudden death (tf_player.cpp:4441-4448) ------------

    [Fact]
    public void In_a_melee_only_sudden_death_the_soldiers_kit_is_the_melee_weapon_alone()
    {
        var (_, loadout, _) = Make(TFClass.Soldier, new FixedRound(RoundState.Stalemate), meleeOnly: true);

        var slots = loadout.Loadout(PlayerRig.Index, TFClass.Soldier).Select(i => i.Slot);
        Assert.Equal([TFLoadoutSlot.Melee], slots);
        Assert.Equal(["tf_weapon_shovel"], loadout.Weapons(PlayerRig.Player));
    }

    [Fact]
    public void In_a_melee_only_sudden_death_the_spy_keeps_his_pdas()
    {
        var (_, loadout, _) = Make(TFClass.Spy, new FixedRound(RoundState.Stalemate), meleeOnly: true);

        var slots = loadout.Loadout(PlayerRig.Index, TFClass.Spy).Select(i => i.Slot);
        Assert.Contains(TFLoadoutSlot.Melee, slots);
        Assert.Contains(TFLoadoutSlot.Pda, slots);
        Assert.Contains(TFLoadoutSlot.Pda2, slots);
        Assert.DoesNotContain(TFLoadoutSlot.Primary, slots);
    }

    [Fact]
    public void A_melee_only_stalemate_that_has_not_started_leaves_the_kit_untouched()
    {
        var (_, loadout, _) = Make(TFClass.Soldier, new FixedRound(RoundState.RoundRunning), meleeOnly: true);

        Assert.Equal(3, loadout.Weapons(PlayerRig.Player).Count());
    }

    [Fact]
    public void Without_mp_stalemate_meleeonly_the_kit_is_untouched()
    {
        var (_, loadout, _) = Make(TFClass.Soldier, new FixedRound(RoundState.Stalemate));

        Assert.Equal(3, loadout.Weapons(PlayerRig.Player).Count());
    }
}
