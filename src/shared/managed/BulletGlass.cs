//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared;

/// <summary>
/// When a bullet carries on through glass: stock's rule in
/// <c>CBaseEntity::FireBullets</c> (baseentity_shared.cpp, GAME_DLL), which
/// re-fires a round from behind a <c>func_breakable</c> whose struck surface
/// is glass (<c>HandleShotImpactingGlass</c>), unless the mapper set
/// SF_BREAK_NO_BULLET_PENETRATION.
///
/// <para>
/// It was a native header (bullet_glass.h) that no compiled translation unit
/// included; the server's Arsenal asks it of every round that lands on a
/// managed <c>func_breakable</c>, and re-fires it from behind. A CLASSNAME rather than an
/// entity, because the inline version dereferenced a NULL for everything that
/// was not a CBaseEntity: something nothing can name is null, and null is not
/// a func_breakable.
/// </para>
/// </summary>
public static class BulletGlass
{
    /// <summary>SF_BREAK_NO_BULLET_PENETRATION (func_break.h).</summary>
    public const int NoBulletPenetration = 0x0800;

    /// <summary>
    /// Whether a round that just landed should be re-fired from behind what it
    /// hit: the struck surface is glass (CHAR_TEX_GLASS), the thing hit is
    /// exactly a <c>func_breakable</c> (case-insensitively, as stock's
    /// ClassMatches; <c>func_breakable_surf</c> is not one), and its
    /// spawnflags do not refuse it.
    /// </summary>
    public static bool PassesThrough(bool glassSurface, string? hitClass, int hitSpawnFlags)
        => glassSurface
        && string.Equals(hitClass, "func_breakable", StringComparison.OrdinalIgnoreCase)
        && (hitSpawnFlags & NoBulletPenetration) == 0;
}
