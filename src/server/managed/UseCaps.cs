//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The use bits of stock's object caps (FCAP_*, baseentity_shared.h): what a
/// player's +USE may do with an entity, read through
/// <see cref="ScriptedEntity.ObjectCaps"/>. The transition and save bits are
/// not here; nothing on this side reads them.
/// </summary>
[Flags]
public enum UseCaps
{
    None = 0,

    /// <summary>FCAP_IMPULSE_USE: a press uses it once.</summary>
    ImpulseUse = 0x10,

    /// <summary>FCAP_CONTINUOUS_USE: used on every command the key is held.</summary>
    ContinuousUse = 0x20,

    /// <summary>FCAP_ONOFF_USE: used on the press and again on the release.</summary>
    OnOffUse = 0x40,

    /// <summary>FCAP_DIRECTIONAL_USE: the player sends +/- 1 (tracktrains only).</summary>
    DirectionalUse = 0x80,

    /// <summary>FCAP_USE_ONGROUND: found by standing on it.</summary>
    UseOnGround = 0x100,

    /// <summary>FCAP_USE_IN_RADIUS: found by the radius search, not only under the crosshair.</summary>
    UseInRadius = 0x200,

    /// <summary>
    /// The four that make an entity usable at all (CBasePlayer::IsUseableEntity),
    /// and the ones a child takes from its parent (CBaseEntity::ObjectCaps).
    /// </summary>
    Usable = ImpulseUse | ContinuousUse | OnOffUse | DirectionalUse,
}
