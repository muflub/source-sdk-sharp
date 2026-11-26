using SourceSharp.Abi;

using TF2.Server.Econ;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Econ;

using TF2.Tests.Ctf;
using TF2.Tests.Projectiles;
using TF2.Tests.Weapons;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The Dead Ringer's seat: the watch the loadout screen equips
/// (<c>tf2_equip spy pda2 59</c>) is the item the spawn kit grants, and the
/// grant is the player manager whose walk is the press's mode read
/// (<c>HasFeignDeath</c>'s <c>set_weapon_mode</c> over
/// <c>GetAttributeManager</c>, tf_weapon_invis.h:66 over
/// tf_player.h:637-642) — the arm <see cref="TFSpyComponent"/> presses with.
/// </summary>
public sealed class TFDeadRingerSeatFacts
{
    private static readonly EntityHandle Player = new(0x0001_0000u | PlayerRig.Index);

    /// <summary>The Dead Ringer's defindex, as the schema carries it.</summary>
    private const int DeadRinger = 59;

    /// <summary><c>TF_WEAPON_INVIS</c> as the rows spell it.</summary>
    private const string InvisWatch = "tf_weapon_invis";

    /// <summary>The seat: the spy's kit granted, the DR equipped first when
    /// <paramref name="ringer"/> says so, the server's owners over the items
    /// the grant wrote.</summary>
    private static (TFServerWeaponOwners Owners, AttributeHooks Hooks) Seated(bool ringer)
    {
        var rig = new PlayerRig().As(TFClass.Spy);

        var inventory = new LocalInventory(
            new InventoryStore(new ModInfo(_ => null), rig.Schema, rig.Log), rig.Schema,
            new TFWeaponOwnersTests.NoClients(), rig.Log);
        inventory.OnClientActive(PlayerRig.Index);

        if (ringer)
        {
            ItemInstance? watch = inventory.Grant(PlayerRig.Index, DeadRinger);
            Assert.NotNull(watch);
            Assert.True(inventory.Equip(PlayerRig.Index, TFClass.Spy, TFLoadoutSlot.Pda2, watch!.Id));
        }

        TFPlayerItems items = rig.Items;
        var loadout = LoadoutDeps.Loadout(inventory, rig.Schema, classes: rig.Carriers, items: items,
            table: rig.Classes, catalog: rig.Catalog, carriers: rig.Carriers,
            players: rig.Engine, precache: rig.Engine, log: rig.Log);
        Assert.Contains(InvisWatch, loadout.Weapons(Player));

        var owners = new TFServerWeaponOwners(rig.Carriers, new TFRoundWinners(), items,
            rig.Engine, new EmptyDirectory(), new FakeStats(),
            new Combat.RigMediguns(), rig.Schema, new PoollessGiver(), rig.Catalog);
        return (owners, new AttributeHooks());
    }

    /// <summary>The equipped watch's seat answers the press's mode read: the
    /// grant put the item's <c>set_weapon_mode</c> on the manager the
    /// <c>ActivateInvisibilityWatch</c> arm walks (TFSpy.cs, tf_weapon_invis.h:66).</summary>
    [Fact]
    public void The_equipped_dead_ringer_reads_mode_one_through_the_seat()
    {
        var (owners, hooks) = Seated(ringer: true);

        Assert.Equal(TFSpyComponent.InvisFeignDeathMode,
                     hooks.Int(0, "set_weapon_mode", owners.ManagerOf(PlayerRig.Index)));
    }

    /// <summary>The press reads the watch item itself (TFSpyComponent.WatchScript): the equipped Dead Ringer's seat answers 1 there too.</summary>
    [Fact]
    public void The_equipped_dead_ringer_reads_mode_one_off_the_watch()
    {
        var (owners, hooks) = Seated(ringer: true);

        Assert.Equal(TFSpyComponent.InvisFeignDeathMode,
                     hooks.Int(0, "set_weapon_mode", owners.ItemOf(PlayerRig.Index, TFSpyComponent.WatchScript)));
    }

    /// <summary>The seat's control: the base watch's kit reads no mode through
    /// the same walk, so the mode-one answer is the item's, not the walk's.</summary>
    [Fact]
    public void The_base_watch_reads_no_mode()
    {
        var (owners, hooks) = Seated(ringer: false);

        Assert.Equal(0, hooks.Int(0, "set_weapon_mode", owners.ManagerOf(PlayerRig.Index)));
    }
}
