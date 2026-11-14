using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Server.Combat;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Shared.Player;

using Xunit;

namespace TF2.Tests.Combat;

/// <summary>
/// The pyro rage's gain arms over the machine's damage layer: the burn arm
/// (<c>tf_player.cpp:9730-9735</c> through <see cref="TFRageMeter.GainOnBurnDamage"/>)
/// fills the Phlogistinator's meter from the damage burning does, the
/// <see cref="TFRageMeter.BurnDamageDivisor"/> over the buff table's pyro rage
/// (<c>g_RageBuffTypes</c>, tf_shareddefs.h:2767), while the dealt arm stays
/// the soldier's.
/// </summary>
public sealed class TFPyroRageTests
{
    /// <summary>The afterburn tick's type, the burn the meter builds from.</summary>
    private static readonly DamageType Tick = TFBurn.TickDamageType;

    /// <summary>
    /// A burning on a pyro attacker wearing the rage's buff type builds the
    /// meter by the damage over three (<c>tf_player.cpp:9733</c>): twelve of
    /// burning four points at a time. A hit no burn dealt — the plain bullet —
    /// builds nothing: the arm's type test (:9731).
    /// </summary>
    [Fact]
    public void BurningBuildsThePyrosRage()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Pyro;
        rig.AttackerItem(TFRageMeter.BuffTypeAttribute, AttributeDescriptionFormat.Additive, TFRageMeter.PyroRageBuffType);

        rig.Hit(12f, Tick);
        Assert.Equal(4f, rig.AttackerState.RageMeter);

        rig.Hit(12f, DamageType.Bullet | TFDamageBits.UseDistanceMod);
        Assert.Equal(4f, rig.AttackerState.RageMeter);
    }

    /// <summary>
    /// The meter caps at full under a burning past it, stock's
    /// <c>MIN(val, 100)</c> (tf_player_shared.cpp:13883), and a draining
    /// deploy refuses the gain (<c>SetRageMeter</c>, :13875) — the burn arm
    /// refusing with the dealt one.
    /// </summary>
    [Fact]
    public void TheBurnArmCapsAndTheDrainRefuses()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Pyro;
        rig.AttackerItem(TFRageMeter.BuffTypeAttribute, AttributeDescriptionFormat.Additive, TFRageMeter.PyroRageBuffType);

        rig.Hit(600f, Tick);
        Assert.Equal(TFRageMeter.FullMeter, rig.AttackerState.RageMeter);

        rig.AttackerState.RageDraining = true;
        rig.Hit(12f, Tick);
        Assert.Equal(TFRageMeter.FullMeter, rig.AttackerState.RageMeter);
    }

    /// <summary>
    /// A buff type whose table flag is not the burn — the Buff Banner's 1 —
    /// gains nothing from a burning however long it burns: the flag test of
    /// the pyro arm (<c>g_RageBuffTypes[iBuffId].m_iBuffFlags &
    /// kRageBuffFlag_OnBurnDamageDealt</c>, tf_player.cpp:8749).
    /// </summary>
    [Fact]
    public void ABannerBuffGainsNothingFromBurning()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Pyro;
        rig.AttackerItem(TFRageMeter.BuffTypeAttribute, AttributeDescriptionFormat.Additive, 1f);

        rig.Hit(12f, Tick);
        Assert.Equal(0f, rig.AttackerState.RageMeter);
    }

    /// <summary>
    /// The soldier's meter is the dealt arm's alone: a burning on a soldier
    /// attacker leaves the pyro arm unread, the class gate of the same block.
    /// </summary>
    [Fact]
    public void ASoldiersBurnBuildsNothingOnTheBurnArm()
    {
        var rig = new CombatRig();
        rig.AttackerState.PlayerClass = (int)TFClass.Soldier;
        rig.AttackerItem(TFRageMeter.BuffTypeAttribute, AttributeDescriptionFormat.Additive, TFRageMeter.PyroRageBuffType);

        rig.Hit(12f, Tick);
        Assert.Equal(0f, rig.AttackerState.RageMeter);
    }
}
