//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>A surfaceprop's density, kg per cubic metre as physics_surfaceprops gives it.</summary>
public interface ISurfaceDensity
{
    float DensityOf(int surfaceProps);
}

/// <summary>The engine's surface table, through the one lookup it answers.</summary>
public sealed class EngineSurfaceDensity(PhysicsSurfaces surfaces) : ISurfaceDensity
{
    public float DensityOf(int surfaceProps) => surfaces.Of(surfaceProps).Density;
}

/// <summary>
/// Which entities are touching fluid, and which of them float (physics stock
/// parity I2). Stock's CCollisionEvent::FluidStartTouch sets
/// EFL_TOUCHING_FLUID and FluidEndTouch clears it; CBaseEntity::IsFloating is
/// that flag and a physics material less dense than water, which is what
/// gives a floating object three times the impact energy
/// (CalculatePhysicsImpactDamage). Fed by <see cref="PhysicsEventRouter"/>
/// from the ring's FluidStart, which carries the object's material, and
/// FluidEnd.
/// </summary>
public sealed class FluidTouches
{
    /// <summary>Water's density: stock floats a material below it ("only works for water").</summary>
    public const float WaterDensity = 1000f;

    private readonly ISurfaceDensity _densities;
    private readonly Dictionary<EntityHandle, int> _touching = new();

    public FluidTouches(ISurfaceDensity densities) => _densities = densities;

    /// <summary>The entity's object entered fluid; <paramref name="material"/> is its own surfaceprop.</summary>
    public void Start(EntityHandle entity, int material) => _touching[entity] = material;

    /// <summary>It left.</summary>
    public void End(EntityHandle entity) => _touching.Remove(entity);

    /// <summary>EFL_TOUCHING_FLUID.</summary>
    public bool IsTouching(EntityHandle entity) => _touching.ContainsKey(entity);

    /// <summary>CBaseEntity::IsFloating.</summary>
    public bool IsFloating(EntityHandle entity)
        => _touching.TryGetValue(entity, out int material)
           && _densities.DensityOf(material) < WaterDensity;
}
