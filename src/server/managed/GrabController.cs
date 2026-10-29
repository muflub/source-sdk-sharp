//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// Where the carrier is looking from, as one command's UpdateObject reads it:
/// the facts of the player stock's CGrabController::UpdateObject asks for.
/// </summary>
/// <param name="EyeAngles">EyeAngles(), degrees.</param>
/// <param name="ShootPosition">Weapon_ShootPosition().</param>
/// <param name="ObbMaxs">CollisionProp()->OBBMaxs().</param>
/// <param name="AbsMins">The world AABB's mins (WorldSpaceAABB).</param>
/// <param name="AbsMaxs">Its maxs.</param>
/// <param name="Center">CollisionProp()->WorldSpaceCenter().</param>
/// <param name="StandingOn">GetGroundEntity() is the held object.</param>
public readonly record struct GrabView(Vector EyeAngles, Vector ShootPosition, Vector ObbMaxs,
                                       Vector AbsMins, Vector AbsMaxs, Vector Center, bool StandingOn);

/// <summary>
/// CGrabController (weapon_physcannon.cpp, HL2MP's shared copy), for the
/// physcannon: holds one entity's physics object at a target the gun moves
/// every command, through a motion controller whose Simulate is this.
///
/// <para>
/// The object is held by ComputeShadowControl towards the target, at a
/// reduced mass so the carrier cannot pour energy into the world through it,
/// and given back its own mass and damping when let go. Every decision is
/// stock's; the vphysics calls go through <see cref="IVPhysicsGrabCalls"/>.
/// One object only: stock walks VPhysicsGetObjectList, which for anything the
/// gun can pick up here (no ragdolls: they are flesh) is the one object.
/// </para>
/// </summary>
public sealed class GrabController : IMotionSimulate
{
    /// <summary>DEFAULT_MAX_ANGULAR.</summary>
    public const float DefaultMaxAngular = 360f * 10f;

    /// <summary>REDUCED_CARRY_MASS.</summary>
    public const float ReducedCarryMass = 1f;

    /// <summary>The rotational damping a held object gets (AttachEntity's 10).</summary>
    public const float HeldRotDamping = 10f;

    /// <summary>FVPHYSICS_PLAYER_HELD.</summary>
    public const ushort PlayerHeld = 0x0004;

    // MASK_SOLID_BRUSHONLY.
    private const uint MaskSolidBrushOnly = 0x1 | 0x4000 | 0x2 | 0x8;

    private readonly IVPhysicsGrabCalls _v;
    private readonly IEngineEntityPhysics _physics;
    private readonly ICustomMotionControllers _controllers;

    private ShadowControlParams _shadow;
    private float _timeToArrive;
    private float _errorTime;
    private float _error;
    private float _contactAmount;
    private float _loadWeight;
    private float _savedRotDamping;
    private float _savedMass;
    private EntityHandle _attached;
    private uint _controller;
    private int _frameCount = -1;

    // InContactWithHeavyObject's and the slide's snapshot, reused.
    private readonly GrabContact[] _contacts = new GrabContact[32];

    public GrabController(IVPhysicsGrabCalls v, IEngineEntityPhysics physics, ICustomMotionControllers controllers)
    {
        _v = v;
        _physics = physics;
        _controllers = controllers;

        _shadow.DampFactor = 1f;
        _shadow.TeleportDistance = 0f;
        // make this controller really stiff!
        _shadow.MaxSpeed = 1000f;
        _shadow.MaxAngular = DefaultMaxAngular;
        _shadow.MaxDampSpeed = _shadow.MaxSpeed * 2f;
        _shadow.MaxDampAngular = _shadow.MaxAngular;
    }

    /// <summary>m_attachedAnglesPlayerSpace.</summary>
    public QAngle AttachedAnglesPlayerSpace { get; set; }

    /// <summary>m_attachedPositionObjectSpace.</summary>
    public Vector AttachedPositionObjectSpace { get; set; }

    /// <summary>What is held, or <see cref="EntityHandle.None"/>.</summary>
    public EntityHandle Attached => _attached;

    /// <summary>The held object's own mass, summed (m_flLoadWeight).</summary>
    public float LoadWeight => _loadWeight;

    /// <summary>The mass the object had before it was picked up (GetSavedMass).</summary>
    public float SavedMass => _attached == EntityHandle.None ? 0f : _savedMass;

    /// <summary>The motion controller holding it, 0 for none.</summary>
    public uint Controller => _controller;

    /// <summary>The target the shadow is steering towards.</summary>
    public ShadowControlParams Shadow => _shadow;

    /// <summary>
    /// AttachEntity: take hold of <paramref name="entity"/>'s object.
    /// <paramref name="playerToWorld"/> is the carrier's EntityToWorldTransform
    /// rotation; <paramref name="entityToWorld"/> and
    /// <paramref name="entityOrigin"/> the object's, and
    /// <paramref name="entityCenter"/> its WorldSpaceCenter. False when the
    /// entity has no object or no controller could be made.
    /// </summary>
    public bool AttachEntity(EntityHandle entity, in GrabMatrix playerToWorld,
                             in GrabMatrix entityToWorld, Vector entityOrigin, Vector entityCenter,
                             float frameTime)
    {
        nint phys = _physics.PhysicsObject(entity);
        if (phys == 0)
            return false;

        (Vector position, QAngle angles) = _v.Position(phys);

        _controller = _controllers.CreateCustom(entity, this);
        if (_controller == 0)
            return false;

        _v.Wake(phys);
        _v.SetGameFlags(phys, (ushort)(_v.GameFlags(phys) | PlayerHeld));
        _attached = entity;
        SetTargetPosition(position, angles, frameTime);

        _loadWeight = 0f;
        float mass = _v.Mass(phys);
        _savedRotDamping = _v.RotDamping(phys);
        _loadWeight += mass;
        _savedMass = mass;

        // reduce the mass to prevent the player from adding crazy amounts of
        // energy to the system (one object, so flFactor is 1)
        _v.SetMass(phys, ReducedCarryMass);
        _v.SetRotDamping(phys, HeldRotDamping);

        // Give extra mass to the phys object we're actually picking up
        _v.SetMass(phys, ReducedCarryMass);
        _v.EnableDrag(phys, false);

        _errorTime = -1f; // 1 seconds until error starts accumulating
        _error = 0f;
        _contactAmount = 0f;

        AttachedAnglesPlayerSpace = GrabMath.TransformAnglesToLocalSpace(angles, playerToWorld);
        AttachedPositionObjectSpace = GrabMath.ITransform(entityCenter, entityToWorld, entityOrigin);
        return true;
    }

    /// <summary>
    /// DetachEntity: let go, giving the object back its mass, damping and
    /// drag. <paramref name="clearVelocity"/> stops it dead (a launch, which
    /// sets its own velocity next); otherwise its speed is clamped to
    /// <paramref name="linearLimit"/> and 720°/s.
    /// </summary>
    public void DetachEntity(bool clearVelocity, float linearLimit)
    {
        EntityHandle entity = _attached;

        if (entity != EntityHandle.None && _physics.PhysicsObject(entity) is var phys && phys != 0)
        {
            // on the odd chance that it's gone to sleep while under anti-gravity
            _v.EnableDrag(phys, true);
            _v.Wake(phys);
            _v.SetMass(phys, _savedMass);
            _v.SetRotDamping(phys, _savedRotDamping);
            _v.SetGameFlags(phys, (ushort)(_v.GameFlags(phys) & ~PlayerHeld));

            if (clearVelocity)
            {
                // PhysForceClearVelocity
                _v.SetVelocity(phys, Vector.Zero, Vector.Zero);
                _v.ClearContactFriction(phys);
            }
            else
            {
                ClampPhysicsVelocity(phys, linearLimit, 2.0f * 360.0f);
            }
        }

        _attached = EntityHandle.None;

        if (_controller != 0)
            _controllers.Destroy(_controller);

        _controller = 0;
    }

    /// <summary>
    /// The object is gone (removed, or its physics destroyed): drop the
    /// controller without touching an object that no longer exists.
    /// </summary>
    public void Forget()
    {
        _attached = EntityHandle.None;

        if (_controller != 0)
            _controllers.Destroy(_controller);

        _controller = 0;
    }

    private void ClampPhysicsVelocity(nint phys, float linearLimit, float angularLimit)
    {
        (Vector vel, Vector angVel) = _v.Velocity(phys);
        float speed = GrabMath.Normalize(ref vel) - linearLimit;
        float angSpeed = GrabMath.Normalize(ref angVel) - angularLimit;
        speed = speed < 0 ? 0 : -speed;
        angSpeed = angSpeed < 0 ? 0 : -angSpeed;
        vel *= speed;
        angVel *= angSpeed;
        _v.AddVelocity(phys, vel, angVel);
    }

    /// <summary>SetTargetPosition: where the shadow steers, arriving within a frame.</summary>
    public void SetTargetPosition(Vector target, QAngle orientation, float frameTime)
    {
        _shadow.TargetPosition = target;
        _shadow.TargetRotation = orientation;

        _timeToArrive = frameTime;

        if (_attached != EntityHandle.None)
        {
            nint phys = _physics.PhysicsObject(_attached);

            if (phys != 0)
                _v.Wake(phys);
            else
                DetachEntity(false, 0f);
        }
    }

    /// <summary>
    /// ComputeError: how far the object has lagged its target, smoothed over
    /// the time since last asked; 9999 (let go) for an object with no physics.
    /// </summary>
    public float ComputeError()
    {
        if (_errorTime <= 0)
            return 0;

        if (_attached != EntityHandle.None)
        {
            nint phys = _physics.PhysicsObject(_attached);

            if (phys != 0)
            {
                Vector pos = _v.ShadowPosition(phys);

                float error = (_shadow.TargetPosition - pos).Length;
                if (_errorTime > 0)
                {
                    if (_errorTime > 1)
                        _errorTime = 1;

                    float speed = error / _errorTime;
                    if (speed > _shadow.MaxSpeed)
                        error *= 0.5f;

                    _error = (1 - _errorTime) * _error + error * _errorTime;
                }
            }
            else
            {
                DetachEntity(false, 0f);
                return 9999; // force detach
            }
        }

        _errorTime = 0;
        return _error;
    }

    /// <summary>
    /// UpdateObject: re-aim the held object at the hold position in front of
    /// the carrier. False when it should be dropped.
    /// </summary>
    /// <param name="view">The carrier, this command.</param>
    /// <param name="playerToWorld">The carrier's EntityToWorldTransform rotation.</param>
    /// <param name="maxError">How much lag is tolerated (12 for the cannon).</param>
    /// <param name="frameCount">gpGlobals->framecount, which limits this to once a frame.</param>
    /// <param name="traceFraction">
    /// A MASK_SOLID_BRUSHONLY ray from the first point to the second skipping
    /// the carrier and the object: its fraction.
    /// </param>
    public bool UpdateObject(in GrabView view, in GrabMatrix playerToWorld, float maxError,
                             int frameCount, float frameTime, Func<Vector, Vector, uint, float> traceFraction)
    {
        EntityHandle entity = _attached;
        if (entity == EntityHandle.None)
            return false;
        if (ComputeError() > maxError)
            return false;
        if (view.StandingOn)
            return false;

        nint phys = _physics.PhysicsObject(entity);
        if (phys == 0)
            return false;

        // Adrian: Oops, our object became motion disabled, let go!
        if (!_v.IsMoveable(phys))
            return false;

        if (_frameCount == frameCount)
            return true;

        _frameCount = frameCount;

        Vector playerAngles = view.EyeAngles;
        float pitch = GrabMath.AngleDistance(playerAngles.X, 0);
        playerAngles.X = Math.Clamp(pitch, -75f, 75f);
        Vector forward = Angles.Vectors(playerAngles).Forward;

        // Now clamp a sphere of object radius at end to the player's bbox
        Vector radial = _physics.CollideExtent(phys, forward * -1f) - _v.Position(phys).Position;
        Vector player2d = view.ObbMaxs;
        float playerRadius = MathF.Sqrt(player2d.X * player2d.X + player2d.Y * player2d.Y);
        float flDot = forward.Dot(radial);

        float radius = playerRadius + MathF.Abs(flDot);

        float distance = 24 + (radius * 2.0f);

        Vector start = view.ShootPosition;
        Vector end = start + (forward * distance);

        float fraction = traceFraction(start, end, MaskSolidBrushOnly);

        if (fraction < 0.5f)
            end = start + forward * (radius * 0.5f);
        else if (fraction <= 1.0f)
            end = start + forward * (distance - radius);

        Vector playerLine = view.Center;
        Vector nearest = GrabMath.ClosestPointOnLine(end, playerLine + new Vector(0, 0, view.AbsMins.Z),
                                                     playerLine + new Vector(0, 0, view.AbsMaxs.Z));

        Vector delta = end - nearest;
        float len = GrabMath.Normalize(ref delta);
        if (len < radius)
            end = nearest + delta * radius;

        QAngle angles = GrabMath.TransformAnglesToWorldSpace(AttachedAnglesPlayerSpace, playerToWorld);

        GrabMatrix attachedToWorld = GrabMath.AngleMatrix(angles);
        Vector offset = attachedToWorld.Rotate(AttachedPositionObjectSpace);

        SetTargetPosition(end - offset, angles, frameTime);
        return true;
    }

    /// <summary>
    /// CGrabController::Simulate: steer the shadow, slide along the contacts,
    /// and add nothing else.
    /// </summary>
    public MotionSimResult Simulate(nint physicsObject, float deltaTime, out Vector linear, out Vector angular)
    {
        ShadowControlParams shadowParams = _shadow;

        if (InContactWithHeavyObject(physicsObject, _loadWeight))
            _contactAmount = GrabMath.Approach(0.1f, _contactAmount, deltaTime * 2.0f);
        else
            _contactAmount = GrabMath.Approach(1.0f, _contactAmount, deltaTime * 2.0f);

        shadowParams.MaxAngular = _shadow.MaxAngular * _contactAmount * _contactAmount * _contactAmount;
        _timeToArrive = _v.ComputeShadowControl(physicsObject, in shadowParams, _timeToArrive, deltaTime);

        // Slide along the current contact points to fix bouncing problems
        (Vector velocity, Vector angVel) = _v.Velocity(physicsObject);
        velocity = ComputeSlideDirection(physicsObject, velocity, ref angVel, _loadWeight);
        _v.SetVelocityInstantaneous(physicsObject, velocity);

        linear = Vector.Zero;
        angular = Vector.Zero;
        _errorTime += deltaTime;

        return MotionSimResult.LocalAcceleration;
    }

    private bool InContactWithHeavyObject(nint phys, float heavyMass)
    {
        int count = _v.Contacts(phys, _contacts);

        for (int i = 0; i < count; i++)
        {
            if (!_contacts[i].OtherMoveable || _contacts[i].OtherMass > heavyMass)
                return true;
        }

        return false;
    }

    /// <summary>PhysComputeSlideDirection (physics_shared.cpp), with the angular output kept.</summary>
    private Vector ComputeSlideDirection(nint phys, Vector velocity, ref Vector angVel, float minMass)
    {
        int count = _v.Contacts(phys, _contacts);

        for (int i = 0; i < count; i++)
        {
            GrabContact c = _contacts[i];
            if (c.OtherMoveable && c.OtherMass <= minMass)
                continue;

            Vector normal = c.Normal;

            // BUGBUG: Figure out the correct rotation clipping equation
            angVel = normal * angVel.Dot(normal);

            // Determine how far along plane to slide based on incoming direction.
            // NOTE: Normal points away from this object
            float proj = velocity.Dot(normal);
            if (proj > 0.0f)
                velocity -= normal * proj;
        }

        return velocity;
    }
}

/// <summary>A matrix3x4_t's rotation, by its three columns (the x, y and z axes).</summary>
public readonly record struct GrabMatrix(Vector X, Vector Y, Vector Z)
{
    /// <summary>VectorRotate: this rotation applied to <paramref name="v"/>.</summary>
    public Vector Rotate(Vector v) => X * v.X + Y * v.Y + Z * v.Z;

    /// <summary>VectorIRotate: the inverse rotation applied to <paramref name="v"/>.</summary>
    public Vector IRotate(Vector v) => new(X.Dot(v), Y.Dot(v), Z.Dot(v));

    /// <summary>ConcatTransforms' rotation: this, then <paramref name="inner"/> in its frame.</summary>
    public GrabMatrix Concat(in GrabMatrix inner) => new(Rotate(inner.X), Rotate(inner.Y), Rotate(inner.Z));

    /// <summary>MatrixInvert's rotation: the transpose.</summary>
    public GrabMatrix Transposed => new(new Vector(X.X, Y.X, Z.X), new Vector(X.Y, Y.Y, Z.Y), new Vector(X.Z, Y.Z, Z.Z));
}

/// <summary>The mathlib the grab controller reads (mathlib_base.cpp, vector.h).</summary>
public static class GrabMath
{
    private const float DegToRad = MathF.PI / 180f;
    private const float RadToDeg = 180f / MathF.PI;

    /// <summary>AngleMatrix's rotation for (pitch, yaw, roll) in degrees.</summary>
    public static GrabMatrix AngleMatrix(QAngle angles)
    {
        (float sy, float cy) = MathF.SinCos(DegToRad * angles.Yaw);
        (float sp, float cp) = MathF.SinCos(DegToRad * angles.Pitch);
        (float sr, float cr) = MathF.SinCos(DegToRad * angles.Roll);

        float crcy = cr * cy;
        float crsy = cr * sy;
        float srcy = sr * cy;
        float srsy = sr * sy;

        return new GrabMatrix(
            new Vector(cp * cy, cp * sy, -sp),
            new Vector(sp * srcy - crsy, sp * srsy + crcy, sr * cp),
            new Vector(sp * crcy + srsy, sp * crsy - srcy, cr * cp));
    }

    /// <summary>MatrixAngles: the (pitch, yaw, roll) of a rotation.</summary>
    public static QAngle MatrixAngles(in GrabMatrix m)
    {
        Vector forward = m.X;
        Vector left = m.Y;
        float up2 = m.Z.Z;

        float xyDist = MathF.Sqrt(forward.X * forward.X + forward.Y * forward.Y);

        // enough here to get angles?
        if (xyDist > 0.001f)
        {
            return new QAngle(RadToDeg * MathF.Atan2(-forward.Z, xyDist),
                              RadToDeg * MathF.Atan2(forward.Y, forward.X),
                              RadToDeg * MathF.Atan2(left.Z, up2));
        }

        // forward is mostly Z, gimbal lock
        return new QAngle(RadToDeg * MathF.Atan2(-forward.Z, xyDist),
                          RadToDeg * MathF.Atan2(-left.X, left.Y),
                          0f);
    }

    /// <summary>TransformAnglesToLocalSpace: <paramref name="angles"/> in <paramref name="parent"/>'s frame.</summary>
    public static QAngle TransformAnglesToLocalSpace(QAngle angles, in GrabMatrix parent)
        => MatrixAngles(parent.Transposed.Concat(AngleMatrix(angles)));

    /// <summary>TransformAnglesToWorldSpace: <paramref name="angles"/> out of <paramref name="parent"/>'s frame.</summary>
    public static QAngle TransformAnglesToWorldSpace(QAngle angles, in GrabMatrix parent)
        => MatrixAngles(parent.Concat(AngleMatrix(angles)));

    /// <summary>VectorITransform: a world point in the frame at <paramref name="origin"/>.</summary>
    public static Vector ITransform(Vector point, in GrabMatrix rotation, Vector origin)
        => rotation.IRotate(point - origin);

    /// <summary>AngleDistance: the shortest signed difference, in (−180, 180].</summary>
    public static float AngleDistance(float next, float cur)
    {
        float delta = next - cur;

        if (delta < -180)
            delta += 360;
        else if (delta > 180)
            delta -= 360;

        return delta;
    }

    /// <summary>Approach: <paramref name="value"/> moved towards <paramref name="target"/> by at most <paramref name="speed"/>.</summary>
    public static float Approach(float target, float value, float speed)
    {
        float delta = target - value;

        if (delta > speed)
            value += speed;
        else if (delta < -speed)
            value -= speed;
        else
            value = target;

        return value;
    }

    /// <summary>VectorNormalize: normalises in place, answering the old length.</summary>
    public static float Normalize(ref Vector v)
    {
        float length = v.Length;

        if (length != 0f)
            v *= 1f / length;

        return length;
    }

    /// <summary>CalcClosestPointOnLine: the projection of <paramref name="p"/> on the infinite line a-b.</summary>
    public static Vector ClosestPointOnLine(Vector p, Vector a, Vector b)
    {
        Vector dir = b - a;
        float div = dir.Dot(dir);
        float t = div < 0.00001f ? 0f : (dir.Dot(p) - dir.Dot(a)) / div;
        return a + dir * t;
    }
}
