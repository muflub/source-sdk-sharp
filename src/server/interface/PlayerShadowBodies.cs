//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// What native does for the player shadow, and nothing it decides
/// (plan_physics_bones_native_zero 2.5): it reads the player's own state, holds
/// the three vphysics pointers of a body managed built, tears that body down
/// (the player's destructor, or managed asking), and writes the two things a
/// command leaves on the player.
/// </summary>
public interface IPlayerShadowNative
{
    /// <summary>The player's facts and the body native holds; false when the handle names no player.</summary>
    bool Facts(EntityHandle player, out PlayerShadowFacts facts);

    /// <summary>Hold a body built by the environment that exists now. False when the player is gone.</summary>
    bool Adopt(EntityHandle player, nint stand, nint crouch, nint controller);

    /// <summary>Tear the held body down (native's DestroyVCollision), or drop it if its environment has gone.</summary>
    void Release(EntityHandle player);

    /// <summary>
    /// Which hull the solver holds now; and, after a command, stock's
    /// <c>m_oldOrigin = GetAbsOrigin()</c> and the move data's step height
    /// consumed.
    /// </summary>
    void Commit(EntityHandle player, int state, bool afterCommand);

    /// <summary>The level's IPhysicsEnvironment, or 0 between levels.</summary>
    nint Environment();
}

/// <summary>
/// What stock's <c>CBasePlayer::Touch</c> reads of an entity the move ran into:
/// its movetype (the block's word, which is what the report gives an entity
/// holding an object), its solid type and flags, and its physics object.
/// False when the handle names no entity this side can read.
/// </summary>
public interface IPlayerTouchWorld
{
    bool TryEntity(EntityHandle entity, out int moveType, out int solidType, out int solidFlags, out nint physicsObject);
}
