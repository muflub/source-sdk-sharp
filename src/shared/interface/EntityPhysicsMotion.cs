//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The entity side of <see cref="EntityPhysicsMotion"/>: the entity's
/// <c>IPhysicsObject *</c> (a held-pointer read, 0 for none), its own velocity
/// members (the shell's mirrors, for an entity the solver is not moving), its
/// facing, and the console.
/// </summary>
public interface IEntityPhysicsMotionHost
{
    nint PhysicsObject(EntityHandle entity);
    Vector EntityVelocity(EntityHandle entity);
    void SetEntityVelocity(EntityHandle entity, Vector velocity);
    Vector EntityAngularVelocity(EntityHandle entity);
    void SetEntityAngularVelocity(EntityHandle entity, Vector degreesPerSecond);
    Vector Angles(EntityHandle entity);
    void Warning(string message);
}
