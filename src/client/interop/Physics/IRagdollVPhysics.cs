using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <c>modelinfo-&gt;GetVCollide( modelIndex )</c>, as a ragdoll reads it: the
/// <c>vcollide_t</c> itself, its solid count and its key-value text. A default
/// value is "the model has no collide".
/// </summary>
public readonly record struct RagdollCollide(nint Pointer, int SolidCount, nint KeyValues)
{
    public bool IsNone => Pointer == 0;
}

/// <summary>
/// One <c>solid</c> block, as <c>IVPhysicsKeyParser::ParseSolid</c> wrote it
/// into a <c>solid_t</c> (with the solid-defaults key handler, stock's
/// <c>g_SolidSetup</c>). <see cref="Native"/> is that <c>solid_t</c>: its
/// <c>params</c> are what <see cref="IRagdollVPhysics.CreatePolyObject"/>
/// hands vphysics, and it is released by <see cref="IRagdollVPhysics.ReleaseSolid"/>.
/// </summary>
public readonly record struct RagdollSolid(int Index, string Name, string SurfaceProp, nint Native);

/// <summary>
/// What <c>RagdollAddSolid</c> writes into a solid's <c>params</c> before the
/// object is made: the mass when the body is a statue (<c>fixedConstraints</c>),
/// the rotational inertia limit, the owner's game data word and the model's
/// name (<c>pName</c>, which vphysics keeps and so must outlive the object).
/// </summary>
public readonly record struct RagdollObjectOverrides(float? Mass, float RotInertiaLimit, nint GameData, nint Name);

/// <summary>
/// A custom block's keys, as <c>IVPhysicsKeyParser::ParseCustom</c> hands them
/// to an <c>IVPhysicsKeyHandler</c>'s <c>ParseKeyValue</c>. The parser lowers
/// every key and value before it hands them over.
/// </summary>
public interface IRagdollKeySink
{
    void ParseKeyValue(string key, string value);
}

/// <summary>
/// Every vphysics, model-info and surface-property call a client ragdoll makes
/// (plan_physics_bones_native_zero Phase 3), by pointer: the managed half of
/// stock's ragdoll_shared.cpp and ragdoll.cpp decides, and these carry it out
/// through the generated vtables. A 0 pointer is a no-op answering zero, as
/// stock's NULL checks are.
/// </summary>
public interface IRagdollVPhysics
{
    // ---- the level -----------------------------------------------------------

    /// <summary>The level's <c>IPhysicsEnvironment</c> (<c>physenv</c>), or 0 before one exists.</summary>
    nint Environment();

    // ---- the model -----------------------------------------------------------

    /// <summary><c>modelinfo-&gt;GetVCollide( modelIndex )</c>.</summary>
    RagdollCollide Collide(int modelIndex);

    /// <summary><c>pCollide-&gt;solids[ index ]</c>.</summary>
    nint CollideSolid(in RagdollCollide collide, int index);

    /// <summary><c>modelinfo-&gt;GetModelBounds( GetModel( modelIndex ), mins, maxs )</c>; false with no model.</summary>
    bool ModelBounds(int modelIndex, out Vector mins, out Vector maxs);

    /// <summary><c>physprops-&gt;GetSurfaceIndex( name )</c>: -1 for a name the table lacks.</summary>
    int SurfaceIndex(string name);

    // ---- the key parser (IPhysicsCollision / IVPhysicsKeyParser) -------------

    nint KeyParserCreate(nint keyValues);

    void KeyParserDestroy(nint parser);

    bool Finished(nint parser);

    string BlockName(nint parser);

    /// <summary><c>ParseSolid( &amp;solid, &amp;g_SolidSetup )</c>, into a <c>solid_t</c> the caller releases.</summary>
    RagdollSolid ParseSolid(nint parser);

    void ReleaseSolid(in RagdollSolid solid);

    /// <summary><c>ParseRagdollConstraint( &amp;constraint, NULL )</c>.</summary>
    ConstraintRagdollParams ParseRagdollConstraint(nint parser);

    /// <summary><c>ParseCustom( pCustom, handler )</c>, the handler being <paramref name="sink"/>.</summary>
    void ParseCustom(nint parser, IRagdollKeySink sink);

    void SkipBlock(nint parser);

    // ---- collision sets (IPhysics / IPhysicsCollisionSet) ---------------------

    nint FindCollisionSet(int modelIndex);

    nint FindOrCreateCollisionSet(int modelIndex, int maxElementCount);

    void EnableCollisions(nint collisionSet, int index0, int index1);

    void DisableCollisions(nint collisionSet, int index0, int index1);

    // ---- the environment's creators -----------------------------------------

    nint CreateConstraintGroup(nint environment, in ConstraintGroupParams p);

    void DestroyConstraintGroup(nint environment, nint group);

    /// <summary>
    /// <c>CreatePolyObject( collide, surfaceIndex, vec3_origin, vec3_angle, &amp;solid.params )</c>,
    /// after writing <paramref name="overrides"/> into those params.
    /// </summary>
    nint CreatePolyObject(nint environment, nint collide, int surfaceIndex, in RagdollSolid solid,
                          in RagdollObjectOverrides overrides);

    nint CreateRagdollConstraint(nint environment, nint reference, nint attached, nint group,
                                 in ConstraintRagdollParams p);

    nint CreateFixedConstraint(nint environment, nint reference, nint attached, nint group,
                               in ConstraintFixedParams p);

    void DestroyConstraint(nint environment, nint constraint);

    void DestroyObject(nint environment, nint physicsObject);

    /// <summary>
    /// <c>GetActiveObjectCount</c> then <c>GetActiveObjects</c>: the objects the
    /// last step moved, written into <paramref name="buffer"/>, which is grown
    /// (never shrunk) when it is short, so a caller that keeps it allocates
    /// nothing in a steady state. The count written.
    /// </summary>
    int ActiveObjects(nint environment, ref nint[] buffer);

    // ---- the constraint group (IPhysicsConstraintGroup) -----------------------

    void GroupActivate(nint group);

    bool GroupIsInErrorState(nint group);

    void GroupClearErrorState(nint group);

    // ---- one element (IPhysicsObject) ----------------------------------------

    float Mass(nint physicsObject);

    Vector Position(nint physicsObject);

    (Vector Position, QAngle Angles) PositionAngles(nint physicsObject);

    Float3x4 PositionMatrix(nint physicsObject);

    void SetPositionMatrix(nint physicsObject, in Float3x4 matrix, bool isTeleport);

    void SetGameIndex(nint physicsObject, ushort index);

    ushort GameFlags(nint physicsObject);

    void SetGameFlags(nint physicsObject, ushort flags);

    nint GameData(nint physicsObject);

    void EnableObjectCollisions(nint physicsObject, bool enable);

    void Wake(nint physicsObject);

    void Sleep(nint physicsObject);

    bool IsAsleep(nint physicsObject);

    void ApplyForceCenter(nint physicsObject, Vector force);

    void ApplyForceOffset(nint physicsObject, Vector force, Vector worldPosition);

    Vector LocalToWorld(nint physicsObject, Vector local);

    Vector LocalToWorldVector(nint physicsObject, Vector local);

    Vector WorldToLocalVector(nint physicsObject, Vector world);

    void AddVelocity(nint physicsObject, Vector linear, Vector angular);

    void SetVelocity(nint physicsObject, Vector linear, Vector angular);

    Vector VelocityAtPoint(nint physicsObject, Vector worldPosition);

    /// <summary><c>GetCollide</c>: the element's <c>CPhysCollide</c>.</summary>
    nint ObjectCollide(nint physicsObject);

    /// <summary><c>physcollision-&gt;CollideGetAABB( &amp;mins, &amp;maxs, collide, origin, angles )</c>.</summary>
    (Vector Mins, Vector Maxs) CollideAabb(nint collide, Vector origin, QAngle angles);

    // ---- contacts (IPhysicsFrictionSnapshot) ---------------------------------

    nint CreateFrictionSnapshot(nint physicsObject);

    void DestroyFrictionSnapshot(nint physicsObject, nint snapshot);

    bool SnapshotIsValid(nint snapshot);

    nint SnapshotObject(nint snapshot, int index);

    Vector SnapshotSurfaceNormal(nint snapshot);

    void SnapshotNext(nint snapshot);

    void SnapshotClearFrictionForce(nint snapshot);

    void SnapshotRecomputeFriction(nint snapshot);
}
