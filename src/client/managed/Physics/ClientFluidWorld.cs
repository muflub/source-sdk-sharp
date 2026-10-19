using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;

namespace SourceSharp.Client.Physics;

/// <summary>
/// The client fluid entries' world over the engine: the physics object's own
/// vtables, the fluid controller's, the owner entity's absolute transform (a
/// clientside physics prop's is its object's), and
/// IShimPhysicsClient's splash reads and effects (lane c6-input).
/// </summary>
public sealed class ClientFluidWorld(IClientSplashCarryOut splash, IClientPhysicsObjectReads objects,
                                     IVPhysicsObjects physicsObjects, IVPhysicsFluids fluids,
                                     IEngineEntities entities, IClientPhysicsPropOwners? props = null)
    : IClientFluidWorld
{
    public nint GameData(nint physicsObject) => physicsObjects.GameData(physicsObject);

    public ushort GameFlags(nint physicsObject) => objects.GameFlags(physicsObject);

    public Vector Velocity(nint physicsObject) => splash.Velocity(physicsObject);

    public (Vector Normal, float Dist) SurfacePlane(nint fluid) => fluids.SurfacePlane(fluid);

    public (Vector Origin, Vector Angles) OwnerTransform(EntityHandle owner)
    {
        if (props is not null && props.TryTransform(owner, out Vector origin, out QAngle angles))
            return (origin, new Vector(angles.Pitch, angles.Yaw, angles.Roll));

        return (entities.GetOrigin(owner), entities.GetAngles(owner));
    }

    public Vector CollideExtent(nint physicsObject, Vector origin, Vector angles, Vector direction)
        => splash.CollideExtent(physicsObject, origin, angles, direction);

    public int PointContents(Vector point) => splash.PointContents(point);

    public (Vector Color, float Luminosity) SplashLighting(Vector point) => splash.SplashLighting(point);

    public void Splash(in ClientSplashEffect effect)
        => splash.SplashEffect((int)effect.Kind, effect.Origin, effect.Normal, effect.Scale, effect.Color,
                               effect.Lifetime, effect.Alpha);
}
