//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// The engine's collision groups, positionally. Mirrors <c>Collision_Group_t</c>
/// in <c>public/const.h</c>, where the values are positions in an unnumbered
/// enum.
///
/// <para>
/// <b>This is NOT <see cref="CollisionGroup"/>, and the two must not be
/// merged.</b> That one is a small managed vocabulary with numbers of its own,
/// chosen precisely so a member added to the middle of the engine's table
/// cannot silently turn a vehicle clip into a door blocker — it is translated
/// in a native switch. This one has to BE the engine's numbering, because it
/// indexes the engine's matrix: row and bit are group indices as the engine
/// counts them.
/// </para>
///
/// <para>
/// So a value here is an index and a value there is a token, and mixing them is
/// a real bug. They are separate types so the compiler says so.
/// </para>
/// </summary>
public enum EngineCollisionGroup
{
    None = 0,
    Debris,
    DebrisTrigger,
    InteractiveDebris,
    Interactive,
    Player,
    BreakableGlass,
    Vehicle,
    PlayerMovement,
    Npc,
    InVehicle,
    Weapon,
    VehicleClip,
    Projectile,
    DoorBlocker,
    PassableDoor,
    Dissolving,
    PushAway,
    NpcActor,
    NpcScripted,

    /// <summary>One past the last, exactly as LAST_SHARED_COLLISION_GROUP is.</summary>
    Count,
}
