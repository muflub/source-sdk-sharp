//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Runtime.CompilerServices;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Server;

/// <summary>
/// The constraint and spring tables (plan_physics_bones_native_zero 2.4),
/// ported from shell_constraints.cpp. A constraint is a relationship between
/// two entities, owned by neither, so it is an id into a table rather than a
/// field on either end. Managed says what is attached to what and on which
/// axis; the solver stays in VPhysics, which is where it belongs.
///
/// <para>
/// Every vphysics call goes through <see cref="IVPhysicsJoints"/> (a direct
/// vtable call); the decisions native made are made here, in native's order:
/// which end is the world and goes first, what each kind's frame of reference
/// is measured from, the motor's load, and what a break tells.
/// </para>
/// </summary>
public sealed class PhysicsConstraints : IEngineConstraints, IEngineSprings, IGameLifecycle, ISaveCustom
{
    private readonly PhysicsJointTable _table;
    private readonly IVPhysicsJoints _vphysics;
    private readonly IPhysicsJointWorld _world;
    private readonly ILog _log;
    private readonly Action<PhysicsEvent> _post;

    public PhysicsConstraints(PhysicsJointTable table, IVPhysicsJoints vphysics, IPhysicsJointWorld world,
                              ILog log, Action<PhysicsEvent> post)
    {
        _table = table;
        _vphysics = vphysics;
        _world = world;
        _log = log;
        _post = post;
    }

    /// <summary>One live constraint: the vphysics object, the two ends as handles (None for the world), its kind.</summary>
    internal sealed class ConstraintEntry(nint pointer, EntityHandle a, EntityHandle b, ConstraintKind kind)
    {
        public nint Pointer { get; } = pointer;
        public EntityHandle A { get; } = a;
        public EntityHandle B { get; } = b;
        public ConstraintKind Kind { get; } = kind;

        /// <summary>A world hinge that made its object hinged, which taking it down undoes.</summary>
        public bool WorldHinged { get; set; }
    }

    /// <summary>One live spring.</summary>
    internal sealed class SpringEntry(nint pointer, EntityHandle a, EntityHandle b)
    {
        public nint Pointer { get; } = pointer;
        public EntityHandle A { get; } = a;
        public EntityHandle B { get; } = b;
    }

    /// <summary>
    /// The parameter count each kind needs (native's s_nRequired). A short
    /// block is a refusal rather than a read off the end.
    /// </summary>
    private static ReadOnlySpan<int> Required => [2, 9, 5, 10, 10, 17, 19];

    /// <summary>FVPHYSICS_CONSTRAINT_STATIC: pinned to the world.</summary>
    internal const ushort ConstraintStaticFlag = 0x0002;

    //-------------------------------------------------------------------------
    // Constraints.
    //-------------------------------------------------------------------------

    public int Count() => _table.CountOf(PhysicsJointKind.Constraint);

    /// <summary>
    /// The object to constrain, or the world. An end that names no live entity
    /// is anchored to the world — what a mapper means by a hinge with a single
    /// target. 0 comes back only when the entity exists but has no physics.
    /// </summary>
    private nint EndObject(EntityHandle entity, out bool isWorld)
    {
        if (!_world.IsValid(entity))
        {
            isWorld = true;
            return _world.WorldObject();
        }

        isWorld = false;
        return _world.PhysicsObject(entity);
    }

    /// <summary>
    /// Stock puts the world in slot 0 when only one end is attached, and every
    /// kind below assumes that ordering. Answers false when neither end has an
    /// object or both are the same one, having said why.
    /// </summary>
    private bool OrderEnds(string what, ref nint obj1, ref bool world1, ref nint obj2, ref bool world2)
    {
        nint worldObject = _world.WorldObject();

        if (obj1 == 0 && obj2 != 0)
        {
            obj1 = worldObject;
            world1 = true;
        }
        else if (obj1 != 0 && obj2 == 0)
        {
            obj2 = obj1;
            obj1 = worldObject;
            world1 = true;
        }

        if (obj1 == 0 || obj2 == 0)
        {
            _log.Warning($"[SourceSharp] {what}: neither end has a physics object\n");
            return false;
        }

        if (obj1 == obj2)
        {
            _log.Warning($"[SourceSharp] {what}: both ends are the same object\n");
            return false;
        }

        return true;
    }

    public uint Create(EntityHandle a, EntityHandle b, int kind, ReadOnlySpan<float> p)
    {
        nint env = _world.Environment();
        if (env == 0)
            return 0u;

        if (kind < 0 || kind > (int)ConstraintKind.Ragdoll)
        {
            _log.Warning($"[SourceSharp] constraint: unknown kind {kind}\n");
            return 0u;
        }

        if (p.Length < Required[kind])
        {
            _log.Warning($"[SourceSharp] constraint kind {kind} needs {Required[kind]} parameters, got {p.Length}\n");
            return 0u;
        }

        nint obj1 = EndObject(a, out bool world1);
        nint obj2 = EndObject(b, out bool world2);
        if (!OrderEnds("constraint", ref obj1, ref world1, ref obj2, ref world2))
            return 0u;

        // CPhysConstraint::ActivateConstraint's two refusals.
        if (_vphysics.IsStatic(obj1) && _vphysics.IsStatic(obj2))
        {
            _log.Warning("[SourceSharp] constraint attached to two static objects\n");
            return 0u;
        }

        if (_vphysics.HasShadowController(obj1) && _vphysics.HasShadowController(obj2))
        {
            _log.Warning("[SourceSharp] constraint attached to two shadow objects\n");
            return 0u;
        }

        var breakable = ConstraintBreakableParams.Defaults();
        breakable.ForceLimit = p[0];
        breakable.TorqueLimit = p[1];
        breakable.Strength = 1f;

        nint worldObject = _world.WorldObject();
        nint constraint;
        bool worldHinged = false;

        switch ((ConstraintKind)kind)
        {
            case ConstraintKind.Fixed:
            {
                var fixedParams = ConstraintFixedParams.Defaults();
                fixedParams.AttachedRefXform = RelativeXform(obj1, obj2);
                fixedParams.Constraint = breakable;

                // Pinning something to the world is what a mapper means by
                // "fixed", and VPhysics needs telling so it does not keep
                // simulating the attached object as though it were free.
                if (obj1 == worldObject)
                    _vphysics.SetGameFlags(obj2, (ushort)(_vphysics.GameFlags(obj2) | ConstraintStaticFlag));

                constraint = _vphysics.CreateFixedConstraint(env, obj1, obj2, fixedParams);
                break;
            }

            case ConstraintKind.Hinge:
            {
                var axis = new Vector(p[5], p[6], p[7]);
                if (axis.X == 0f && axis.Y == 0f && axis.Z == 0f)
                {
                    _log.Warning("[SourceSharp] hinge constraint has no axis direction\n");
                    return 0u;
                }

                // The world goes first, as stock's GetConstraintObjects arranges
                // it: the first object is the REFERENCE the second turns
                // against. Built the other way round a hinge to the world holds
                // nothing, which let dm_lockdown's fan be torn off its pivot by
                // its own motor. A hinge's axis and point name no end, so the
                // two can simply trade places.
                if (obj2 == worldObject && obj1 != worldObject)
                {
                    (obj1, obj2) = (obj2, obj1);
                    (world1, world2) = (world2, world1);
                    (a, b) = (b, a);
                }

                var hinge = ConstraintHingeParams.Defaults();
                hinge.WorldPosition = new Vector(p[2], p[3], p[4]);
                hinge.WorldAxisDirection = axis;
                hinge.Constraint = breakable;

                // The 1000 is stock's: hinge friction is in kg*in^2/s^2, and a
                // mapper's number means the same here as in Hammer.
                hinge.HingeAxis = ConstraintAxisLimit.Friction(0f, 0f, p[8] * 1000f);

                // SF_CONSTRAINT_ASSUME_WORLD_GEOMETRY to the world: the object
                // itself becomes hinged about the local axis the hinge's is.
                worldHinged = p.Length > 9 && p[9] != 0f && WorldHinge(obj1 == worldObject, obj2, axis);

                // The "do not collide" checkbox is PhysHinge's: it records the
                // pair in EntityPairCollisions once the joint exists (2.1).
                constraint = _vphysics.CreateHingeConstraint(env, obj1, obj2, hinge);
                break;
            }

            case ConstraintKind.BallSocket:
            {
                var ballsocket = ConstraintBallsocketParams.Defaults();
                var anchor = new Vector(p[2], p[3], p[4]);
                ballsocket.ConstraintPosition0 = _vphysics.WorldToLocal(obj1, anchor);
                ballsocket.ConstraintPosition1 = _vphysics.WorldToLocal(obj2, anchor);
                ballsocket.Constraint = breakable;

                // A ball socket that resisted turning would be a hinge: the
                // torque limit would snap it the moment it swung.
                ballsocket.Constraint.TorqueLimit = 0f;

                constraint = _vphysics.CreateBallsocketConstraint(env, obj1, obj2, ballsocket);
                break;
            }

            case ConstraintKind.Length:
            {
                var refPosition = new Vector(p[2], p[3], p[4]);
                var attachedPosition = new Vector(p[5], p[6], p[7]);

                // constraint_lengthparams_t::InitWorldspace, not rigid.
                var length = ConstraintLengthParams.Defaults();
                length.ObjectPosition0 = _vphysics.WorldToLocal(obj1, refPosition);
                length.ObjectPosition1 = _vphysics.WorldToLocal(obj2, attachedPosition);
                length.TotalLength = (refPosition - attachedPosition).Length;
                length.MinLength = 0f;

                length.TotalLength += p[9];
                length.MinLength = p[8];
                length.Constraint = breakable;

                constraint = _vphysics.CreateLengthConstraint(env, obj1, obj2, length);
                break;
            }

            case ConstraintKind.Sliding:
            {
                var start = new Vector(p[2], p[3], p[4]);
                var end = new Vector(p[5], p[6], p[7]);

                // No refusal for coincident ends, as stock has none
                // (CPhysSlideConstraint::CreateConstraint). Native's guard
                // compared this normalize's answer with 0, which the engine's
                // SSE VectorNormalize never gives: it answers sqrt(1e-10).
                Vector axis = end - start;
                MoveVec.Normalize(ref axis);

                // Stock's own snap, and not cosmetic: an axis a fraction off
                // true drifts the attached object sideways over its travel.
                axis = PhysMotor.SnapToAxis(axis);

                // constraint_slidingparams_t::InitWithCurrentObjectState.
                var sliding = ConstraintSlidingParams.Defaults();
                sliding.AttachedRefXform = RelativeXform(obj1, obj2);
                sliding.SlideAxisRef = JointMath.IRotate(axis, _vphysics.PositionMatrix(obj1));
                sliding.Friction = p[8];
                sliding.Constraint = breakable;

                if (p[9] != 0f)
                {
                    // The limits are the two ends PROJECTED onto the axis,
                    // measured from where the attached object stands now, and
                    // widened to include it: an object that spawned outside its
                    // own limits is otherwise shoved to the nearest end.
                    Vector here = _vphysics.Position(obj2);

                    sliding.LimitMin = axis.Dot(start);
                    sliding.LimitMax = axis.Dot(end);

                    if (sliding.LimitMax < sliding.LimitMin)
                        (sliding.LimitMin, sliding.LimitMax) = (sliding.LimitMax, sliding.LimitMin);

                    float atHere = here.Dot(axis);

                    if (atHere < sliding.LimitMin)
                        sliding.LimitMin = atHere;
                    else if (atHere > sliding.LimitMax)
                        sliding.LimitMax = atHere;

                    // VPhysics wants them relative to the current position.
                    sliding.LimitMin -= atHere;
                    sliding.LimitMax -= atHere;
                }

                constraint = _vphysics.CreateSlidingConstraint(env, obj1, obj2, sliding);
                break;
            }

            case ConstraintKind.Pulley:
            {
                var pulley = ConstraintPulleyParams.Defaults();
                pulley.PulleyPosition0 = new Vector(p[2], p[3], p[4]);
                pulley.PulleyPosition1 = new Vector(p[5], p[6], p[7]);

                // In each object's OWN space: zero is the object's centre.
                pulley.ObjectPosition0 = new Vector(p[8], p[9], p[10]);
                pulley.ObjectPosition1 = new Vector(p[11], p[12], p[13]);

                // Applied to the second span before the rope is measured.
                float gearRatio = p[15];

                Vector world0 = JointMath.Transform(pulley.ObjectPosition0, _vphysics.PositionMatrix(obj1));
                Vector world1End = JointMath.Transform(pulley.ObjectPosition1, _vphysics.PositionMatrix(obj2));

                // Measured as strung rather than given: a total that disagreed
                // with where the objects are yanks both on the first tick.
                pulley.TotalLength = p[14]
                    + (world0 - pulley.PulleyPosition0).Length
                    + (world1End - pulley.PulleyPosition1).Length * gearRatio;

                // Zero would divide the attached end's travel by nothing;
                // stock leaves the default of 1 standing for it.
                if (gearRatio != 0f)
                    pulley.GearRatio = gearRatio;

                pulley.IsRigid = p[16] != 0f ? (byte)1 : (byte)0;
                pulley.Constraint = breakable;

                constraint = _vphysics.CreatePulleyConstraint(env, obj1, obj2, pulley);
                break;
            }

            default: // ConstraintKind.Ragdoll
            {
                var ragdoll = ConstraintRagdollParams.Defaults();

                // The joint's own frame, as a map writes it; both transforms
                // are expressed relative to it from each object's current pose.
                BoneMatrix3x4 jointToWorld = BoneSetupMath.AngleMatrix(new BoneAngle(p[5], p[6], p[7]),
                                                                       new System.Numerics.Vector3(p[2], p[3], p[4]));

                BoneMatrix3x4 worldToReference = BoneSetupMath.MatrixInvert(Bone(_vphysics.PositionMatrix(obj1)));
                ragdoll.ConstraintToReference = Float(BoneSetupMath.ConcatTransforms(worldToReference, jointToWorld));

                BoneMatrix3x4 worldToAttached = BoneSetupMath.MatrixInvert(Bone(_vphysics.PositionMatrix(obj2)));
                ragdoll.ConstraintToAttached = Float(BoneSetupMath.ConcatTransforms(worldToAttached, jointToWorld));

                // Valve's FIXME, still true: these friction numbers are in
                // different units from the hinge's, which is why the hinge
                // scales by 1000 and this does not.
                ragdoll.Axis0 = ConstraintAxisLimit.Friction(p[8], p[9], p[14]);
                ragdoll.Axis1 = ConstraintAxisLimit.Friction(p[10], p[11], p[15]);
                ragdoll.Axis2 = ConstraintAxisLimit.Friction(p[12], p[13], p[16]);

                ragdoll.OnlyAngularLimits = p[17] != 0f ? (byte)1 : (byte)0;
                ragdoll.IsActive = p[18] != 0f ? (byte)1 : (byte)0;
                ragdoll.Constraint = breakable;

                constraint = _vphysics.CreateRagdollConstraint(env, obj1, obj2, ragdoll);
                break;
            }
        }

        if (constraint == 0)
        {
            _log.Warning($"[SourceSharp] constraint kind {kind} could not be created\n");
            return 0u;
        }

        var entry = new ConstraintEntry(constraint, world1 ? EntityHandle.None : a, world2 ? EntityHandle.None : b,
                                        (ConstraintKind)kind) { WorldHinged = worldHinged };
        uint id = _table.Bind(PhysicsJointKind.Constraint, entry);

        if (id == 0u)
        {
            // Refused rather than made room for: an id the caller cannot use
            // would leave a live constraint nothing can ever destroy.
            _log.Warning($"[SourceSharp] constraint kind {kind}: the physics id table is full\n");
            _vphysics.Deactivate(constraint);
            _vphysics.DestroyConstraint(env, constraint);
            return 0u;
        }

        return id;
    }

    public void Destroy(uint constraint)
    {
        if (_table.Lookup<ConstraintEntry>(constraint, PhysicsJointKind.Constraint) is not { } entry)
            return;

        // ~CPhysConstraint: Deactivate, then destroy.
        Deactivate(entry);
        _vphysics.DestroyConstraint(_world.Environment(), entry.Pointer);

        // Freeing the id advances the slot's serial: every copy of it anybody
        // kept answers nothing from this line on.
        _table.Free(constraint, PhysicsJointKind.Constraint);
    }

    public bool IsValid(uint constraint) => _table.Lookup<ConstraintEntry>(constraint, PhysicsJointKind.Constraint) is not null;

    public void Deactivate(uint constraint)
    {
        if (_table.Lookup<ConstraintEntry>(constraint, PhysicsJointKind.Constraint) is { } entry)
            Deactivate(entry);
    }

    private void Deactivate(ConstraintEntry entry)
    {
        nint reference = _vphysics.ReferenceObject(entry.Pointer);
        nint attached = _vphysics.AttachedObject(entry.Pointer);

        // CPhysHinge::Deactivate. NOTE: RemoveHinged() is always safe
        if (entry.WorldHinged)
            _vphysics.RemoveHinged(attached);

        _vphysics.Deactivate(entry.Pointer);

        // CPhysConstraint::ClearStaticFlag on both.
        foreach (nint obj in new[] { reference, attached })
        {
            if (obj != 0)
                _vphysics.SetGameFlags(obj, (ushort)(_vphysics.GameFlags(obj) & ~ConstraintStaticFlag));
        }
    }

    public void Activate(uint constraint)
    {
        if (_table.Lookup<ConstraintEntry>(constraint, PhysicsJointKind.Constraint) is not { } entry)
            return;

        nint reference = _vphysics.ReferenceObject(entry.Pointer);
        nint attached = _vphysics.AttachedObject(entry.Pointer);
        if (reference == 0 || attached == 0)
            return;

        _vphysics.Activate(entry.Pointer);
        _vphysics.Wake(reference);
        _vphysics.Wake(attached);
    }


    public void SetHingeVelocity(uint constraint, float degreesPerSecond, float systemLoadScale)
    {
        if (_table.Lookup<ConstraintEntry>(constraint, PhysicsJointKind.Constraint) is not { Kind: ConstraintKind.Hinge } entry)
            return;

        nint reference = _vphysics.ReferenceObject(entry.Pointer);
        nint attached = _vphysics.AttachedObject(entry.Pointer);
        if (reference == 0 || attached == 0)
            return;

        float massLoad = 1f;
        int numMasses = 0;

        if (_vphysics.IsMoveable(reference))
        {
            massLoad = _vphysics.Inertia(reference).Length;
            numMasses++;
            _vphysics.Wake(reference);
        }

        if (_vphysics.IsMoveable(attached))
        {
            massLoad += _vphysics.Inertia(attached).Length;
            numMasses++;
            _vphysics.Wake(attached);
        }

        if (numMasses > 0)
            massLoad /= numMasses;

        // Stock multiplies by the load scale twice.
        float loadScale = systemLoadScale != 0f ? systemLoadScale : 1f;
        _vphysics.SetAngularMotor(entry.Pointer, degreesPerSecond,
            degreesPerSecond * loadScale * massLoad * loadScale * (1f / _world.TickInterval()));
    }

    /// <summary>
    /// Drives a joint along its axis. Not guarded on the kind: a constraint
    /// that cannot be driven linearly ignores the call in VPhysics. Stock's
    /// CPhysSlideConstraint arithmetic: the force is scaled by the AVERAGE mass
    /// of the ends that can move.
    /// </summary>
    public void SetLinearMotor(uint constraint, float unitsPerSecond, float systemLoadScale)
    {
        if (_table.Lookup<ConstraintEntry>(constraint, PhysicsJointKind.Constraint) is not { } entry)
            return;

        nint reference = _vphysics.ReferenceObject(entry.Pointer);
        nint attached = _vphysics.AttachedObject(entry.Pointer);
        if (reference == 0 || attached == 0)
            return;

        // Stock's shape, ASSIGNMENT then addition: the 1 it starts from
        // survives when only the attached end can move, a kilogram heavy.
        float massLoad = 1f;
        int masses = 0;

        if (_vphysics.IsMoveable(reference))
        {
            massLoad = _vphysics.Mass(reference);
            masses++;
            _vphysics.Wake(reference);
        }

        if (_vphysics.IsMoveable(attached))
        {
            massLoad += _vphysics.Mass(attached);
            masses++;
            _vphysics.Wake(attached);
        }

        if (masses > 0)
            massLoad /= masses;

        float loadScale = systemLoadScale != 0f ? systemLoadScale : 1f;

        _vphysics.SetLinearMotor(entry.Pointer, unitsPerSecond,
            unitsPerSecond * loadScale * massLoad * (1f / _world.TickInterval()));
    }

    /// <summary>
    /// The break vphysics reports, through managed's IPhysicsConstraintEvent.
    /// A constraint this table did not make finds nothing and tells nobody,
    /// which is the right answer for one some other code made. Ours goes to
    /// the router as SS_PHYSEVENT_CONSTRAINT_BROKEN did: the constraint's id
    /// and both ends, delivered after the step.
    /// </summary>
    public void Broken(nint constraint)
    {
        if (constraint == 0)
            return;

        foreach (var (id, entry) in _table.All<ConstraintEntry>(PhysicsJointKind.Constraint))
        {
            if (entry.Pointer != constraint)
                continue;

            _post(new PhysicsEvent
            {
                KindRaw = (uint)PhysicsEventKind.ConstraintBroken,
                Flags = (uint)PhysicsJointKind.Constraint,
                Entity0 = entry.A.Value,
                Entity1 = entry.B.Value,
                Object0 = id,
            });
            return;
        }
    }

    //-------------------------------------------------------------------------
    // Springs: the same shape of thing, in the same id table under their own
    // kind, so a spring id asked for as a constraint answers nothing.
    //-------------------------------------------------------------------------

    int IEngineSprings.Count() => _table.CountOf(PhysicsJointKind.Spring);

    uint IEngineSprings.Create(EntityHandle a, EntityHandle b, Vector start, Vector end, float constant, float damping,
                               float relativeDamping, float naturalLength, bool onlyStretch)
    {
        nint env = _world.Environment();
        if (env == 0)
            return 0u;

        nint obj1 = EndObject(a, out bool world1);
        nint obj2 = EndObject(b, out bool world2);
        if (!OrderEnds("spring", ref obj1, ref world1, ref obj2, ref world2))
            return 0u;

        // Stock's PointIsNearer: the endpoints are world positions with no
        // word on which belongs to which object, and a spring built the wrong
        // way round crosses over and yanks both.
        Vector centre = _vphysics.Position(obj2);
        if (!((centre - end).LengthSquared < (centre - start).LengthSquared))
            (start, end) = (end, start);

        var spring = new SpringParams
        {
            Constant = constant,
            Damping = damping,
            RelativeDamping = relativeDamping,
            NaturalLength = naturalLength,
            StartPosition = start,
            EndPosition = end,
            UseLocalPositions = 0,
            OnlyStretch = onlyStretch ? (byte)1 : (byte)0,
        };

        nint pointer = _vphysics.CreateSpring(env, obj1, obj2, spring);
        if (pointer == 0)
        {
            _log.Warning("[SourceSharp] spring could not be created\n");
            return 0u;
        }

        uint id = _table.Bind(PhysicsJointKind.Spring, new SpringEntry(pointer, world1 ? EntityHandle.None : a,
                                                                        world2 ? EntityHandle.None : b));
        if (id == 0u)
        {
            _log.Warning("[SourceSharp] spring: the physics id table is full\n");
            _vphysics.DestroySpring(env, pointer);
            return 0u;
        }

        return id;
    }

    void IEngineSprings.Destroy(uint spring)
    {
        if (_table.Lookup<SpringEntry>(spring, PhysicsJointKind.Spring) is not { } entry)
            return;

        _vphysics.DestroySpring(_world.Environment(), entry.Pointer);
        _table.Free(spring, PhysicsJointKind.Spring);
    }

    bool IEngineSprings.IsValid(uint spring) => _table.Lookup<SpringEntry>(spring, PhysicsJointKind.Spring) is not null;

    void IEngineSprings.SetConstant(uint spring, float constant)
    {
        if (_table.Lookup<SpringEntry>(spring, PhysicsJointKind.Spring) is { } entry)
            _vphysics.SetSpringConstant(entry.Pointer, constant);
    }

    void IEngineSprings.SetDamping(uint spring, float damping)
    {
        if (_table.Lookup<SpringEntry>(spring, PhysicsJointKind.Spring) is { } entry)
            _vphysics.SetSpringDamping(entry.Pointer, damping);
    }

    void IEngineSprings.SetLength(uint spring, float length)
    {
        if (_table.Lookup<SpringEntry>(spring, PhysicsJointKind.Spring) is { } entry)
            _vphysics.SetSpringLength(entry.Pointer, length);
    }

    //-------------------------------------------------------------------------
    // The level and the reload.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Everything goes at level change. The environment is torn down with the
    /// level, so the pointers are dropped rather than destroyed one by one: by
    /// the time this runs the objects may already be gone.
    /// </summary>
    public void LevelShutdown()
    {
        _table.FreeAll(PhysicsJointKind.Constraint);
        _table.FreeAll(PhysicsJointKind.Spring);
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsConstraints;

    void IGameLifecycle.OnLevelShutdown() => LevelShutdown();

    /// <summary>
    /// A reload keeps the environment and every joint in it, and the entities
    /// carry their ids across (PhysicsConstraintEntity's _jointId), so the
    /// table goes too: each row under the id it had.
    /// </summary>
    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach (var (id, entry) in _table.All<ConstraintEntry>(PhysicsJointKind.Constraint))
        {
            writer.AddRow()
                  .Set("id", unchecked((int)id))
                  .Set("kind", (int)PhysicsJointKind.Constraint)
                  .Set("ptr", ((long)entry.Pointer).ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Set("a", entry.A)
                  .Set("b", entry.B)
                  .Set("ck", (int)entry.Kind)
                  .Set("wh", entry.WorldHinged ? 1 : 0);
        }

        foreach (var (id, entry) in _table.All<SpringEntry>(PhysicsJointKind.Spring))
        {
            writer.AddRow()
                  .Set("id", unchecked((int)id))
                  .Set("kind", (int)PhysicsJointKind.Spring)
                  .Set("ptr", ((long)entry.Pointer).ToString(System.Globalization.CultureInfo.InvariantCulture))
                  .Set("a", entry.A)
                  .Set("b", entry.B);
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        LevelShutdown();

        foreach (ReloadRow row in reader.Rows)
        {
            uint id = unchecked((uint)row.GetInt("id"));
            nint pointer = long.TryParse(row.GetString("ptr"), System.Globalization.NumberStyles.Integer,
                                         System.Globalization.CultureInfo.InvariantCulture, out long raw)
                ? (nint)raw : 0;
            if (pointer == 0)
                continue;

            var kind = (PhysicsJointKind)row.GetInt("kind");
            object entry = kind == PhysicsJointKind.Spring
                ? new SpringEntry(pointer, row.GetHandle("a"), row.GetHandle("b"))
                : new ConstraintEntry(pointer, row.GetHandle("a"), row.GetHandle("b"), (ConstraintKind)row.GetInt("ck"))
                  {
                      WorldHinged = row.GetInt("wh") != 0,
                  };
            _table.Restore(id, kind, entry);
        }
    }

    /// <summary>
    /// CPhysHinge::IsWorldHinge and its BecomeHinged: to the world, the
    /// hinge's axis in the attached object's space, snapped, names one of its
    /// axes, which the object is hinged about. False leaves it free.
    /// </summary>
    private bool WorldHinge(bool toWorld, nint attached, Vector worldAxis)
    {
        if (!toWorld)
            return false;

        Vector localHinge = PhysMotor.SnapToAxis(_vphysics.WorldToLocalVector(attached, worldAxis));
        int hingeAxis = UnitAxisIndex(localHinge);
        if (hingeAxis < 0)
            return false;

        _vphysics.BecomeHinged(attached, hingeAxis);
        return true;
    }

    /// <summary>GetUnitAxisIndex (physconstraint.cpp): the one component that is exactly +-1, the rest 0; else -1.</summary>
    internal static int UnitAxisIndex(Vector axis)
    {
        bool valid = false;
        int index = -1;

        for (int i = 0; i < 3; i++)
        {
            float c = i == 0 ? axis.X : i == 1 ? axis.Y : axis.Z;
            if (c != 0f)
            {
                if (MathF.Abs(c) == 1f)
                {
                    if (index < 0)
                    {
                        index = i;
                        valid = true;
                        continue;
                    }
                }
                valid = false;
            }
        }

        return valid ? index : -1;
    }

    //-------------------------------------------------------------------------
    // Transforms.
    //-------------------------------------------------------------------------

    /// <summary>constraints.h's BuildObjectRelativeXform: the attached object in the reference's space.</summary>
    private Float3x4 RelativeXform(nint reference, nint attached)
    {
        BoneMatrix3x4 outInv = BoneSetupMath.MatrixInvert(Bone(_vphysics.PositionMatrix(reference)));
        return Float(BoneSetupMath.ConcatTransforms(outInv, Bone(_vphysics.PositionMatrix(attached))));
    }

    /// <summary>The same 48 bytes as the bone library's matrix, for its MatrixInvert / ConcatTransforms / AngleMatrix.</summary>
    internal static BoneMatrix3x4 Bone(Float3x4 m) => Unsafe.BitCast<Float3x4, BoneMatrix3x4>(m);

    internal static Float3x4 Float(BoneMatrix3x4 m) => Unsafe.BitCast<BoneMatrix3x4, Float3x4>(m);
}

/// <summary>
/// mathlib_base.cpp's vector-by-matrix products on <see cref="Vector"/> and
/// <see cref="Float3x4"/>, in stock's summation order (the bone library's
/// BoneSetupMath spells the same sums over System.Numerics).
/// </summary>
internal static class JointMath
{
    /// <summary><c>VectorIRotate</c>: rotate by the transpose, column-major summation.</summary>
    public static Vector IRotate(Vector v, in Float3x4 m) => new(
        v.X * m[0, 0] + v.Y * m[1, 0] + v.Z * m[2, 0],
        v.X * m[0, 1] + v.Y * m[1, 1] + v.Z * m[2, 1],
        v.X * m[0, 2] + v.Y * m[1, 2] + v.Z * m[2, 2]);

    /// <summary><c>VectorTransform</c>: per row, dot then the translation column.</summary>
    public static Vector Transform(Vector v, in Float3x4 m) => new(
        (v.X * m[0, 0] + v.Y * m[0, 1] + v.Z * m[0, 2]) + m[0, 3],
        (v.X * m[1, 0] + v.Y * m[1, 1] + v.Z * m[1, 2]) + m[1, 3],
        (v.X * m[2, 0] + v.Y * m[2, 1] + v.Z * m[2, 2]) + m[2, 3]);

    /// <summary><c>CrossProduct</c>.</summary>
    public static Vector Cross(Vector a, Vector b) => new(
        a.Y * b.Z - a.Z * b.Y,
        a.Z * b.X - a.X * b.Z,
        a.X * b.Y - a.Y * b.X);
}
