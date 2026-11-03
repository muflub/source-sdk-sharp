using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The carrier's ammo pool: the frame's copy of what the inventory holds, the
/// server half of the published pool (<see cref="TFPlayerNetState.Ammo"/>) —
/// the word stock's client receives per player (m_iAmmo,
/// DT_LocalPlayerExclusive, player.cpp:7929) and spends a weapon switch from.
/// </summary>
public class TFAmmoPoolMirrorTests
{
    [Fact]
    public void The_carrier_carries_the_pools_the_inventory_holds()
    {
        var rig = new PlayerRig().Join();
        var mirror = new TFAmmoPoolMirror(rig.Engine, rig.Carriers, new Carrying(36, 12));

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);

        Assert.Equal(36, rig.State.Ammo[(int)TFAmmo.Primary]);
        Assert.Equal(12, rig.State.Ammo[(int)TFAmmo.Secondary]);
        Assert.Equal(0, rig.State.Ammo[(int)TFAmmo.Metal]);
        Assert.Equal(0, rig.State.Ammo[(int)TFAmmo.Grenades1]);
    }

    [Fact]
    public void A_pool_the_inventory_no_longer_holds_reads_as_empty()
    {
        var rig = new PlayerRig().Join();
        var pools = new Carrying(36, 12);
        var mirror = new TFAmmoPoolMirror(rig.Engine, rig.Carriers, pools);

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);
        pools.Empty();
        mirror.Tick(11f);

        Assert.Equal(0, rig.State.Ammo[(int)TFAmmo.Primary]);
    }

    [Fact]
    public void A_player_with_no_carrier_is_skipped()
    {
        var rig = new PlayerRig();
        var mirror = new TFAmmoPoolMirror(rig.Engine, rig.Carriers, new Carrying(36, 12));

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);

        Assert.Equal(0, rig.Carriers.Count);
    }

    /// <summary>An inventory whose rows are the answer, until emptied.</summary>
    private sealed class Carrying(int primary, int secondary) : IWeaponGiver
    {
        public int Primary { get; set; } = primary;
        public int Secondary { get; set; } = secondary;
        private bool _empty;

        public void Empty() => _empty = true;

        public bool GiveWeapon(EntityHandle player, string className) => false;

        public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;

        public bool PoolOf(EntityHandle player, string ammoName, out int count)
        {
            count = 0;
            if (_empty)
                return true;

            if (ammoName == TFAmmoNames.Name(TFAmmo.Primary))
                count = Primary;
            else if (ammoName == TFAmmoNames.Name(TFAmmo.Secondary))
                count = Secondary;
            else
                return false;

            return true;
        }
    }
}
