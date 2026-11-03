//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Combat;

namespace TF2.Shared;

/// <summary>
/// The sapper's use as a seam. The wrench hook below runs on both sides, but
/// only a server can search the buildings, place a sapper and hurt one, so
/// the hook and the <c>sap</c> command ask this interface and the server's
/// use layer (<c>TF2.Server.Buildings.TFSapperUse</c>) answers it. On a
/// client replay the slot is empty and every call is a no-op: the sap and
/// the sapper's wound are the server's.
/// </summary>
public interface ITFSapperState
{
    /// <summary>
    /// The sapper slot's placement (<c>CTFWeaponBuilder::PrimaryAttack</c>'s
    /// <c>BS_PLACING</c> attachment arm, tf_weapon_builder.cpp:358-424, over
    /// <c>FindSnapToBuildPos</c> / <c>FindNearestBuildPoint</c>,
    /// tf_obj.cpp:916-1168): the enemy building the owner faces and reaches
    /// gets a sapper — one per building — and a teleporter pair both ends.
    /// Answers whether one was placed.
    /// </summary>
    bool PlaceSapper(int ownerIndex);

    /// <summary>
    /// The hostile-arm find the placement's ghost walks
    /// (<c>CBaseObject::FindSnapToBuildPos</c> through
    /// <c>UpdateAttachmentPlacement</c>, tf_obj.cpp:663-679, :1169-1280): the
    /// nearest enemy building the owner faces, reaches and sees, its build
    /// point the ghost stands on. Answers whether one was found; none, the
    /// handle is <see cref="EntityHandle.None"/> and the ghost hides.
    /// </summary>
    bool FindSappable(int ownerIndex, out EntityHandle building, out Vector point);

    /// <summary>
    /// The wrench's building hit (<c>CTFWrench::Smack</c> →
    /// <c>OnFriendlyBuildingHit</c> → <c>InputWrenchHit</c>,
    /// tf_weapon_wrench.cpp:145-199, tf_obj.cpp:2746-2802): the sapper on a
    /// friendly building inside the swing takes <c>WRENCH_DMG_VS_SAPPER</c>.
    /// Answers whether one was hit.
    /// </summary>
    bool WrenchSmack(int ownerIndex, float range);
}

/// <summary>
/// The wrench's link in the chain, beside the melee behaviour it runs under:
/// the swing asks the sapper seam for its building hit
/// (<c>CTFWrench::Smack</c>'s <c>OnFriendlyBuildingHit</c>,
/// tf_weapon_wrench.cpp:145-199). Stock lands the hit at the smack 0.5 s
/// after the swing; here it is asked at the swing itself, the knife's
/// precedent (docs/divergences.md). A client replay without the seam asks
/// nothing: the sapper's wound is the server's, and so is the building's
/// repair, refill and upgrade metal that rides the same call.
/// </summary>
[SideService(Side.Server)]
[SideService(Side.Client)]
public sealed class TFWrenchHook(ITFSapperState? sappers = null) : ITFWeaponHook
{
    /// <summary>The wrench's row (<c>TF_WEAPON_WRENCH</c>'s script).</summary>
    public const string WrenchScript = "tf_weapon_wrench";

    /// <summary>
    /// <c>CTFWrench::Smack</c>'s swing for the building hit
    /// (tf_weapon_wrench.cpp:165): the wrench reaches a building at 70, past
    /// the melee script's 48 the swing otherwise answers.
    /// </summary>
    public const float SmackRange = 70f;

    /// <summary>Stage 2.12's band, after the <c>sap</c> command.</summary>
    public const int Order = 1_000_000 - 1_000 + 986;

    public int HookOrder => Order;

    private static bool IsWrench(in TFWeaponHookContext weapon) =>
        string.Equals(weapon.Type.Name, WrenchScript, StringComparison.Ordinal);

    public bool BeforeFire(in TFWeaponHookContext weapon, in WeaponAttackContext attack)
    {
        if (IsWrench(in weapon) && attack.Command.OwnerIndex > 0 && attack.EffectsLive && sappers is { } state)
            state.WrenchSmack(attack.Command.OwnerIndex, SmackRange);
        return true;
    }
}
