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
/// prop_sphere: a ball, for finding out how the physics behaves (CPhysSphere,
/// props.cpp).
///
/// Stock's own comment calls it the debug sphere, and it is a <c>CPhysicsProp</c>
/// only by inheritance: <c>OverridePropdata</c> returns true, which stops the
/// model's prop data being read at all, and with that gone every part of
/// prop_physics that decides anything — the health, the damage modifiers, the
/// explosion, the gibs — has nothing to decide from. What is left is a model the
/// solver moves, so that is what this is, and it is why this does not derive from
/// <see cref="PropPhysics"/> despite stock's class doing exactly that.
///
/// The sphere is stock's: CreateVPhysics builds a real twelve-unit sphere
/// object rather than the model's compiled hull, which is the whole point of
/// the entity (a sphere rolls where a hull tumbles), in a SOLID_BBOX of
/// twelve a side.
/// </summary>
[ScriptedEntity(EntityNames.PropSphere, ScriptedEntityKind.Prop)]
public sealed class PropSphere : AnimatingEntity
{
    /// <summary>CPhysSphere's radius.</summary>
    public const float Radius = 12f;

    public PropSphere(IEngineEntityPhysics physics)
    {
        Physics = physics;
    }

    private readonly IEngineEntityPhysics Physics;

    /// <summary>What a map may set on this entity. See <see cref="PropSphereVars"/>.</summary>
    [ScriptVars] public PropSphereVars Vars = new();

    public override void OnSpawn()
    {
        if (Vars.Model.Length > 0)
            SetModel(Vars.Model);

        // SetCollisionBounds( -12, 12 ), then the sphere, which the prop
        // shell's own creation after this returns then finds already made.
        Vector extent = new(Radius, Radius, Radius);
        Entities.SetBounds(Handle, extent * -1f, extent);

        if (!Physics.InitSpherePhysics(Handle, Radius))
            Log.Warning($"[SourceSharp] {ClassName} '{Name}' could not be made a sphere\n");
    }
}

/// <summary>
/// The map keys <see cref="PropSphere"/> declares.
/// </summary>
[ScriptVars]
public partial class PropSphereVars
{
    public string Model { get; set; } = string.Empty;
}
