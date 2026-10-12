//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Client.Input;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Bones;
using NVector = System.Numerics.Vector3;

namespace SourceSharp.Client.Ragdolls;

/// <summary>
/// game/shared/ragdoll_shared.cpp and the body half of game/client/ragdoll.cpp
/// (<c>CRagdoll</c>), in managed code (plan_physics_bones_native_zero Phase 3).
/// Holds nothing but the services it is handed: every body is a
/// <see cref="RagdollBody"/> its caller owns, and every vphysics call goes
/// through <see cref="IRagdollVPhysics"/>, by pointer.
/// </summary>
/// <remarks>
/// What stock had and this does not: the save/restore datadesc and
/// <c>g_pPhysSaveRestoreManager</c>'s model association (this client has no
/// physics save/restore; the native library never called it either),
/// <c>DrawWireframe</c> and <c>RAGDOLL_VISUALIZE</c> (no caller), and
/// <c>TransformVectorToWorld</c> (no caller).
/// </remarks>
public sealed class RagdollSolver(IRagdollVPhysics vphysics, ILog log, Func<int> developer)
{
    /// <summary>vphysics_interface.h's <c>FVPHYSICS_PART_OF_RAGDOLL</c>.</summary>
    public const ushort PartOfRagdoll = 0x0008;

    /// <summary>vphysics_interface.h's <c>FVPHYSICS_MULTIOBJECT_ENTITY</c>.</summary>
    public const ushort MultiObjectEntity = 0x0010;

    /// <summary>ragdoll.cpp's <c>RAGDOLL_SLEEP_TOLERANCE</c>.</summary>
    public const float SleepTolerance = 1.0f;

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static BoneMatrix3x4 Bone(Float3x4 m) => Unsafe.BitCast<Float3x4, BoneMatrix3x4>(m);

    internal static Float3x4 Float(BoneMatrix3x4 m) => Unsafe.BitCast<BoneMatrix3x4, Float3x4>(m);

    private static Vector V(NVector v) => new(v.X, v.Y, v.Z);

    private static NVector N(Vector v) => new(v.X, v.Y, v.Z);

    private void DevMsg(int level, string text)
    {
        if (developer() >= level)
            log.Msg(text);
    }

    //-------------------------------------------------------------------------
    // RagdollCreate and what it calls.
    //-------------------------------------------------------------------------

    private void AddSolid(nint environment, RagdollBody ragdoll, RagdollParams p, in RagdollSolid solid)
    {
        if (solid.Index < 0 || solid.Index >= p.Collide.SolidCount)
            return;

        int n = ragdoll.ListCount;
        int boneIndex = BoneSetupQuery.BoneIndexByName(p.StudioHdr, solid.Name);
        ragdoll.BoneIndex[n] = boneIndex;

        if (boneIndex < 0)
        {
            log.Msg($"CRagdollProp::CreateObjects:  Couldn't Lookup Bone {solid.Name}\n");
            return;
        }

        int surfaceData = vphysics.SurfaceIndex(solid.SurfaceProp);

        if (surfaceData < 0)
            surfaceData = vphysics.SurfaceIndex("default");

        var overrides = new RagdollObjectOverrides(p.FixedConstraints ? 1000f : null, 0.1f, p.GameData, p.Name);
        nint obj = vphysics.CreatePolyObject(environment, vphysics.CollideSolid(p.Collide, solid.Index), surfaceData,
                                             solid, overrides);

        RagdollElement element = ragdoll.List[n];
        element.Object = obj;
        vphysics.SetPositionMatrix(obj, Float(p.CurrentBones[boneIndex]), true);
        element.ParentIndex = -1;
        vphysics.SetGameIndex(obj, (ushort)n);

        ragdoll.ListCount++;
    }

    private void AddConstraint(nint environment, RagdollBody ragdoll, RagdollParams p, ConstraintRagdollParams constraint)
    {
        if (constraint.ChildIndex == constraint.ParentIndex)
        {
            DevMsg(1, $"Bogus constraint on ragdoll {p.StudioHdr.Name}\n");
            constraint.ChildIndex = -1;
            constraint.ParentIndex = -1;
        }

        if (constraint.ChildIndex < 0 || constraint.ParentIndex < 0)
            return;

        RagdollElement childElement = ragdoll.List[constraint.ChildIndex];
        RagdollElement parentElement = ragdoll.List[constraint.ParentIndex];

        // save parent index
        childElement.ParentIndex = constraint.ParentIndex;

        if (p.JointFrictionScale > 0)
        {
            constraint.Axis0.Torque *= p.JointFrictionScale;
            constraint.Axis1.Torque *= p.JointFrictionScale;
            constraint.Axis2.Torque *= p.JointFrictionScale;
        }

        // This parent/child pair is not usually a parent/child pair in the
        // skeleton: bones in between are often collapsed for simulation, so
        // the transform is computed.
        BoneMatrix3x4 toAttached = BoneSetupAdj.CalcBoneToBoneTransform(
            p.StudioHdr, ragdoll.BoneIndex[constraint.ChildIndex], ragdoll.BoneIndex[constraint.ParentIndex]);
        constraint.ConstraintToAttached = Float(toAttached);
        childElement.OriginParentSpace = V(toAttached.Position);
        constraint.ConstraintToReference = Float3x4.Identity;

        if (p.FixedConstraints)
        {
            // Makes the ragdoll a statue...
            ConstraintFixedParams fixedParams = ConstraintFixedParams.Defaults();
            fixedParams.AttachedRefXform = Float(BuildObjectRelativeXform(childElement.Object, parentElement.Object));
            fixedParams.Constraint = ConstraintBreakableParams.Defaults();
            childElement.Constraint = vphysics.CreateFixedConstraint(environment, childElement.Object, parentElement.Object,
                                                                     ragdoll.Group, fixedParams);
        }
        else
        {
            childElement.Constraint = vphysics.CreateRagdollConstraint(environment, childElement.Object,
                                                                       parentElement.Object, ragdoll.Group, constraint);
        }
    }

    /// <summary>constraints.h's <c>BuildObjectRelativeXform</c>: the input object in the output object's space.</summary>
    private BoneMatrix3x4 BuildObjectRelativeXform(nint outputSpace, nint inputSpace)
    {
        BoneMatrix3x4 outInv = BoneSetupMath.MatrixInvert(Bone(vphysics.PositionMatrix(outputSpace)));
        return BoneSetupMath.ConcatTransforms(outInv, Bone(vphysics.PositionMatrix(inputSpace)));
    }

    private void CreateObjects(nint environment, RagdollBody ragdoll, RagdollParams p)
    {
        ragdoll.ListCount = 0;
        ragdoll.Group = 0;
        ragdoll.AllowStretch = p.AllowStretch;

        foreach (RagdollElement element in ragdoll.List)
            element.Clear();

        ragdoll.AnimFriction = default;

        if (p.Collide.IsNone || p.Collide.SolidCount > RagdollBody.MaxElements)
            return;

        ragdoll.Group = vphysics.CreateConstraintGroup(environment, ConstraintGroupParams.Defaults());

        nint parser = vphysics.KeyParserCreate(p.Collide.KeyValues);

        while (!vphysics.Finished(parser))
        {
            string block = vphysics.BlockName(parser);

            if (Is(block, "solid"))
            {
                RagdollSolid solid = vphysics.ParseSolid(parser);

                try
                {
                    AddSolid(environment, ragdoll, p, solid);
                }
                finally
                {
                    vphysics.ReleaseSolid(solid);
                }
            }
            else if (Is(block, "ragdollconstraint"))
            {
                AddConstraint(environment, ragdoll, p, vphysics.ParseRagdollConstraint(parser));
            }
            else if (Is(block, "collisionrules"))
            {
                nint set = vphysics.FindOrCreateCollisionSet(p.ModelIndex, ragdoll.ListCount);
                vphysics.ParseCustom(parser, new RagdollCollisionRules(vphysics, set));
            }
            else if (Is(block, "animatedfriction"))
            {
                vphysics.ParseCustom(parser, new RagdollAnimatedFrictionKeys(ragdoll));
            }
            else
            {
                vphysics.SkipBlock(parser);
            }
        }

        vphysics.KeyParserDestroy(parser);
    }

    /// <summary><c>RagdollCreate</c>: the elements, their joints, and the throw.</summary>
    public bool Create(RagdollBody ragdoll, RagdollParams p, nint environment)
    {
        CreateObjects(environment, ragdoll, p);

        if (ragdoll.ListCount == 0)
            return false;

        int forceBone = p.ForceBoneIndex;

        float totalMass = 0;
        for (int i = 0; i < ragdoll.ListCount; i++)
            totalMass += vphysics.Mass(ragdoll.List[i].Object);
        totalMass = MathF.Max(totalMass, 1);

        // apply force to the model
        Vector nudgeForce = p.ForceVector;
        Vector forcePosition = p.ForcePosition;

        if (forceBone >= 0 && forceBone < ragdoll.ListCount)
        {
            vphysics.ApplyForceCenter(ragdoll.List[forceBone].Object, nudgeForce);
            forcePosition = vphysics.Position(ragdoll.List[forceBone].Object);
        }

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            nint obj = ragdoll.List[i].Object;
            vphysics.SetGameFlags(obj, (ushort)(vphysics.GameFlags(obj) | PartOfRagdoll));
        }

        if (forcePosition.X != 0f || forcePosition.Y != 0f || forcePosition.Z != 0f)
        {
            for (int i = 0; i < ragdoll.ListCount; i++)
            {
                if (forceBone == i)
                    continue;

                nint obj = ragdoll.List[i].Object;
                float scale = vphysics.Mass(obj) / totalMass;
                vphysics.ApplyForceOffset(obj, scale * nudgeForce, forcePosition);
            }
        }

        return true;
    }

    /// <summary><c>RagdollSetupCollisions</c>: the model's collision set, from its rules or stock's defaults.</summary>
    public void SetupCollisions(RagdollBody ragdoll, in RagdollCollide collide, int modelIndex)
    {
        if (collide.IsNone)
            return;

        nint set = vphysics.FindCollisionSet(modelIndex);
        if (set != 0)
            return;

        set = vphysics.FindOrCreateCollisionSet(modelIndex, ragdoll.ListCount);
        if (set == 0)
            return;

        bool foundRules = false;

        nint parser = vphysics.KeyParserCreate(collide.KeyValues);
        while (!vphysics.Finished(parser))
        {
            if (Is(vphysics.BlockName(parser), "collisionrules"))
            {
                nint rulesSet = vphysics.FindOrCreateCollisionSet(modelIndex, ragdoll.ListCount);
                vphysics.ParseCustom(parser, new RagdollCollisionRules(vphysics, rulesSet));
                foundRules = true;
            }
            else
            {
                vphysics.SkipBlock(parser);
            }
        }
        vphysics.KeyParserDestroy(parser);

        if (foundRules)
            return;

        // The default rules: each piece collides with everything except its
        // immediate parent / constrained object.
        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            for (int j = i + 1; j < ragdoll.ListCount; j++)
                vphysics.EnableCollisions(set, i, j);
        }

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            int parent = ragdoll.List[i].ParentIndex;
            if (parent >= 0)
                vphysics.DisableCollisions(set, i, parent);
        }
    }

    /// <summary><c>RagdollActivate</c>.</summary>
    public void Activate(RagdollBody ragdoll, in RagdollCollide collide, int modelIndex, bool forceWake = true)
    {
        SetupCollisions(ragdoll, collide, modelIndex);

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            nint obj = ragdoll.List[i].Object;
            vphysics.SetGameIndex(obj, (ushort)i);
            vphysics.SetGameFlags(obj, (ushort)(vphysics.GameFlags(obj) | MultiObjectEntity));

            // now that the relationships are set, activate the collision system
            vphysics.EnableObjectCollisions(obj, true);

            if (forceWake)
                vphysics.Wake(obj);
        }

        if (ragdoll.Group == 0)
            return;

        // NOTE: This also wakes the objects
        vphysics.GroupActivate(ragdoll.Group);

        // so if we didn't want that, we'll need to put them back to sleep here
        if (!forceWake)
        {
            for (int i = 0; i < ragdoll.ListCount; i++)
                vphysics.Sleep(ragdoll.List[i].Object);
        }
    }

    /// <summary><c>RagdollDestroy</c>, against <paramref name="environment"/> (stock's <c>physenv</c>).</summary>
    public void Destroy(RagdollBody ragdoll, nint environment)
    {
        if (ragdoll.ListCount == 0)
            return;

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            vphysics.DestroyConstraint(environment, ragdoll.List[i].Constraint);
            ragdoll.List[i].Constraint = 0;
        }

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            if (ragdoll.List[i].Object != 0)
                vphysics.DestroyObject(environment, ragdoll.List[i].Object);

            ragdoll.List[i].Object = 0;
        }

        vphysics.DestroyConstraintGroup(environment, ragdoll.Group);
        ragdoll.Group = 0;
        ragdoll.ListCount = 0;
    }

    /// <summary>
    /// <c>~CRagdoll</c>'s first half: collisions off on every element before
    /// <see cref="Destroy"/>, which might otherwise cause touch callbacks on a
    /// half-deleted body.
    /// </summary>
    public void PrepareForDestroy(RagdollBody ragdoll)
    {
        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            if (ragdoll.List[i].Object != 0)
                vphysics.EnableObjectCollisions(ragdoll.List[i].Object, false);
        }
    }

    //-------------------------------------------------------------------------
    // The body's pose.
    //-------------------------------------------------------------------------

    /// <summary><c>RagdollGetBoneMatrix</c>: one element's matrix into its bone, held rigidly to its parent.</summary>
    public bool GetBoneMatrix(RagdollBody ragdoll, Span<BoneMatrix3x4> boneToWorld, int objectIndex)
    {
        int boneIndex = ragdoll.BoneIndex[objectIndex];
        if (boneIndex < 0)
            return false;

        RagdollElement element = ragdoll.List[objectIndex];

        // during restore if a model has changed since the file was saved, this could be NULL
        if (element.Object == 0)
            return false;

        boneToWorld[boneIndex] = Bone(vphysics.PositionMatrix(element.Object));

        if (element.ParentIndex >= 0 && !ragdoll.AllowStretch)
        {
            // overwrite the position from physics to force rigid attachment
            int parentBoneIndex = ragdoll.BoneIndex[element.ParentIndex];
            NVector o = BoneSetupMath.VectorTransform(N(element.OriginParentSpace), boneToWorld[parentBoneIndex]);
            boneToWorld[boneIndex].Position = o;
        }

        return true;
    }

    /// <summary><c>RagdollComputeExactBbox</c>.</summary>
    public (Vector Mins, Vector Maxs) ComputeExactBbox(RagdollBody ragdoll, Vector origin)
    {
        Vector outMins = origin;
        Vector outMaxs = origin;

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            nint obj = ragdoll.List[i].Object;
            (Vector objectOrg, QAngle objectAng) = vphysics.PositionAngles(obj);
            (Vector mins, Vector maxs) = vphysics.CollideAabb(vphysics.ObjectCollide(obj), objectOrg, objectAng);

            outMins = new Vector(mins.X < outMins.X ? mins.X : outMins.X,
                                 mins.Y < outMins.Y ? mins.Y : outMins.Y,
                                 mins.Z < outMins.Z ? mins.Z : outMins.Z);
            outMaxs = new Vector(maxs.X > outMaxs.X ? maxs.X : outMaxs.X,
                                 maxs.Y > outMaxs.Y ? maxs.Y : outMaxs.Y,
                                 maxs.Z > outMaxs.Z ? maxs.Z : outMaxs.Z);
        }

        return (outMins, outMaxs);
    }

    /// <summary><c>RagdollIsAsleep</c>.</summary>
    public bool IsAsleep(RagdollBody ragdoll)
    {
        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            nint obj = ragdoll.List[i].Object;
            if (obj != 0 && !vphysics.IsAsleep(obj))
                return false;
        }

        return true;
    }

    /// <summary><c>RagdollApplyAnimationAsVelocity( ragdoll, pPrevBones, pCurrentBones, dt )</c>.</summary>
    public void ApplyAnimationAsVelocity(RagdollBody ragdoll, ReadOnlySpan<BoneMatrix3x4> prevBones,
                                         ReadOnlySpan<BoneMatrix3x4> currentBones, float dt)
    {
        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            int boneIndex = ragdoll.BoneIndex[i];
            BoneSetupAdj.CalcBoneDerivatives(out NVector velocity, out NVector angVel,
                                             prevBones[boneIndex], currentBones[boneIndex], dt);

            // Angular velocity is always applied in local space in vphysics
            nint obj = ragdoll.List[i].Object;
            Vector localAngVelocity = vphysics.WorldToLocalVector(obj, V(angVel));
            vphysics.AddVelocity(obj, V(velocity), localAngVelocity);
        }
    }

    /// <summary><c>RagdollApplyAnimationAsVelocity( ragdoll, pBoneToWorld )</c>: one matrix per element.</summary>
    public void ApplyAnimationAsVelocity(RagdollBody ragdoll, ReadOnlySpan<BoneMatrix3x4> boneToWorld)
    {
        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            BoneMatrix3x4 inverse = BoneSetupMath.MatrixInvert(boneToWorld[i]);
            (System.Numerics.Quaternion q, NVector pos) = BoneSetupMath.MatrixAnglesQuaternion(inverse);

            (NVector axis, float spin) = BoneSetupMath.QuaternionAxisAngle(q);
            NVector localAngVelocity = axis * spin;
            NVector localVelocity = pos;

            // move those bone-local coords back to world space using the ragdoll transform
            nint obj = ragdoll.List[i].Object;
            Vector velocity = vphysics.LocalToWorldVector(obj, V(localVelocity));
            vphysics.AddVelocity(obj, velocity, V(localAngVelocity));
        }
    }

    //-------------------------------------------------------------------------
    // Separation.
    //-------------------------------------------------------------------------

    /// <summary>physics_shared.cpp's <c>PhysHasContactWithOtherInDirection</c>.</summary>
    public bool HasContactWithOtherInDirection(nint physicsObject, Vector dir)
    {
        bool hit = false;
        nint gameData = vphysics.GameData(physicsObject);
        nint snapshot = vphysics.CreateFrictionSnapshot(physicsObject);

        while (vphysics.SnapshotIsValid(snapshot))
        {
            nint other = vphysics.SnapshotObject(snapshot, 1);

            if (vphysics.GameData(other) != gameData)
            {
                Vector normal = vphysics.SnapshotSurfaceNormal(snapshot);
                if (normal.Dot(dir) > 0)
                {
                    hit = true;
                    break;
                }
            }

            vphysics.SnapshotNext(snapshot);
        }

        vphysics.DestroyFrictionSnapshot(physicsObject, snapshot);
        return hit;
    }

    /// <summary>physics_shared.cpp's <c>PhysForceClearVelocity</c>.</summary>
    public void ForceClearVelocity(nint physicsObject)
    {
        nint snapshot = vphysics.CreateFrictionSnapshot(physicsObject);

        // clear the velocity of the rigid body
        vphysics.SetVelocity(physicsObject, Vector.Zero, Vector.Zero);

        // now clear the "strain" stored in the contact points
        while (vphysics.SnapshotIsValid(snapshot))
        {
            vphysics.SnapshotClearFrictionForce(snapshot);
            vphysics.SnapshotRecomputeFriction(snapshot);
            vphysics.SnapshotNext(snapshot);
        }

        vphysics.DestroyFrictionSnapshot(physicsObject, snapshot);
    }

    /// <summary>
    /// <c>RagdollSolveSeparation</c>: a limb the solver has left off its
    /// parent's attachment, when the cause looks like a state the solver
    /// cannot fix, is put back by hand. <paramref name="traceHits"/> is
    /// <c>UTIL_TraceRay( Ray( start, end ), MASK_SOLID, the owner, COLLISION_GROUP_NONE ).DidHit()</c>.
    /// Answers how many it fixed.
    /// </summary>
    public int SolveSeparation(RagdollBody ragdoll, Func<Vector, Vector, bool> traceHits, int tickCount)
    {
        Span<byte> needsFix = stackalloc byte[256];
        int fixCount = 0;

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            needsFix[i] = 0;
            RagdollElement element = ragdoll.List[i];

            if (element.Constraint == 0 || element.ParentIndex < 0)
                continue;

            nint parent = ragdoll.List[element.ParentIndex].Object;
            Vector start = vphysics.Position(element.Object);
            Vector target = vphysics.LocalToWorld(parent, element.OriginParentSpace);

            if (needsFix[element.ParentIndex] != 0)
            {
                needsFix[i] = 1;
                ++fixCount;
                continue;
            }

            Vector dir = target - start;
            if (dir.LengthSquared <= 1.0f)
                continue;

            // heuristic: guess that anything separated and small mass ratio is
            // in some state that's keeping the solver from fixing it
            float mass = vphysics.Mass(element.Object);
            float massParent = vphysics.Mass(parent);

            if (mass * 2.0f < massParent)
            {
                // if this is <0.5 mass of parent and still separated it's
                // attached to something heavy or in a bad state
                needsFix[i] = 1;
                ++fixCount;
                continue;
            }

            if (HasContactWithOtherInDirection(element.Object, dir) && traceHits(target, start))
            {
                needsFix[i] = 1;
                ++fixCount;
            }
        }

        if (fixCount == 0)
        {
            vphysics.GroupClearErrorState(ragdoll.Group);
            return 0;
        }

        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            if (needsFix[i] == 0)
                continue;

            RagdollElement element = ragdoll.List[i];
            nint parent = ragdoll.List[element.ParentIndex].Object;
            Vector target = vphysics.LocalToWorld(parent, element.OriginParentSpace);
            Vector velocity = vphysics.VelocityAtPoint(parent, target);
            Float3x4 xform = vphysics.PositionMatrix(element.Object);
            xform[0, 3] = target.X;
            xform[1, 3] = target.Y;
            xform[2, 3] = target.Z;
            vphysics.SetPositionMatrix(element.Object, xform, true);
            vphysics.SetVelocity(element.Object, velocity, Vector.Zero);
        }

        DevMsg(2, $"TICK:{tickCount,5}:Ragdoll separation count: {fixCount}\n");
        return fixCount;
    }

    //-------------------------------------------------------------------------
    // CRagdoll.
    //-------------------------------------------------------------------------

    /// <summary><c>CRagdoll::BuildRagdollBounds</c>: a cube of half the model's diagonal.</summary>
    public void BuildBounds(RagdollBody ragdoll, Vector modelMins, Vector modelMaxs)
    {
        Vector size = (modelMaxs - modelMins) * 0.5f;
        ragdoll.Radius = size.Length;
        RadiusCube(ragdoll);
    }

    private static void RadiusCube(RagdollBody ragdoll)
    {
        float r = ragdoll.Radius;
        ragdoll.Mins = new Vector(-r, -r, -r);
        ragdoll.Maxs = new Vector(r, r, r);
    }

    /// <summary><c>CRagdoll::GetRagdollOrigin</c>: element zero, the root the .phy declares.</summary>
    public Vector Origin(RagdollBody ragdoll) => vphysics.Position(ragdoll.List[0].Object);

    /// <summary>
    /// <c>CRagdoll::VPhysicsUpdate</c>: once per clock value, the body's cull
    /// box (exact when asleep, the radius cube otherwise), a separation pass
    /// when the group reports an error, and the settle check.
    /// </summary>
    public void VPhysicsUpdate(RagdollBody ragdoll, float curtime, float sleepAfterTime,
                               Func<Vector, Vector, bool> traceHits, int tickCount)
    {
        if (ragdoll.LastUpdate == curtime)
            return;

        ragdoll.LastUpdate = curtime;
        ragdoll.AllAsleep = IsAsleep(ragdoll);

        if (ragdoll.AllAsleep)
        {
            // NOTE: This is the bbox of the ragdoll's physics. It's not always
            // correct to use for culling, but it sure beats using the radius box!
            Vector origin = Origin(ragdoll);
            (Vector mins, Vector maxs) = ComputeExactBbox(ragdoll, origin);
            ragdoll.Mins = mins - origin;
            ragdoll.Maxs = maxs - origin;
        }
        else
        {
            RadiusCube(ragdoll);

            if (ragdoll.Group != 0 && vphysics.GroupIsInErrorState(ragdoll.Group))
                SolveSeparation(ragdoll, traceHits, tickCount);
        }

        // See if we should go to sleep...
        CheckSettleStationary(ragdoll, curtime, sleepAfterTime);
    }

    /// <summary><c>CRagdoll::CheckSettleStationaryRagdoll</c>.</summary>
    public void CheckSettleStationary(RagdollBody ragdoll, float curtime, float sleepAfterTime)
    {
        Vector origin = Origin(ragdoll);
        Vector delta = origin - ragdoll.LastOrigin;
        ragdoll.LastOrigin = origin;

        if (MathF.Abs(delta.X) > SleepTolerance || MathF.Abs(delta.Y) > SleepTolerance ||
            MathF.Abs(delta.Z) > SleepTolerance)
        {
            // It's still moving...
            ragdoll.LastOriginChangeTime = curtime;
            return;
        }

        // It's totally asleep, don't worry about forcing it to settle
        if (ragdoll.AllAsleep)
            return;

        // It has stopped moving, see if it
        float dt = curtime - ragdoll.LastOriginChangeTime;
        if (dt < sleepAfterTime)
            return;

        // Force it to go to sleep
        ForceToSleep(ragdoll);
    }

    /// <summary><c>CRagdoll::PhysForceRagdollToSleep</c>.</summary>
    public void ForceToSleep(RagdollBody ragdoll)
    {
        for (int i = 0; i < ragdoll.ListCount; i++)
        {
            nint obj = ragdoll.List[i].Object;
            if (obj == 0)
                continue;

            ForceClearVelocity(obj);
            vphysics.Sleep(obj);
        }
    }

    /// <summary><c>CRagdoll::ResetRagdollSleepAfterTime</c>.</summary>
    public static void ResetSleepAfterTime(RagdollBody ragdoll, float curtime) => ragdoll.LastOriginChangeTime = curtime;
}

/// <summary>
/// ragdoll_shared.cpp's <c>CRagdollCollisionRules</c>: the <c>collisionrules</c>
/// block's keys, onto a collision set.
/// </summary>
public sealed class RagdollCollisionRules(IRagdollVPhysics vphysics, nint collisionSet) : IRagdollKeySink
{
    public bool SelfCollisions { get; private set; } = true;

    public void ParseKeyValue(string key, string value)
    {
        if (string.Equals(key, "selfcollisions", StringComparison.OrdinalIgnoreCase))
        {
            // keys disabled by default
            SelfCollisions = false;
        }
        else if (string.Equals(key, "collisionpair", StringComparison.OrdinalIgnoreCase) && SelfCollisions)
        {
            // ss::NextToken: up to the first ',', then the rest up to the next.
            string rest = value;
            string first = NextToken(ref rest);
            string second = NextToken(ref rest);

            vphysics.EnableCollisions(collisionSet, CRuntime.Atoi(first), CRuntime.Atoi(second));
        }
    }

    private static string NextToken(ref string text)
    {
        int comma = text.IndexOf(',', StringComparison.Ordinal);

        if (comma < 0)
        {
            string all = text;
            text = string.Empty;
            return all;
        }

        string token = text[..comma];
        text = text[(comma + 1)..];
        return token;
    }
}

/// <summary>
/// ragdoll_shared.cpp's <c>CRagdollAnimatedFriction</c>: the
/// <c>animatedfriction</c> block's keys, onto the body.
/// </summary>
public sealed class RagdollAnimatedFrictionKeys(RagdollBody ragdoll) : IRagdollKeySink
{
    public void ParseKeyValue(string key, string value)
    {
        if (Is(key, "animfrictionmin"))
            ragdoll.AnimFriction.MinAnimatedFriction = CRuntime.Atoi(value);
        else if (Is(key, "animfrictionmax"))
            ragdoll.AnimFriction.MaxAnimatedFriction = CRuntime.Atoi(value);
        else if (Is(key, "animfrictiontimein"))
            ragdoll.AnimFriction.FrictionTimeIn = (float)CRuntime.Atof(value);
        else if (Is(key, "animfrictiontimeout"))
            ragdoll.AnimFriction.FrictionTimeOut = (float)CRuntime.Atof(value);
        else if (Is(key, "animfrictiontimehold"))
            ragdoll.AnimFriction.FrictionTimeHold = (float)CRuntime.Atof(value);
    }

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
