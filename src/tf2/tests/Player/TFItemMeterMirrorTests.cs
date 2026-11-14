using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The server half of the held-aside item meter: each frame the mirror writes
/// the arsenal's per-row pair into the carrier's
/// <see cref="TFPlayerNetState.ItemMeters"/> and
/// <see cref="TFPlayerNetState.ItemMeterTimes"/>, stock's per-slot charge as
/// the client receives it (<c>CTFPlayerShared::UpdateItemChargeMeters</c>,
/// <c>tf_player_shared.cpp:7870-7904</c>) — the holstered weapon's pair
/// included, which is what the next switch's restore advances from.
/// </summary>
public class TFItemMeterMirrorTests
{
    [Fact]
    public void The_carrier_carries_the_pairs_the_arsenal_holds()
    {
        var rig = new PlayerRig().Join();
        var mirror = new TFItemMeterMirror(rig.Engine, rig.Carriers, new Holding(0.5f, 3f));

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);

        Assert.Equal(0.5f, rig.State.ItemMeters[0], 4);
        Assert.Equal(3f, rig.State.ItemMeterTimes[0], 4);
        Assert.Equal(0f, rig.State.ItemMeters[1], 4);
        Assert.Equal(0f, rig.State.ItemMeterTimes[1], 4);
    }

    [Fact]
    public void A_pair_the_arsenal_no_longer_holds_reads_as_none()
    {
        var rig = new PlayerRig().Join();
        var meters = new Holding(0.5f, 3f);
        var mirror = new TFItemMeterMirror(rig.Engine, rig.Carriers, meters);

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);
        meters.Drop();
        mirror.Tick(11f);

        Assert.Equal(0f, rig.State.ItemMeters[0], 4);
        Assert.Equal(0f, rig.State.ItemMeterTimes[0], 4);
    }

    [Fact]
    public void A_player_with_no_carrier_is_skipped()
    {
        var rig = new PlayerRig();
        var mirror = new TFItemMeterMirror(rig.Engine, rig.Carriers, new Holding(0.5f, 3f));

        mirror.Attach(PlayerRig.Player);

        var ex = Record.Exception(() => mirror.Tick(10f));
        Assert.Null(ex);
        Assert.Equal(0, rig.Carriers.Count);
    }

    /// <summary>The row the fake holds a pair for; the next row stays none.</summary>
    private const int Row = 2;

    /// <summary>An arsenal whose carried pair is the answer, until dropped.</summary>
    private sealed class Holding(float meter, float stamp) : IWeaponGiver
    {
        private bool _held = true;

        public void Drop() => _held = false;

        public int CarriedRowAt(EntityHandle player, int slot) => _held && slot == 0 ? Row : -1;

        public bool GiveWeapon(EntityHandle player, string className) => false;

        public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;

        public bool CarriedMeterOf(EntityHandle player, int typeIndex, out float pairMeter, out float pairStamp)
        {
            pairMeter = _held && typeIndex == Row ? meter : 0f;
            pairStamp = _held && typeIndex == Row ? stamp : 0f;
            return _held && typeIndex == Row;
        }
    }
}
