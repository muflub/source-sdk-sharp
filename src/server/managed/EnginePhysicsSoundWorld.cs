using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// <see cref="IPhysicsSoundWorld"/> over the engine: the entity's own physics
/// object read through vphysics' table (<see cref="IVPhysicsInspect"/>), its
/// origin, vphysics' surface table and the world emit. Reads and one
/// carry-out; the batch decides (plan_physics_bones_native_zero 2.3).
/// </summary>
public sealed class EnginePhysicsSoundWorld(IEngineEntityPhysics physics, IEngineEntities entities,
                                            IVPhysicsInspect objects, IVPhysicsSurfaceSounds surfaces,
                                            IEngineSound sound)
    : IPhysicsSoundWorld
{
    public Vector? ObjectPosition(EntityHandle entity)
    {
        nint o = physics.PhysicsObject(entity);
        return o == 0 ? null : objects.Position(o);
    }

    // Stock's PhysBreakSound: the entity's collision origin (a standalone
    // entity's is its origin) and the object's material.
    public (Vector Origin, int Material)? BreakSource(EntityHandle entity)
    {
        nint o = physics.PhysicsObject(entity);
        if (o == 0 || !entities.IsValid(entity))
            return null;

        return (entities.GetOrigin(entity), objects.MaterialIndex(o));
    }

    public SurfaceImpactSounds? Surface(int surfaceProps) => surfaces.Sounds(surfaceProps);

    public bool Emit(string soundName, Vector origin, int channel, float volume)
        => sound.EmitWorldSoundScript(soundName, origin, channel, volume);
}
