using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;

namespace SourceSharp.Tests;

/// <summary>
/// vphysics as the joint tables see it, for their facts: bodies with a pose,
/// a velocity, a mass and an inertia, and every create, destroy and setter
/// recorded with the block it was handed.
/// </summary>
internal sealed class FakeVPhysicsJoints : IVPhysicsJoints
{
    public sealed class Body
    {
        public Float3x4 Pose = Float3x4.Identity;
        public Vector Velocity = default;
        public Vector AngularVelocity = default;
        public float Mass = 10f;
        public bool Moveable = true;
        public ushort GameFlags;
        public int Wakes;
        public float LinearDrag = 1f;
        public float AngularDrag = 1f;
        public (Vector Linear, Vector Angular) VelocityOffset = default;
        public (Vector Force, Vector Point)? VelocityOffsetAsked;
    }

    public readonly Dictionary<nint, Body> Bodies = new();
    public readonly List<string> Calls = new();
    private nint _next = 0x7000;

    public nint Answer = -1; // -1: hand out a fresh pointer; anything else is the answer

    public object? LastBlock;
    public (nint Reference, nint Attached) LastEnds;
    public readonly Dictionary<nint, (float Speed, float Max)> AngularMotors = new();
    public readonly Dictionary<nint, (float Speed, float Max)> LinearMotors = new();
    public readonly Dictionary<nint, (nint Reference, nint Attached)> Ends = new();
    public readonly Dictionary<nint, float> SpringConstants = new();
    public readonly Dictionary<nint, float> SpringDampings = new();
    public readonly Dictionary<nint, float> SpringLengths = new();
    public readonly Dictionary<nint, List<nint>> Attached = new();
    public readonly Dictionary<nint, int> ControllerWakes = new();
    public nint ConstraintHandler;

    public Body Add(nint pointer)
    {
        var body = new Body();
        Bodies[pointer] = body;
        return body;
    }

    public Body Of(nint pointer) => Bodies.TryGetValue(pointer, out var b) ? b : Add(pointer);

    /// <summary>A pose translated to <paramref name="origin"/>, axes identity.</summary>
    public static Float3x4 At(Vector origin)
    {
        var m = Float3x4.Identity;
        m[0, 3] = origin.X;
        m[1, 3] = origin.Y;
        m[2, 3] = origin.Z;
        return m;
    }

    private nint Made(string call, nint reference, nint attached, object block)
    {
        Calls.Add(call);
        LastBlock = block;
        LastEnds = (reference, attached);
        nint made = Answer == -1 ? _next++ : Answer;
        if (made != 0)
            Ends[made] = (reference, attached);
        return made;
    }

    public nint CreateFixedConstraint(nint environment, nint reference, nint attached, in ConstraintFixedParams p) => Made("CreateFixed", reference, attached, p);
    public nint CreateHingeConstraint(nint environment, nint reference, nint attached, in ConstraintHingeParams p) => Made("CreateHinge", reference, attached, p);
    public nint CreateBallsocketConstraint(nint environment, nint reference, nint attached, in ConstraintBallsocketParams p) => Made("CreateBallsocket", reference, attached, p);
    public nint CreateLengthConstraint(nint environment, nint reference, nint attached, in ConstraintLengthParams p) => Made("CreateLength", reference, attached, p);
    public nint CreateSlidingConstraint(nint environment, nint reference, nint attached, in ConstraintSlidingParams p) => Made("CreateSliding", reference, attached, p);
    public nint CreatePulleyConstraint(nint environment, nint reference, nint attached, in ConstraintPulleyParams p) => Made("CreatePulley", reference, attached, p);
    public nint CreateRagdollConstraint(nint environment, nint reference, nint attached, in ConstraintRagdollParams p) => Made("CreateRagdoll", reference, attached, p);

    public void DestroyConstraint(nint environment, nint constraint) => Calls.Add($"DestroyConstraint {constraint:x}");
    public void Deactivate(nint constraint) => Calls.Add($"Deactivate {constraint:x}");
    public void SetAngularMotor(nint constraint, float rotSpeed, float maxAngularImpulse) => AngularMotors[constraint] = (rotSpeed, maxAngularImpulse);
    public void SetLinearMotor(nint constraint, float speed, float maxLinearImpulse) => LinearMotors[constraint] = (speed, maxLinearImpulse);
    public nint ReferenceObject(nint constraint) => Ends.TryGetValue(constraint, out var e) ? e.Reference : 0;
    public nint AttachedObject(nint constraint) => Ends.TryGetValue(constraint, out var e) ? e.Attached : 0;

    public nint CreateSpring(nint environment, nint start, nint end, in SpringParams p) => Made("CreateSpring", start, end, p);
    public void DestroySpring(nint environment, nint spring) => Calls.Add($"DestroySpring {spring:x}");
    public void SetSpringConstant(nint spring, float constant) => SpringConstants[spring] = constant;
    public void SetSpringDamping(nint spring, float damping) => SpringDampings[spring] = damping;
    public void SetSpringLength(nint spring, float length) => SpringLengths[spring] = length;

    public nint CreateMotionController(nint environment, nint handler)
    {
        Calls.Add($"CreateMotionController {handler:x}");
        nint made = Answer == -1 ? _next++ : Answer;
        if (made != 0)
            Attached[made] = new List<nint>();
        return made;
    }

    public void DestroyMotionController(nint environment, nint controller) => Calls.Add($"DestroyMotionController {controller:x}");
    public void AttachObject(nint controller, nint physicsObject, bool checkIfAlreadyAttached) => Attached[controller].Add(physicsObject);
    public void DetachObject(nint controller, nint physicsObject) => Attached[controller].Remove(physicsObject);
    public void WakeObjects(nint controller) => ControllerWakes[controller] = ControllerWakes.GetValueOrDefault(controller) + 1;
    public void SetConstraintEventHandler(nint environment, nint handler) => ConstraintHandler = handler;

    public Float3x4 PositionMatrix(nint physicsObject) => Of(physicsObject).Pose;
    public Vector Position(nint physicsObject) { var m = Of(physicsObject).Pose; return new Vector(m[0, 3], m[1, 3], m[2, 3]); }

    public Vector WorldToLocal(nint physicsObject, Vector world)
    {
        // The fakes' poses are translations: local is world minus the origin.
        Vector origin = Position(physicsObject);
        return world - origin;
    }

    public (Vector Linear, Vector Angular) VelocityOffset(nint physicsObject, Vector force, Vector worldPosition)
    {
        Body b = Of(physicsObject);
        b.VelocityOffsetAsked = (force, worldPosition);
        return b.VelocityOffset;
    }

    public void Wake(nint physicsObject) => Of(physicsObject).Wakes++;
    public ushort GameFlags(nint physicsObject) => Of(physicsObject).GameFlags;
    public void SetGameFlags(nint physicsObject, ushort flags) => Of(physicsObject).GameFlags = flags;
    public bool IsMoveable(nint physicsObject) => Of(physicsObject).Moveable;
    public float Mass(nint physicsObject) => Of(physicsObject).Mass;
    public (Vector Linear, Vector Angular) Velocity(nint physicsObject) => (Of(physicsObject).Velocity, Of(physicsObject).AngularVelocity);
    public float LinearDrag(nint physicsObject, Vector unitDirection) => Of(physicsObject).LinearDrag;
    public float AngularDrag(nint physicsObject, Vector objectSpaceRotationAxis) => Of(physicsObject).AngularDrag;
}

/// <summary>A world for the joint tables' facts: an environment, the world's object, and entities by handle.</summary>
internal sealed class FakeJointWorld : IPhysicsJointWorld
{
    public nint Env = 0xE000;
    public nint World = 0x1000;
    public readonly Dictionary<EntityHandle, nint> Objects = new();
    public readonly Dictionary<nint, Vector> Inertias = new();
    public float Tick = 0.015f;
    public float Now = 10f;
    public float GravityValue = 600f;

    public nint Environment() => Env;
    public nint WorldObject() => World;
    public bool IsValid(EntityHandle entity) => Objects.ContainsKey(entity);
    public nint PhysicsObject(EntityHandle entity) => Objects.TryGetValue(entity, out var o) ? o : 0;
    public Vector Inertia(nint physicsObject) => Inertias.TryGetValue(physicsObject, out var i) ? i : new Vector(1f, 1f, 1f);
    public float TickInterval() => Tick;
    public float CurTime() => Now;
    public float Gravity() => GravityValue;
}
