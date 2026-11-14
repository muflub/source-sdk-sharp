using SourceSharp.Abi;
using SourceSharp.Tests;

using TF2.Server.Combat;
using TF2.Server.Player;
using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Player;
using TF2.Tests.Combat;

using Xunit;

namespace TF2.Tests.Player;

/// <summary>
/// <see cref="TFCritMultTicker"/>, the shared damage-event list behind the
/// crit multiplier byte: a press through the damage layer joins the event and
/// the half-second think weighs it onto
/// <see cref="TFPlayerNetState.CritMult"/> (tf_player_shared.cpp:9520-9660),
/// with stock's windows (tf_shareddefs.h:151-154) and its alias bits
/// (:1171, :9585). The roll's own chance arithmetic is the weapon lane's
/// (<see cref="TF2.Tests.Weapons.TFCritRollTests"/>); these hold the byte the
/// roll reads its chance from.
/// </summary>
public sealed class TFCritMultTickerTests
{
    /// <summary>A handle of its own for the carrier the attach builds (:66-75).</summary>
    private static readonly EntityHandle CarrierHandle = new(0x0002_00AAu);

    /// <summary>
    /// The damage layer over a metered attacker: a real carrier so the byte
    /// has its sent word, and the ticker beside it — the pair the composite's
    /// <c>Create</c> yields (TFPlayerCarriers.cs:190). Health is ample so the
    /// kill cap never bites these hits' numbers (:10770's shape).
    /// </summary>
    private static (CombatRig Rig, TFCritMultTicker Ticker) Metered()
    {
        var rig = new CombatRig();
        rig.Engine.Returns("Entities.GetHealth", 500);                       // health before the blow, ample (:10770)
        var carrierEntity = new TFPlayerDataEntity(rig.Engine);
        carrierEntity.State.BindLocal();
        rig.Engine.Returns("Entities.Create", CarrierHandle);                // what TFPlayerCarrier.Attach builds (:66-75)
        rig.Entities.Placed[CarrierHandle] = carrierEntity;                  // the directory behind the attach's check (:77)
        var carrier = new TFPlayerCarrier(rig.Engine, rig.Entities, rig.Carriers, rig.Log);
        carrier.Attach(CombatRig.Attacker);
        var ticker = new TFCritMultTicker(rig.Carriers, rig.ConVars.Declared<TFDamageEventsTrackForConVar>());
        ticker.Attach(CombatRig.Attacker);
        return (rig, ticker);
    }

    /// <summary>The byte as the client reads it, on the attacker's carrier.</summary>
    private static int ByteOf(CombatRig rig) => rig.Carriers.Get(CombatRig.Attacker.EntryIndex)!.CritMult;

    /// <summary>
    /// The chain the brief asks: a press through <c>Modify</c> books the hit,
    /// and the think half a second's distance away weighs it onto the carrier —
    /// total 400 maps to multiplier 3.5 and the byte's half (:9562-9566), the
    /// word the owners seam's <see cref="TFPlayerData.CritMultiplier"/> then
    /// reads to the roll (its own remap held by TFWeaponOwnersTests).
    /// </summary>
    [Fact]
    public void A_press_of_four_hundred_reads_the_half_byte_after_the_think()
    {
        var (rig, ticker) = Metered();

        rig.Hit(400f, DamageType.Bullet);                                     // Clock.Now 100 when the hit lands
        ticker.Tick(105f);                                                    // delta 5, inside the 2..20 window (:9548-9555)

        Assert.Equal(127, ByteOf(rig));
        Assert.Equal(2.494f, rig.Carriers.Get(CombatRig.Attacker.EntryIndex)!.CritMultiplier(), 3);   // the read the roll's chance rides (:9504-9514)
    }

    /// <summary>
    /// :9548-9551 — a second old, the event is too fresh for the window and
    /// counts for nothing; two seconds on it counts whole (the second leg also
    /// proves the first read took effect).
    /// </summary>
    [Fact]
    public void Too_fresh_damage_counts_nothing_until_the_second_window()
    {
        var (rig, ticker) = Metered();

        rig.Hit(400f, DamageType.Bullet);

        ticker.Tick(101f);                                                    // delta 1 is under the 2 (:9548)
        Assert.Equal(0, ByteOf(rig));

        ticker.Tick(102f);                                                    // gate 101.5 < 102; delta 2 is not under the strict under
        Assert.Equal(127, ByteOf(rig));
    }

    /// <summary>
    /// :9554-9555 — twenty-five seconds old sits past the summed window: kept
    /// on the list (<c>tf_damage_events_track_for</c>'s 30 is the drop line,
    /// :9539), counted nowhere.
    /// </summary>
    [Fact]
    public void Damage_past_the_held_window_counts_no_more()
    {
        var (rig, ticker) = Metered();

        rig.Hit(400f, DamageType.Bullet);

        ticker.Tick(125f);                                                    // delta 25: past MAXTIME, short of the drop (:151, :156)

        Assert.Equal(0, ByteOf(rig));
    }

    /// <summary>
    /// :9583 — the crit bonus is not damage toward the rate: a crit 100
    /// through the layer (bonus 200 beside it, the crit multiplier's share)
    /// records the hundred, landing the byte of the hundred rather than the
    /// byte the whole three hundred would weigh.
    /// </summary>
    [Fact]
    public void The_crit_bonus_is_no_part_of_the_recorded_damage()
    {
        var (rig, ticker) = Metered();

        rig.Hit(100f, DamageType.Bullet, critical: true);                     // the layer's crit: Damage 300 with Bonus 200
        ticker.Tick(105f);

        Assert.Equal(31, ByteOf(rig));                                        // hundred-only: mult 1.625, byte (int)31.875 (:9562-9566); the whole 300 would read 95
    }

    /// <summary>
    /// :9597-9649 — two blasts inside a tenth of a second of each other fold:
    /// the second takes the list slot by raising the first to its larger
    /// damage rather than stacking. Ninety summed once weighs byte 28; the two
    /// hits summed as separate events would weigh 47.
    /// </summary>
    [Fact]
    public void Two_blasts_within_a_tenth_fold_into_the_larger_once()
    {
        var (rig, ticker) = Metered();

        using (rig.Scopes.Push(TFDamageCustom.None, TFWeaponId.None, ""))    // the blast bit stays the caller's (:209)
        {
            rig.Hit(60f, DamageType.Blast);                                   // Clock.Now 100 (:9655's stamp)
            rig.Clock.Now = 100.05f;                                          // inside CRIT_DAMAGE_TIME (:9606)
            rig.Hit(90f, DamageType.Blast);
        }
        ticker.Tick(105f);

        Assert.Equal(28, ByteOf(rig));                                        // max(60, 90) once through the remap (:9630-9639)
    }
}
