//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// func_vehicleclip: a wall only a vehicle can hit.
///
/// <para>
/// The whole entity is one idea that nothing else in Source can express. It is
/// not an invisible wall — a player walks straight through it — and it is not a
/// trigger, because nothing is reported. It is SOLID, and the collision group it
/// is in makes <c>CGameRules::ShouldCollide</c> answer no for everything that is
/// not a vehicle.
/// </para>
///
/// <para>
/// This mod has no vehicles, so the brush collides with nothing at all today —
/// which is also what stock does in a map with no vehicles, and is why the class
/// is worth owning rather than dropping. An unclaimed classname is an entity the
/// engine discards with a warning, and the mapper who placed one gets no clip
/// brush and no explanation. What owning it cost is a collision group on the
/// shell, which used to be a constant and is now a property — so any later clip
/// brush says what it clips in the same sentence.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.FuncVehicleclip, ScriptedEntityKind.Brush)]
public sealed class FuncVehicleClip : ScriptedEntity
{
    /// <summary>Stock's Spawn sets MOVETYPE_PUSH.</summary>
    public override bool MovesAsPusher => true;

    public FuncVehicleClip(IEngineEntityPhysics physics)
    {
        Physics = physics;
    }

    /// <summary>this entity as the solver sees it.</summary>
    private readonly IEngineEntityPhysics Physics;
    public override void OnSpawn()
    {
        // Solid first, group second, and the order matters only for reading:
        // the brush presents its shape to every trace, and the group decides
        // which of those traces are allowed to notice.
        SetSolidity(BrushSolidity.Solid);
        SetVisible(false);

        Physics.SetCollisionGroup(Handle, CollisionGroup.VehicleClip);

        // Static in the solver as well as in the trace world. Stock calls
        // VPhysicsInitStatic for the reason a func_wall does: a vehicle is a
        // physics object, and a brush the solver cannot see is a brush it
        // drives through. The brush's create, which keeps it SOLID_BSP.
        InitStaticPhysics();
    }

    //-------------------------------------------------------------------------
    // Enable and Disable.
    //
    // Stock says this twice — once as FSOLID_NOT_SOLID and once as the physics
    // object's own EnableCollisions — because traces read the first and the
    // solver reads the second. Only the first is here, and that is a stated
    // divergence rather than an omission: the second is observable only to
    // something the solver is simulating against this brush, and the only class
    // of thing that can be is a vehicle. See docs/divergences.md.
    //-------------------------------------------------------------------------
    [ScriptedEvent] private void OnEnable() => Physics.SetNotSolid(Handle, false);
    [ScriptedEvent] private void OnDisable() => Physics.SetNotSolid(Handle, true);
}
