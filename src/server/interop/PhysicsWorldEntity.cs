using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The world entity (worldspawn, entry 0) as the physics callbacks name it.
///
/// <para>
/// Stock's PhysCreateWorld handed the world's object, and every static
/// prop's, the CWorld as game data, so a contact with either named the world
/// entity. Ours carry NULL game data (ServerPhysicsWorld; physicsowner.h says
/// why), so a callback that needs the world's handle asks here. Found by
/// classname, the engine's walk, and kept until it stops resolving: the world
/// is made again with each level, under a new serial.
/// </para>
/// </summary>
public sealed class PhysicsWorldEntity(Func<EntityHandle> find, Func<EntityHandle, bool> isValid)
{
    private EntityHandle _world = EntityHandle.None;

    /// <summary>The world's handle, or <see cref="EntityHandle.None"/> when there is no world.</summary>
    public EntityHandle Handle
    {
        get
        {
            if (_world != EntityHandle.None && isValid(_world))
                return _world;

            _world = find();
            return _world;
        }
    }

    /// <summary>
    /// The world's side: a static object with NULL game data, which is the
    /// world's own object and every static prop's (PhysicsSolver.OwnerOf's
    /// rule). A moving object with no game data is no one's.
    /// </summary>
    public static bool IsWorldSide(IVPhysicsObjects objects, nint physicsObject)
        => physicsObject != 0 && objects.GameData(physicsObject) == 0 && objects.IsStatic(physicsObject);

    /// <summary>
    /// The object's owner as stock's game data named it: the managed owner, or
    /// the world entity for the world's side, or none.
    /// </summary>
    public EntityHandle OwnerOf(IVPhysicsObjects objects, nint physicsObject)
    {
        if (physicsObject == 0)
            return EntityHandle.None;

        nint data = objects.GameData(physicsObject);
        if (data == 0)
            return objects.IsStatic(physicsObject) ? Handle : EntityHandle.None;

        return PhysicsGameData.Owner(data);
    }
}
