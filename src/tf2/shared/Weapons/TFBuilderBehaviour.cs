//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace TF2.Shared;

/// <summary>
/// The builder's placement as a seam. The behaviour below runs on both sides,
/// but only a server can trace the world, refuse a spot, pay the metal and
/// birth the buildable, so the deploy, the trigger and the rotate ask this
/// interface and the server's use layer
/// (<c>TF2.Server.Buildings.TFBuilderPlacement</c>) answers it. On a client
/// replay the slot is empty and every call is a no-op: the blueprint and the
/// build are the server's.
/// </summary>
public interface ITFBuilderPlacement
{
    /// <summary>
    /// <c>CTFWeaponBuilder::Deploy</c>'s <c>StartPlacement</c>
    /// (<c>tf_weapon_builder.cpp:165-172</c>): the blueprint is born — or the
    /// carried object comes back out of the arms (the carried branch,
    /// :1003-1007) — the moment the builder is in the hands.
    /// </summary>
    void StartPlacement(int ownerIndex);

    /// <summary>
    /// <c>UpdatePlacementState</c> → <c>CBaseObject::UpdatePlacement</c>
    /// (<c>tf_weapon_builder.cpp:535-560</c>, <c>tf_obj.cpp:1150-1164</c>):
    /// the blueprint is moved to where the crosshair looks, and the spot is
    /// re-judged. One ask per command.
    /// </summary>
    void UpdatePlacement(int ownerIndex);

    /// <summary>
    /// <c>CTFWeaponBuilder::PrimaryAttack</c>'s <c>BS_PLACING</c> arm
    /// (<c>tf_weapon_builder.cpp:358-423</c>): the buildable stands at the
    /// valid spot — the metal paid, construction started — and the builder
    /// switches away. Answers whether it was placed.
    /// </summary>
    bool Place(int ownerIndex);

    /// <summary>
    /// <c>CBaseObject::RotateBuildAngles</c> (<c>tf_obj.cpp:3416-3421</c>):
    /// the blueprint turns a quarter turn per press.
    /// </summary>
    void Rotate(int ownerIndex);

    /// <summary>
    /// <c>CTFWeaponBuilder::StopPlacement</c> (<c>tf_weapon_builder.cpp:1041-1055</c>):
    /// the builder leaves the hands — the placing blueprint is taken out of
    /// the world, the carried one goes back into the arms.
    /// </summary>
    void StopPlacement(int ownerIndex);

    /// <summary>
    /// The builder itself into the hands (<c>StartBuildingObjectOfType</c>'s
    /// <c>Weapon_Switch</c>, <c>tf_player_shared.cpp:13234-13258</c>): a
    /// build order or a lift arms it. Answers whether a builder is out.
    /// </summary>
    bool ArmBuilder(int ownerIndex);

    /// <summary>
    /// The build order the console names (<c>StartBuildingObjectOfType</c>'s
    /// gate, tf_player.cpp:7594): the type and mode the builder's next deploy
    /// places — a refused <c>CanBuild</c> records nothing and arms no builder.
    /// Answers whether the order was taken.
    /// </summary>
    bool BeginOrder(EntityHandle player, TFObjectType type, int mode);
}

/// <summary>
/// The builder's shell (<c>CTFWeaponBuilder</c>, <c>tf_weapon_builder.cpp</c>):
/// the row has no attack of its own — the core would swing it, so the shot is
/// vetoed — and its work hangs off the frame arms. The deploy brings the
/// blueprint out (<c>StartPlacement</c>, :165-172) under its 0.35 s refire
/// (:173); a trigger past the refire places (:294-297 →
/// <c>PrimaryAttack</c>, whose refire is :440's 0.2 s); the alt-fire turns
/// the blueprint a quarter turn per press under the <c>m_bInAttack2</c>
/// debounce (:443-475); the tail keeps the blueprint at the crosshair
/// (<c>UpdatePlacementState</c>, :535-560). On a holster the placement stops
/// (:216-268 → <c>StopPlacement</c>). A client replay without the seam runs
/// the same state writes and asks nothing: the blueprint is the server's.
/// </summary>
public class TFBuilderBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts), IWeaponBehaviour
{
    /// <summary>Deploy's refire (<c>tf_weapon_builder.cpp:173</c>).</summary>
    public const float DeployRefire = 0.35f;

    /// <summary>PrimaryAttack's refire (<c>tf_weapon_builder.cpp:440</c>).</summary>
    public const float PlaceRefire = 0.2f;

    /// <summary>SecondaryAttack's refire (<c>tf_weapon_builder.cpp:474</c>).</summary>
    public const float RotateRefire = 0.2f;

    /// <summary>The row is a tool: no swing, no spend. The place is the veto's.</summary>
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        // PrimaryAttack's own refire (tf_weapon_builder.cpp:440) — the
        // machine's clock is stock's here, the row having no ammunition of
        // its own to make it read otherwise.
        attack.Weapon.NextPrimaryAttack = attack.Now + PlaceRefire;

        if (attack.EffectsLive && Parts.Builder is { } placement)
            placement.Place(attack.Command.OwnerIndex);

        return false;
    }

    public void Deploy(in WeaponAttackContext attack)
    {
        // Deploy's own 0.35 (tf_weapon_builder.cpp:173), past the machine's
        // deploy time, and the blueprint born as the builder comes out (:172).
        // A deploy runs outside a user command, so the owner is the weapon
        // state's — the deploy dressed it (Arsenal's WeaponState.OwnerIndex) —
        // stock's GetOwnerEntity(), not a default command's index 0.
        attack.Weapon.NextPrimaryAttack = attack.Now + DeployRefire;

        if (attack.EffectsLive && Parts.Builder is { } placement)
            placement.StartPlacement(attack.Weapon.OwnerIndex);
    }

    /// <summary>
    /// The rotate, under the <c>m_bInAttack2</c> debounce
    /// (<c>tf_weapon_builder.cpp:443-449</c>) and its own 0.2 s refire (:474).
    /// The base's ItemPostFrame clears the debounce on release.
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if ((weapon.BehaviourMode & ModeInAttack2) != 0)
            return;

        weapon.BehaviourMode |= ModeInAttack2;
        weapon.NextSecondaryAttack = attack.Now + RotateRefire;

        if (attack.EffectsLive && Parts.Builder is { } placement)
            placement.Rotate(attack.Command.OwnerIndex);
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        // UpdatePlacementState, one ask per command (tf_weapon_builder.cpp:535):
        // the blueprint follows the crosshair whether or not anything is held.
        if (attack.EffectsLive && Parts.Builder is { } placement)
            placement.UpdatePlacement(attack.Command.OwnerIndex);

        base.ItemPostFrame(attack);
    }

    public void Holster(in WeaponAttackContext attack)
    {
        // As the deploy: outside a command, the owner is the weapon state's.
        if (Parts.Builder is { } placement)
            placement.StopPlacement(attack.Weapon.OwnerIndex);
    }
}
