namespace SourceSharp.Abi;

/// <summary>
/// The server's managed physics init (plan_physics_bones_native_zero,
/// InitPhysics to managed): what the entity physics area's
/// <c>InitPhysics</c> and <c>SetPhysicsStatic</c> reach.
/// </summary>
public interface IEntityPhysicsRoute
{
    /// <summary>The entity simulated (or static); true when it has an object afterwards.</summary>
    bool Create(EntityHandle entity, bool isStatic, bool asleep, bool debris);

    /// <summary>The held object destroyed and the entity made again, static or not.</summary>
    bool SetStatic(EntityHandle entity, bool isStatic);

    /// <summary>A static object for the entity, its solidity and movetype kept (a brush's VPhysicsInitStatic).</summary>
    bool CreateStatic(EntityHandle entity);

    /// <summary>Whether the entity's object collides (<c>IPhysicsObject::EnableCollisions</c>); nothing when it has none.</summary>
    void EnableCollisions(EntityHandle entity, bool enable);

    /// <summary>The entity simulated as a perfect sphere of <paramref name="radius"/> (prop_sphere).</summary>
    bool CreateSphere(EntityHandle entity, float radius);
}
