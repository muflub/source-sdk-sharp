namespace SourceSharp.Abi;

/// <summary>
/// Calls on vphysics' own <c>IPhysicsObject</c>, made by managed directly
/// through the generated <see cref="PhysicsObjectFwd"/> (plan_physics_managed:
/// managed invokes the vtable methods itself, no shim slot per method). The
/// pointer comes from <c>IEngineEntityPhysics.PhysicsObject</c>; 0 is no object
/// and every call on it is a no-op, as stock's NULL checks are.
/// </summary>
public interface IVPhysicsObjects
{
    /// <summary><c>IPhysicsObject::RecheckContactPoints</c>.</summary>
    void RecheckContactPoints(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetGameData</c>: the owner word (see <see cref="PhysicsGameData"/>).</summary>
    nint GameData(nint physicsObject);

    /// <summary>
    /// Stock's <c>CountPhysicsObjectEntityContacts</c> (physics.cpp): the
    /// object's friction snapshot, counting the contacts whose other object
    /// <paramref name="other"/> owns, as <paramref name="ownerOf"/> names an
    /// object's owner (the caller's rule: the server's names the world's side
    /// the world entity, as stock's game data did).
    /// </summary>
    int ContactsWith(nint physicsObject, Func<nint, EntityHandle> ownerOf, EntityHandle other);

    /// <summary><c>IPhysicsCollisionData::GetContactPoint</c>.</summary>
    Vector ContactPoint(nint collisionData);

    /// <summary><c>IPhysicsObject::GetVelocityAtPoint</c>.</summary>
    Vector VelocityAtPoint(nint physicsObject, Vector worldPoint);

    /// <summary><c>IPhysicsObject::GetVelocity</c>: linear, and angular in local space.</summary>
    (Vector Linear, Vector Angular) Velocity(nint physicsObject);

    /// <summary><c>IPhysicsObject::AddVelocity</c>.</summary>
    void AddVelocity(nint physicsObject, Vector linear, Vector angular);

    /// <summary><c>IPhysicsObject::CalculateLinearDrag</c>.</summary>
    float LinearDrag(nint physicsObject, Vector direction);

    /// <summary><c>IPhysicsObject::CalculateAngularDrag</c>.</summary>
    float AngularDrag(nint physicsObject, Vector axis);

    /// <summary><c>IPhysicsObject::GetMass</c>.</summary>
    float Mass(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetGameFlags</c>.</summary>
    ushort GameFlags(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetMaterialIndex</c>.</summary>
    int MaterialIndex(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetPositionMatrix</c>'s three axis columns.</summary>
    (Vector X, Vector Y, Vector Z) Axes(nint physicsObject);

    /// <summary><c>IPhysicsObject::SetVelocity</c>: linear, and angular in local space.</summary>
    void SetVelocity(nint physicsObject, Vector linear, Vector angular);

    /// <summary><c>IPhysicsObject::IsStatic</c>.</summary>
    bool IsStatic(nint physicsObject);

    /// <summary><c>IPhysicsObject::IsMoveable</c>.</summary>
    bool IsMoveable(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetCallbackFlags</c> (CALLBACK_*).</summary>
    ushort CallbackFlags(nint physicsObject);

    /// <summary><c>IPhysicsCollisionData::GetSurfaceNormal</c>.</summary>
    Vector SurfaceNormal(nint collisionData);
}

/// <summary>An <c>IPhysicsFluidController</c>, by its generated vtable.</summary>
public interface IVPhysicsFluids
{
    /// <summary><c>GetSurfacePlane</c>: the surface's normal and distance.</summary>
    (Vector Normal, float Dist) SurfacePlane(nint fluid);

    /// <summary><c>GetDensity</c>.</summary>
    float Density(nint fluid);
}

public sealed unsafe class VPhysicsFluids : IVPhysicsFluids
{
    private readonly VPhysicsTables _vphysics;

    /// <summary>Over <paramref name="vphysics"/>, its side's vphysics forwarder tables.</summary>
    public VPhysicsFluids(VPhysicsTables vphysics)
        => _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));

    public (Vector Normal, float Dist) SurfacePlane(nint fluid)
    {
        if (fluid == 0)
            return (Vector.Zero, 0f);

        Vector normal;
        float dist;
        var vtbl = _vphysics.PhysicsFluidController;
        vtbl->GetSurfacePlane((void*)fluid, &normal, &dist);
        return (normal, dist);
    }

    public float Density(nint fluid)
        => fluid == 0 ? 0f : (_vphysics.PhysicsFluidController)->GetDensity((void*)fluid);
}

/// <summary>The real objects. Holds nothing: the pointer is the whole of the object.</summary>
public sealed unsafe class VPhysicsObjects : IVPhysicsObjects
{
    private readonly VPhysicsTables _vphysics;

    /// <summary>Over <paramref name="vphysics"/>, its side's vphysics forwarder tables.</summary>
    public VPhysicsObjects(VPhysicsTables vphysics)
        => _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));

    public void RecheckContactPoints(nint physicsObject)
    {
        if (physicsObject == 0)
            return;

        var vtbl = _vphysics.PhysicsObject;
        vtbl->RecheckContactPoints((void*)physicsObject);
    }

    public nint GameData(nint physicsObject)
    {
        if (physicsObject == 0)
            return 0;

        var vtbl = _vphysics.PhysicsObject;
        return (nint)vtbl->GetGameData((void*)physicsObject);
    }

    public int ContactsWith(nint physicsObject, Func<nint, EntityHandle> ownerOf, EntityHandle other)
    {
        ArgumentNullException.ThrowIfNull(ownerOf);
        if (physicsObject == 0)
            return 0;

        var objectVtbl = _vphysics.PhysicsObject;
        void* snapshot = objectVtbl->CreateFrictionSnapshot((void*)physicsObject);
        if (snapshot == null)
            return 0;

        var snapshotVtbl = _vphysics.PhysicsFrictionSnapshot;
        int count = 0;
        while (snapshotVtbl->IsValid(snapshot) != 0)
        {
            nint touching = (nint)snapshotVtbl->GetObject(snapshot, 1);
            if (ownerOf(touching) == other)
                count++;
            snapshotVtbl->NextFrictionData(snapshot);
        }

        objectVtbl->DestroyFrictionSnapshot((void*)physicsObject, snapshot);
        return count;
    }

    public Vector ContactPoint(nint collisionData)
    {
        if (collisionData == 0)
            return Vector.Zero;

        Vector point;
        var vtbl = _vphysics.PhysicsCollisionData;
        vtbl->GetContactPoint((void*)collisionData, &point);
        return point;
    }

    public (Vector Linear, Vector Angular) Velocity(nint physicsObject)
    {
        if (physicsObject == 0)
            return (Vector.Zero, Vector.Zero);

        Vector linear, angular;
        var vtbl = _vphysics.PhysicsObject;
        vtbl->GetVelocity((void*)physicsObject, &linear, &angular);
        return (linear, angular);
    }

    public void AddVelocity(nint physicsObject, Vector linear, Vector angular)
    {
        if (physicsObject == 0)
            return;

        var vtbl = _vphysics.PhysicsObject;
        vtbl->AddVelocity((void*)physicsObject, &linear, &angular);
    }

    public float LinearDrag(nint physicsObject, Vector direction)
        => physicsObject == 0 ? 0f : (_vphysics.PhysicsObject)->CalculateLinearDrag((void*)physicsObject, &direction);

    public float AngularDrag(nint physicsObject, Vector axis)
        => physicsObject == 0 ? 0f : (_vphysics.PhysicsObject)->CalculateAngularDrag((void*)physicsObject, &axis);

    public float Mass(nint physicsObject)
        => physicsObject == 0 ? 0f : (_vphysics.PhysicsObject)->GetMass((void*)physicsObject);

    public ushort GameFlags(nint physicsObject)
        => physicsObject == 0 ? (ushort)0 : (_vphysics.PhysicsObject)->GetGameFlags((void*)physicsObject);

    public int MaterialIndex(nint physicsObject)
        => physicsObject == 0 ? 0 : (_vphysics.PhysicsObject)->GetMaterialIndex((void*)physicsObject);

    public (Vector X, Vector Y, Vector Z) Axes(nint physicsObject)
    {
        if (physicsObject == 0)
            return (Vector.Zero, Vector.Zero, Vector.Zero);

        // matrix3x4_t: three rows of four floats; the axes are its columns.
        float* m = stackalloc float[12];
        var vtbl = _vphysics.PhysicsObject;
        vtbl->GetPositionMatrix((void*)physicsObject, m);
        return (new Vector(m[0], m[4], m[8]), new Vector(m[1], m[5], m[9]), new Vector(m[2], m[6], m[10]));
    }

    public void SetVelocity(nint physicsObject, Vector linear, Vector angular)
    {
        if (physicsObject == 0)
            return;

        var vtbl = _vphysics.PhysicsObject;
        vtbl->SetVelocity((void*)physicsObject, &linear, &angular);
    }

    public bool IsStatic(nint physicsObject)
        => physicsObject != 0 && (_vphysics.PhysicsObject)->IsStatic((void*)physicsObject) != 0;

    public bool IsMoveable(nint physicsObject)
        => physicsObject != 0 && (_vphysics.PhysicsObject)->IsMoveable((void*)physicsObject) != 0;

    public ushort CallbackFlags(nint physicsObject)
        => physicsObject == 0 ? (ushort)0 : (_vphysics.PhysicsObject)->GetCallbackFlags((void*)physicsObject);

    public Vector SurfaceNormal(nint collisionData)
    {
        if (collisionData == 0)
            return Vector.Zero;

        Vector normal;
        var vtbl = _vphysics.PhysicsCollisionData;
        vtbl->GetSurfaceNormal((void*)collisionData, &normal);
        return normal;
    }

    public Vector VelocityAtPoint(nint physicsObject, Vector worldPoint)
    {
        if (physicsObject == 0)
            return Vector.Zero;

        Vector velocity;
        var vtbl = _vphysics.PhysicsObject;
        vtbl->GetVelocityAtPoint((void*)physicsObject, &worldPoint, &velocity);
        return velocity;
    }
}

/// <summary>The level's <c>IPhysicsEnvironment</c>, called by its generated vtable.</summary>
public interface IVPhysicsEnvironment
{
    /// <summary><c>IPhysicsEnvironment::SetObjectEventHandler</c>.</summary>
    void SetObjectEventHandler(nint environment, nint handler);

    /// <summary><c>IPhysicsEnvironment::SetCollisionEventHandler</c>.</summary>
    void SetCollisionEventHandler(nint environment, nint handler);

    /// <summary><c>IPhysicsEnvironment::SetCollisionSolver</c>.</summary>
    void SetCollisionSolver(nint environment, nint handler);

    /// <summary><c>IPhysicsEnvironment::GetSimulationTimestep</c>.</summary>
    float SimulationTimestep(nint environment);

    /// <summary><c>IPhysicsEnvironment::SetConstraintEventHandler</c>.</summary>
    void SetConstraintEventHandler(nint environment, nint handler);
}

public sealed unsafe class VPhysicsEnvironment : IVPhysicsEnvironment
{
    private readonly VPhysicsTables _vphysics;

    /// <summary>Over <paramref name="vphysics"/>, its side's vphysics forwarder tables.</summary>
    public VPhysicsEnvironment(VPhysicsTables vphysics)
        => _vphysics = vphysics ?? throw new ArgumentNullException(nameof(vphysics));

    public void SetObjectEventHandler(nint environment, nint handler)
    {
        if (environment == 0)
            return;

        var vtbl = _vphysics.PhysicsEnvironment;
        vtbl->SetObjectEventHandler((void*)environment, (void*)handler);
    }

    public float SimulationTimestep(nint environment)
        => environment == 0 ? 0f : (_vphysics.PhysicsEnvironment)->GetSimulationTimestep((void*)environment);

    public void SetCollisionSolver(nint environment, nint handler)
    {
        if (environment == 0)
            return;

        var vtbl = _vphysics.PhysicsEnvironment;
        vtbl->SetCollisionSolver((void*)environment, (void*)handler);
    }

    public void SetCollisionEventHandler(nint environment, nint handler)
    {
        if (environment == 0)
            return;

        var vtbl = _vphysics.PhysicsEnvironment;
        vtbl->SetCollisionEventHandler((void*)environment, (void*)handler);
    }

    public void SetConstraintEventHandler(nint environment, nint handler)
    {
        if (environment == 0)
            return;

        var vtbl = _vphysics.PhysicsEnvironment;
        vtbl->SetConstraintEventHandler((void*)environment, (void*)handler);
    }
}
