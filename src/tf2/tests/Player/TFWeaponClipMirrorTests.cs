using SourceSharp.Abi;
using SourceSharp.Server;

using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// The server half of the carried clip: each frame the mirror writes the
/// arsenal's per-row answer into the carrier's <see cref="TFPlayerNetState.Clips"/>,
/// stock's per-weapon <c>m_iClip1</c> arrival in one row per weapon type
/// (<c>basecombatweapon_shared.cpp</c>'s send table) — the holstered weapon's
/// clip included, which is what the resupply rewrites and the next switch
/// deploys with.
/// </summary>
public class TFWeaponClipMirrorTests
{
    [Fact]
    public void The_carrier_carries_the_clips_the_arsenal_holds()
    {
        var rig = new PlayerRig().Join();
        var mirror = new TFWeaponClipMirror(rig.Engine, rig.Carriers, new Holding(clip: 5));

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);

        Assert.Equal(5, rig.State.Clips[Row]);
        Assert.Equal(-1, rig.State.Clips[Row + 1]);
    }

    [Fact]
    public void A_clip_the_arsenal_no_longer_holds_reads_as_none()
    {
        var rig = new PlayerRig().Join();
        var clips = new Holding(clip: 5);
        var mirror = new TFWeaponClipMirror(rig.Engine, rig.Carriers, clips);

        mirror.Attach(PlayerRig.Player);
        mirror.Tick(10f);
        clips.Drop();
        mirror.Tick(11f);

        Assert.Equal(-1, rig.State.Clips[Row]);
    }

    [Fact]
    public void A_player_with_no_carrier_is_skipped()
    {
        var rig = new PlayerRig();
        var mirror = new TFWeaponClipMirror(rig.Engine, rig.Carriers, new Holding(clip: 5));

        mirror.Attach(PlayerRig.Player);

        var ex = Record.Exception(() => mirror.Tick(10f));
        Assert.Null(ex);
        Assert.Equal(0, rig.Carriers.Count);
    }

    /// <summary>The row the fake holds a clip for; the next row stays none.</summary>
    private const int Row = 2;

    /// <summary>An arsenal whose carried clip is the answer, until dropped.</summary>
    private sealed class Holding(int clip) : IWeaponGiver
    {
        private bool _held = true;

        public void Drop() => _held = false;

        public bool GiveWeapon(EntityHandle player, string className) => false;

        public int GiveAmmo(EntityHandle player, int count, string ammoName) => 0;

        public int CarriedClipOf(EntityHandle player, int typeIndex)
            => _held && typeIndex == Row ? clip : -1;
    }
}
