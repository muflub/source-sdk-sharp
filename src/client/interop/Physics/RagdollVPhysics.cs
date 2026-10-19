using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="IRagdollVPhysics"/> over the real engine (plan_physics_bones_native_zero
/// Phase 3): every call through the generated forwarders
/// (plan_engine_forwarders.md) -- IPhysics', IPhysicsCollision's and
/// IPhysicsSurfaceProps' own, IVModelInfo's (<see cref="ModelInfoFwd"/>) and,
/// for the objects, the side's <see cref="VPhysicsTables"/>. Holds the factory interfaces it acquired, the
/// two key-handler tables managed built, and one native string; the bodies'
/// pointers are its caller's.
/// </summary>
public sealed unsafe class RagdollVPhysics : IRagdollVPhysics, IDisposable
{
    private readonly IClientPhysicsEngine _environment;
    private readonly void* _physics;
    private readonly PhysicsFwd* _physicsFwd;
    private readonly void* _collision;
    private readonly PhysicsCollisionFwd* _collisionFwd;
    private readonly void* _props;
    private readonly PhysicsSurfacePropsFwd* _propsFwd;
    private readonly void* _modelInfo;
    private readonly ModelInfoFwd* _modelInfoFwd;
    private readonly VPhysicsTables _vphysics;

    // "DEFAULT", the name g_PhysDefaultObjectParams carries. A solid's name is
    // always replaced by the model's before an object is made, so vphysics
    // never keeps this one.
    private byte* _defaultName;

    private RagdollKeyHandlerBinding? _keys;
    private ManagedVtable? _keyTable;

    /// <summary>
    /// The client's one key-handler table, once <see cref="InstallHandlers"/>
    /// has built it: the world's physics build parses its solids with it too
    /// (plan_physics_bones_native_zero 2.6).
    /// </summary>
    public RagdollKeyHandlerBinding? Keys => _keys;

    private RagdollVPhysics(IClientPhysicsEngine environment, void* physics, PhysicsFwd* physicsFwd, void* collision,
                            PhysicsCollisionFwd* collisionFwd, void* props, PhysicsSurfacePropsFwd* propsFwd,
                            void* modelInfo, ModelInfoFwd* modelInfoFwd, VPhysicsTables vphysics)
    {
        _environment = environment;
        _physics = physics;
        _physicsFwd = physicsFwd;
        _collision = collision;
        _collisionFwd = collisionFwd;
        _props = props;
        _propsFwd = propsFwd;
        _modelInfo = modelInfo;
        _modelInfoFwd = modelInfoFwd;
        _vphysics = vphysics;

        _defaultName = (byte*)NativeMemory.AllocZeroed(8);
        "DEFAULT"u8.CopyTo(new Span<byte>(_defaultName, 8));
    }

    /// <summary>The four factory interfaces, from <paramref name="engine"/>'s own side; null with the reason when one does not answer.</summary>
    public static RagdollVPhysics? Acquire(ModuleEngine engine, IClientPhysicsEngine environment, out string why)
    {
        NativeInterfaceFactory factory = (engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces;

        if (!Forwarders.TryAcquire<PhysicsFwd>(factory, SourceSharpFactoryType.Physics, "VPhysics031",
                                               out void* physics, out PhysicsFwd* physicsFwd, out why)
            || !Forwarders.TryAcquire<PhysicsCollisionFwd>(factory, SourceSharpFactoryType.Physics, "VPhysicsCollision007",
                                                           out void* collision, out PhysicsCollisionFwd* collisionFwd, out why)
            || !Forwarders.TryAcquire<PhysicsSurfacePropsFwd>(factory, SourceSharpFactoryType.Physics,
                                                              PhysicsSurfaceProps.InterfaceVersion,
                                                              out void* props, out PhysicsSurfacePropsFwd* propsFwd, out why)
            || !Forwarders.TryAcquire<ModelInfoFwd>(factory, SourceSharpFactoryType.AppSystem,
                                                 EngineModelInfo.ClientInterfaceVersion,
                                                 out void* modelInfo, out ModelInfoFwd* modelInfoFwd, out why))
        {
            return null;
        }

        return new RagdollVPhysics(environment, physics, physicsFwd, collision, collisionFwd, props, propsFwd,
                                   modelInfo, modelInfoFwd, engine.Api.VPhysics);
    }

    /// <summary>
    /// The key-handler table, built in the side's registry: the solids'
    /// defaults (stock's <c>g_SolidSetup</c>) and the custom blocks' router, in
    /// one table (a registry holds one per contract).
    /// </summary>
    public void InstallHandlers(ManagedVtables vtables, Crossings crossings, PhysicsCallbackObjects objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        _keys = new RagdollKeyHandlerBinding(crossings, (nint)_defaultName);
        _keyTable = RagdollKeyHandlerBinding.Create(vtables, _keys);
        // The IVPhysicsKeyHandler vphysics is handed: libss_fwd_client's object,
        // built in the block (plan_forwarders_everywhere.md phase 5).
        _keys.Table = objects.KeyHandler(_keyTable);
    }

    public void Dispose()
    {
        if (_defaultName != null)
        {
            NativeMemory.Free(_defaultName);
            _defaultName = null;
        }
    }

    private PhysicsEnvironmentFwd* Env(nint environment) => _vphysics.PhysicsEnvironment;

    private PhysicsObjectFwd* Obj(nint physicsObject) => _vphysics.PhysicsObject;

    private VPhysicsKeyParserFwd* Parser(nint parser) => _vphysics.VPhysicsKeyParser;

    private PhysicsCollisionSetFwd* Set(nint set) => _vphysics.PhysicsCollisionSet;

    private PhysicsConstraintGroupFwd* Group(nint group) => _vphysics.PhysicsConstraintGroup;

    private PhysicsFrictionSnapshotFwd* Snapshot(nint snapshot) => _vphysics.PhysicsFrictionSnapshot;

    // ---- the level -----------------------------------------------------------

    public nint Environment() => _environment.Environment();

    // ---- the model -----------------------------------------------------------

    public RagdollCollide Collide(int modelIndex)
    {
        if (modelIndex <= 0)
            return default;

        byte* collide = (byte*)_modelInfoFwd->GetVCollide_2(_modelInfo, modelIndex);

        if (collide == null)
            return default;

        int solidCount = *(ushort*)(collide + VCollideFields.SolidCountAndPacked) & VCollideFields.SolidCountMask;
        return new RagdollCollide((nint)collide, solidCount, *(nint*)(collide + VCollideFields.KeyValues));
    }

    public nint CollideSolid(in RagdollCollide collide, int index)
    {
        if (collide.Pointer == 0 || index < 0 || index >= collide.SolidCount)
            return 0;

        nint* solids = *(nint**)((byte*)collide.Pointer + VCollideFields.Solids);
        return solids == null ? 0 : solids[index];
    }

    public bool ModelBounds(int modelIndex, out Vector mins, out Vector maxs)
    {
        mins = Vector.Zero;
        maxs = Vector.Zero;

        void* model = modelIndex > 0 ? _modelInfoFwd->GetModel(_modelInfo, modelIndex) : null;

        if (model == null)
            return false;

        Vector lo, hi;
        _modelInfoFwd->GetModelBounds(_modelInfo, model, &lo, &hi);
        mins = lo;
        maxs = hi;
        return true;
    }

    public int SurfaceIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _propsFwd->GetSurfaceIndex(_props, p);
    }

    // ---- the key parser ------------------------------------------------------

    public nint KeyParserCreate(nint keyValues)
        => keyValues == 0 ? 0 : (nint)_collisionFwd->VPhysicsKeyParserCreate(_collision, (byte*)keyValues);

    public void KeyParserDestroy(nint parser)
    {
        if (parser != 0)
            _collisionFwd->VPhysicsKeyParserDestroy(_collision, (void*)parser);
    }

    public bool Finished(nint parser) => parser == 0 || Parser(parser)->Finished((void*)parser) != 0;

    // A block name is a short token.
    public string BlockName(nint parser)
        => Vtable.TextOf(Parser(parser)->GetCurrentBlockName((void*)parser), 256) ?? string.Empty;

    public RagdollSolid ParseSolid(nint parser)
    {
        byte* solid = (byte*)NativeMemory.AllocZeroed(SolidLayout.Size);

        if (_keys is null || _keyTable is null)
        {
            // No table (the root was never built): the solid keeps vphysics'
            // own defaults rather than a handler that is not there.
            Parser(parser)->ParseSolid((void*)parser, solid, null);
        }
        else
        {
            _keys.SolidInFlight = true;

            try
            {
                Parser(parser)->ParseSolid((void*)parser, solid, (void*)_keys.Table);
            }
            finally
            {
                _keys.SolidInFlight = false;
            }
        }

        return new RagdollSolid(*(int*)(solid + SolidLayout.Index),
                                Vtable.TextOf(solid + SolidLayout.Name, SolidLayout.NameCapacity) ?? string.Empty,
                                Vtable.TextOf(solid + SolidLayout.SurfaceProp, SolidLayout.NameCapacity) ?? string.Empty,
                                (nint)solid);
    }

    public void ReleaseSolid(in RagdollSolid solid)
    {
        if (solid.Native != 0)
            NativeMemory.Free((void*)solid.Native);
    }

    public ConstraintRagdollParams ParseRagdollConstraint(nint parser)
    {
        ConstraintRagdollParams constraint = default;
        Parser(parser)->ParseRagdollConstraint((void*)parser, &constraint, null);
        return constraint;
    }

    public void ParseCustom(nint parser, IRagdollKeySink sink)
    {
        if (_keys is null || _keyTable is null)
        {
            // No table to hand vphysics (the root was never built): the block
            // is passed over rather than parsed with a handler that is not there.
            Parser(parser)->SkipBlock((void*)parser);
            return;
        }

        _keys.Current = sink;

        try
        {
            Parser(parser)->ParseCustom((void*)parser, (void*)_keys.Table, (void*)_keys.Table);
        }
        finally
        {
            _keys.Current = null;
        }
    }

    public void SkipBlock(nint parser) => Parser(parser)->SkipBlock((void*)parser);

    // ---- collision sets ------------------------------------------------------

    public nint FindCollisionSet(int modelIndex) => (nint)_physicsFwd->FindCollisionSet(_physics, unchecked((uint)modelIndex));

    public nint FindOrCreateCollisionSet(int modelIndex, int maxElementCount)
        => (nint)_physicsFwd->FindOrCreateCollisionSet(_physics, unchecked((uint)modelIndex), maxElementCount);

    public void EnableCollisions(nint collisionSet, int index0, int index1)
    {
        if (collisionSet != 0)
            Set(collisionSet)->EnableCollisions((void*)collisionSet, index0, index1);
    }

    public void DisableCollisions(nint collisionSet, int index0, int index1)
    {
        if (collisionSet != 0)
            Set(collisionSet)->DisableCollisions((void*)collisionSet, index0, index1);
    }

    // ---- creators ------------------------------------------------------------

    public nint CreateConstraintGroup(nint environment, in ConstraintGroupParams p)
    {
        if (environment == 0)
            return 0;

        fixed (ConstraintGroupParams* block = &p)
            return (nint)Env(environment)->CreateConstraintGroup((void*)environment, block);
    }

    public void DestroyConstraintGroup(nint environment, nint group)
    {
        if (environment != 0 && group != 0)
            Env(environment)->DestroyConstraintGroup((void*)environment, (void*)group);
    }

    public nint CreatePolyObject(nint environment, nint collide, int surfaceIndex, in RagdollSolid solid,
                                 in RagdollObjectOverrides overrides)
    {
        if (environment == 0 || collide == 0 || solid.Native == 0)
            return 0;

        var p = (ObjectParams*)((byte*)solid.Native + SolidLayout.Params);

        if (overrides.Mass is float mass)
            p->Mass = mass;

        p->RotInertiaLimit = overrides.RotInertiaLimit;
        p->GameData = overrides.GameData;
        p->Name = overrides.Name;

        Vector origin = Vector.Zero;
        QAngle angles = QAngle.Zero;
        return (nint)Env(environment)->CreatePolyObject((void*)environment, (void*)collide, surfaceIndex, &origin,
                                                       &angles, p);
    }

    public nint CreateRagdollConstraint(nint environment, nint reference, nint attached, nint group,
                                        in ConstraintRagdollParams p)
    {
        if (environment == 0)
            return 0;

        fixed (ConstraintRagdollParams* block = &p)
            return (nint)Env(environment)->CreateRagdollConstraint((void*)environment, (void*)reference,
                                                                  (void*)attached, (void*)group, block);
    }

    public nint CreateFixedConstraint(nint environment, nint reference, nint attached, nint group,
                                      in ConstraintFixedParams p)
    {
        if (environment == 0)
            return 0;

        fixed (ConstraintFixedParams* block = &p)
            return (nint)Env(environment)->CreateFixedConstraint((void*)environment, (void*)reference,
                                                                (void*)attached, (void*)group, block);
    }

    public nint CreateBallsocketConstraint(nint environment, nint reference, nint attached, nint group,
                                           in ConstraintBallsocketParams p)
    {
        if (environment == 0)
            return 0;

        fixed (ConstraintBallsocketParams* block = &p)
            return (nint)Env(environment)->CreateBallsocketConstraint((void*)environment, (void*)reference,
                                                                     (void*)attached, (void*)group, block);
    }

    public void DestroyConstraint(nint environment, nint constraint)
    {
        if (environment != 0 && constraint != 0)
            Env(environment)->DestroyConstraint((void*)environment, (void*)constraint);
    }

    public void DestroyObject(nint environment, nint physicsObject)
    {
        if (environment != 0 && physicsObject != 0)
            Env(environment)->DestroyObject((void*)environment, (void*)physicsObject);
    }

    public int ActiveObjects(nint environment, ref nint[] buffer)
    {
        if (environment == 0)
            return 0;

        int count = Env(environment)->GetActiveObjectCount((void*)environment);

        if (count <= 0)
            return 0;

        if (buffer.Length < count)
            buffer = new nint[Math.Max(count, buffer.Length * 2)];

        fixed (nint* list = buffer)
            Env(environment)->GetActiveObjects((void*)environment, list);

        return count;
    }

    // ---- group ---------------------------------------------------------------

    public void GroupActivate(nint group)
    {
        if (group != 0)
            Group(group)->Activate((void*)group);
    }

    public bool GroupIsInErrorState(nint group) => group != 0 && Group(group)->IsInErrorState((void*)group) != 0;

    public void GroupClearErrorState(nint group)
    {
        if (group != 0)
            Group(group)->ClearErrorState((void*)group);
    }

    // ---- objects -------------------------------------------------------------

    public float Mass(nint physicsObject) => physicsObject == 0 ? 0f : Obj(physicsObject)->GetMass((void*)physicsObject);

    public void SetMass(nint physicsObject, float mass)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->SetMass((void*)physicsObject, mass);
    }

    public Vector Position(nint physicsObject)
    {
        if (physicsObject == 0)
            return Vector.Zero;

        Vector position;
        Obj(physicsObject)->GetPosition((void*)physicsObject, &position, null);
        return position;
    }

    public (Vector Position, QAngle Angles) PositionAngles(nint physicsObject)
    {
        if (physicsObject == 0)
            return (Vector.Zero, QAngle.Zero);

        Vector position;
        QAngle angles;
        Obj(physicsObject)->GetPosition((void*)physicsObject, &position, &angles);
        return (position, angles);
    }

    public Float3x4 PositionMatrix(nint physicsObject)
    {
        Float3x4 matrix = Float3x4.Identity;

        if (physicsObject != 0)
            Obj(physicsObject)->GetPositionMatrix((void*)physicsObject, &matrix);

        return matrix;
    }

    public void SetPositionMatrix(nint physicsObject, in Float3x4 matrix, bool isTeleport)
    {
        if (physicsObject == 0)
            return;

        fixed (Float3x4* m = &matrix)
            Obj(physicsObject)->SetPositionMatrix((void*)physicsObject, m, isTeleport ? (byte)1 : (byte)0);
    }

    public void SetGameIndex(nint physicsObject, ushort index)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->SetGameIndex((void*)physicsObject, index);
    }

    public ushort GameFlags(nint physicsObject)
        => physicsObject == 0 ? (ushort)0 : Obj(physicsObject)->GetGameFlags((void*)physicsObject);

    public void SetGameFlags(nint physicsObject, ushort flags)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->SetGameFlags((void*)physicsObject, flags);
    }

    public ushort CallbackFlags(nint physicsObject)
        => physicsObject == 0 ? (ushort)0 : Obj(physicsObject)->GetCallbackFlags((void*)physicsObject);

    public void SetCallbackFlags(nint physicsObject, ushort flags)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->SetCallbackFlags((void*)physicsObject, flags);
    }

    public nint GameData(nint physicsObject)
        => physicsObject == 0 ? 0 : (nint)Obj(physicsObject)->GetGameData((void*)physicsObject);

    public void EnableObjectCollisions(nint physicsObject, bool enable)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->EnableCollisions((void*)physicsObject, enable ? (byte)1 : (byte)0);
    }

    public void Wake(nint physicsObject)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->Wake((void*)physicsObject);
    }

    public void Sleep(nint physicsObject)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->Sleep((void*)physicsObject);
    }

    public bool IsAsleep(nint physicsObject)
        => physicsObject != 0 && Obj(physicsObject)->IsAsleep((void*)physicsObject) != 0;

    public void ApplyForceCenter(nint physicsObject, Vector force)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->ApplyForceCenter((void*)physicsObject, &force);
    }

    public void ApplyForceOffset(nint physicsObject, Vector force, Vector worldPosition)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->ApplyForceOffset((void*)physicsObject, &force, &worldPosition);
    }

    public Vector LocalToWorld(nint physicsObject, Vector local)
    {
        Vector world = Vector.Zero;

        if (physicsObject != 0)
            Obj(physicsObject)->LocalToWorld((void*)physicsObject, &world, &local);

        return world;
    }

    public Vector LocalToWorldVector(nint physicsObject, Vector local)
    {
        Vector world = Vector.Zero;

        if (physicsObject != 0)
            Obj(physicsObject)->LocalToWorldVector((void*)physicsObject, &world, &local);

        return world;
    }

    public Vector WorldToLocal(nint physicsObject, Vector world)
    {
        Vector local = Vector.Zero;

        if (physicsObject != 0)
            Obj(physicsObject)->WorldToLocal((void*)physicsObject, &local, &world);

        return local;
    }

    public Vector WorldToLocalVector(nint physicsObject, Vector world)
    {
        Vector local = Vector.Zero;

        if (physicsObject != 0)
            Obj(physicsObject)->WorldToLocalVector((void*)physicsObject, &local, &world);

        return local;
    }

    public void AddVelocity(nint physicsObject, Vector linear, Vector angular)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->AddVelocity((void*)physicsObject, &linear, &angular);
    }

    public void SetVelocity(nint physicsObject, Vector linear, Vector angular)
    {
        if (physicsObject != 0)
            Obj(physicsObject)->SetVelocity((void*)physicsObject, &linear, &angular);
    }

    public Vector VelocityAtPoint(nint physicsObject, Vector worldPosition)
    {
        Vector velocity = Vector.Zero;

        if (physicsObject != 0)
            Obj(physicsObject)->GetVelocityAtPoint((void*)physicsObject, &worldPosition, &velocity);

        return velocity;
    }

    public nint ObjectCollide(nint physicsObject)
        => physicsObject == 0 ? 0 : (nint)Obj(physicsObject)->GetCollide((void*)physicsObject);

    public (Vector Mins, Vector Maxs) CollideAabb(nint collide, Vector origin, QAngle angles)
    {
        Vector mins = Vector.Zero, maxs = Vector.Zero;

        if (collide != 0)
            _collisionFwd->CollideGetAABB(_collision, &mins, &maxs, (void*)collide, &origin, &angles);

        return (mins, maxs);
    }

    // ---- snapshots -----------------------------------------------------------

    public nint CreateFrictionSnapshot(nint physicsObject)
        => physicsObject == 0 ? 0 : (nint)Obj(physicsObject)->CreateFrictionSnapshot((void*)physicsObject);

    public void DestroyFrictionSnapshot(nint physicsObject, nint snapshot)
    {
        if (physicsObject != 0 && snapshot != 0)
            Obj(physicsObject)->DestroyFrictionSnapshot((void*)physicsObject, (void*)snapshot);
    }

    public bool SnapshotIsValid(nint snapshot) => snapshot != 0 && Snapshot(snapshot)->IsValid((void*)snapshot) != 0;

    public nint SnapshotObject(nint snapshot, int index)
        => snapshot == 0 ? 0 : (nint)Snapshot(snapshot)->GetObject((void*)snapshot, index);

    public Vector SnapshotSurfaceNormal(nint snapshot)
    {
        Vector normal = Vector.Zero;

        if (snapshot != 0)
            Snapshot(snapshot)->GetSurfaceNormal((void*)snapshot, &normal);

        return normal;
    }

    public void SnapshotNext(nint snapshot)
    {
        if (snapshot != 0)
            Snapshot(snapshot)->NextFrictionData((void*)snapshot);
    }

    public void SnapshotClearFrictionForce(nint snapshot)
    {
        if (snapshot != 0)
            Snapshot(snapshot)->ClearFrictionForce((void*)snapshot);
    }

    public void SnapshotRecomputeFriction(nint snapshot)
    {
        if (snapshot != 0)
            Snapshot(snapshot)->RecomputeFriction((void*)snapshot);
    }
}
