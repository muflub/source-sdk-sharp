//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>What native does for an entity's physics init: it holds the object (stock's VPhysicsSwapObject).</summary>
public interface IEntityPhysicsNative
{
    /// <summary>The shell holds <paramref name="physicsObject"/> and answers the one it held; false when the handle names no shell.</summary>
    bool Swap(EntityHandle entity, nint physicsObject, out nint previous);
}
