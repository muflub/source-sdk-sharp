using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The vphysics calls managed's constraint, spring and motion-controller
/// tables make (plan_physics_bones_native_zero 2.4), each one a direct call
/// through a generated vtable: <see cref="PhysicsEnvironmentVtbl"/> for the
/// creators, <see cref="PhysicsConstraintVtbl"/>, <see cref="PhysicsSpringVtbl"/>
/// and <see cref="PhysicsMotionControllerVtbl"/> for what they return, and
/// <see cref="PhysicsObjectVtbl"/> for the bodies they join. A 0 pointer is a
/// no-op answering zero, as stock's NULL checks are.
/// </summary>
public interface IVPhysicsJoints
{
    nint CreateFixedConstraint(nint environment, nint reference, nint attached, in ConstraintFixedParams p);

    nint CreateHingeConstraint(nint environment, nint reference, nint attached, in ConstraintHingeParams p);

    nint CreateBallsocketConstraint(nint environment, nint reference, nint attached, in ConstraintBallsocketParams p);

    nint CreateLengthConstraint(nint environment, nint reference, nint attached, in ConstraintLengthParams p);

    nint CreateSlidingConstraint(nint environment, nint reference, nint attached, in ConstraintSlidingParams p);

    nint CreatePulleyConstraint(nint environment, nint reference, nint attached, in ConstraintPulleyParams p);

    nint CreateRagdollConstraint(nint environment, nint reference, nint attached, in ConstraintRagdollParams p);

    /// <summary><c>IPhysicsEnvironment::DestroyConstraint</c>.</summary>
    void DestroyConstraint(nint environment, nint constraint);

    /// <summary><c>IPhysicsConstraint::Deactivate</c>.</summary>
    void Deactivate(nint constraint);

    /// <summary><c>IPhysicsConstraint::SetAngularMotor</c>.</summary>
    void SetAngularMotor(nint constraint, float rotSpeed, float maxAngularImpulse);

    /// <summary><c>IPhysicsConstraint::SetLinearMotor</c>.</summary>
    void SetLinearMotor(nint constraint, float speed, float maxLinearImpulse);

    /// <summary><c>IPhysicsConstraint::GetReferenceObject</c>.</summary>
    nint ReferenceObject(nint constraint);

    /// <summary><c>IPhysicsConstraint::GetAttachedObject</c>.</summary>
    nint AttachedObject(nint constraint);

    /// <summary><c>IPhysicsEnvironment::CreateSpring</c>.</summary>
    nint CreateSpring(nint environment, nint start, nint end, in SpringParams p);

    /// <summary><c>IPhysicsEnvironment::DestroySpring</c>.</summary>
    void DestroySpring(nint environment, nint spring);

    /// <summary><c>IPhysicsSpring::SetSpringConstant</c>.</summary>
    void SetSpringConstant(nint spring, float constant);

    /// <summary><c>IPhysicsSpring::SetSpringDamping</c>.</summary>
    void SetSpringDamping(nint spring, float damping);

    /// <summary><c>IPhysicsSpring::SetSpringLength</c>.</summary>
    void SetSpringLength(nint spring, float length);

    /// <summary><c>IPhysicsEnvironment::CreateMotionController</c>, around a managed-built IMotionEvent.</summary>
    nint CreateMotionController(nint environment, nint handler);

    /// <summary><c>IPhysicsEnvironment::DestroyMotionController</c>.</summary>
    void DestroyMotionController(nint environment, nint controller);

    /// <summary><c>IPhysicsMotionController::AttachObject</c>.</summary>
    void AttachObject(nint controller, nint physicsObject, bool checkIfAlreadyAttached);

    /// <summary><c>IPhysicsMotionController::DetachObject</c>.</summary>
    void DetachObject(nint controller, nint physicsObject);

    /// <summary><c>IPhysicsMotionController::WakeObjects</c>.</summary>
    void WakeObjects(nint controller);

    /// <summary><c>IPhysicsEnvironment::SetConstraintEventHandler</c>.</summary>
    void SetConstraintEventHandler(nint environment, nint handler);

    /// <summary><c>IPhysicsObject::GetPositionMatrix</c>.</summary>
    Float3x4 PositionMatrix(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetPosition</c>'s position.</summary>
    Vector Position(nint physicsObject);

    /// <summary><c>IPhysicsObject::WorldToLocal</c>.</summary>
    Vector WorldToLocal(nint physicsObject, Vector world);

    /// <summary><c>IPhysicsObject::CalculateVelocityOffset</c>: the velocity and the local angular impulse.</summary>
    (Vector Linear, Vector Angular) VelocityOffset(nint physicsObject, Vector force, Vector worldPosition);

    /// <summary><c>IPhysicsObject::Wake</c>.</summary>
    void Wake(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetGameFlags</c>.</summary>
    ushort GameFlags(nint physicsObject);

    /// <summary><c>IPhysicsObject::SetGameFlags</c>.</summary>
    void SetGameFlags(nint physicsObject, ushort flags);

    /// <summary><c>IPhysicsObject::IsMoveable</c>.</summary>
    bool IsMoveable(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetMass</c>.</summary>
    float Mass(nint physicsObject);

    /// <summary><c>IPhysicsObject::GetVelocity</c>: linear, and angular in local space.</summary>
    (Vector Linear, Vector Angular) Velocity(nint physicsObject);

    /// <summary><c>IPhysicsObject::CalculateLinearDrag</c>.</summary>
    float LinearDrag(nint physicsObject, Vector unitDirection);

    /// <summary><c>IPhysicsObject::CalculateAngularDrag</c>.</summary>
    float AngularDrag(nint physicsObject, Vector objectSpaceRotationAxis);
}

/// <summary>The real vphysics. Holds nothing: every pointer is handed in.</summary>
public sealed unsafe class VPhysicsJoints : IVPhysicsJoints
{
    private static PhysicsEnvironmentVtbl* Env(nint environment) => *(PhysicsEnvironmentVtbl**)environment;

    private static PhysicsConstraintVtbl* Constraint(nint constraint) => *(PhysicsConstraintVtbl**)constraint;

    private static PhysicsSpringVtbl* Spring(nint spring) => *(PhysicsSpringVtbl**)spring;

    private static PhysicsMotionControllerVtbl* Controller(nint controller) => *(PhysicsMotionControllerVtbl**)controller;

    private static PhysicsObjectVtbl* Object(nint physicsObject) => *(PhysicsObjectVtbl**)physicsObject;

    public nint CreateFixedConstraint(nint environment, nint reference, nint attached, in ConstraintFixedParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintFixedParams* block = &p)
            return (nint)Env(environment)->CreateFixedConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public nint CreateHingeConstraint(nint environment, nint reference, nint attached, in ConstraintHingeParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintHingeParams* block = &p)
            return (nint)Env(environment)->CreateHingeConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public nint CreateBallsocketConstraint(nint environment, nint reference, nint attached, in ConstraintBallsocketParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintBallsocketParams* block = &p)
            return (nint)Env(environment)->CreateBallsocketConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public nint CreateLengthConstraint(nint environment, nint reference, nint attached, in ConstraintLengthParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintLengthParams* block = &p)
            return (nint)Env(environment)->CreateLengthConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public nint CreateSlidingConstraint(nint environment, nint reference, nint attached, in ConstraintSlidingParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintSlidingParams* block = &p)
            return (nint)Env(environment)->CreateSlidingConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public nint CreatePulleyConstraint(nint environment, nint reference, nint attached, in ConstraintPulleyParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintPulleyParams* block = &p)
            return (nint)Env(environment)->CreatePulleyConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public nint CreateRagdollConstraint(nint environment, nint reference, nint attached, in ConstraintRagdollParams p)
    {
        if (environment == 0)
            return 0;
        fixed (ConstraintRagdollParams* block = &p)
            return (nint)Env(environment)->CreateRagdollConstraint((void*)environment, (void*)reference, (void*)attached, null, block);
    }

    public void DestroyConstraint(nint environment, nint constraint)
    {
        if (environment != 0 && constraint != 0)
            Env(environment)->DestroyConstraint((void*)environment, (void*)constraint);
    }

    public void Deactivate(nint constraint)
    {
        if (constraint != 0)
            Constraint(constraint)->Deactivate((void*)constraint);
    }

    public void SetAngularMotor(nint constraint, float rotSpeed, float maxAngularImpulse)
    {
        if (constraint != 0)
            Constraint(constraint)->SetAngularMotor((void*)constraint, rotSpeed, maxAngularImpulse);
    }

    public void SetLinearMotor(nint constraint, float speed, float maxLinearImpulse)
    {
        if (constraint != 0)
            Constraint(constraint)->SetLinearMotor((void*)constraint, speed, maxLinearImpulse);
    }

    public nint ReferenceObject(nint constraint)
        => constraint == 0 ? 0 : (nint)Constraint(constraint)->GetReferenceObject((void*)constraint);

    public nint AttachedObject(nint constraint)
        => constraint == 0 ? 0 : (nint)Constraint(constraint)->GetAttachedObject((void*)constraint);

    public nint CreateSpring(nint environment, nint start, nint end, in SpringParams p)
    {
        if (environment == 0)
            return 0;
        fixed (SpringParams* block = &p)
            return (nint)Env(environment)->CreateSpring((void*)environment, (void*)start, (void*)end, block);
    }

    public void DestroySpring(nint environment, nint spring)
    {
        if (environment != 0 && spring != 0)
            Env(environment)->DestroySpring((void*)environment, (void*)spring);
    }

    public void SetSpringConstant(nint spring, float constant)
    {
        if (spring != 0)
            Spring(spring)->SetSpringConstant((void*)spring, constant);
    }

    public void SetSpringDamping(nint spring, float damping)
    {
        if (spring != 0)
            Spring(spring)->SetSpringDamping((void*)spring, damping);
    }

    public void SetSpringLength(nint spring, float length)
    {
        if (spring != 0)
            Spring(spring)->SetSpringLength((void*)spring, length);
    }

    public nint CreateMotionController(nint environment, nint handler)
        => environment == 0 ? 0 : (nint)Env(environment)->CreateMotionController((void*)environment, (void*)handler);

    public void DestroyMotionController(nint environment, nint controller)
    {
        if (environment != 0 && controller != 0)
            Env(environment)->DestroyMotionController((void*)environment, (void*)controller);
    }

    public void AttachObject(nint controller, nint physicsObject, bool checkIfAlreadyAttached)
    {
        if (controller != 0 && physicsObject != 0)
            Controller(controller)->AttachObject((void*)controller, (void*)physicsObject, checkIfAlreadyAttached ? (byte)1 : (byte)0);
    }

    public void DetachObject(nint controller, nint physicsObject)
    {
        if (controller != 0 && physicsObject != 0)
            Controller(controller)->DetachObject((void*)controller, (void*)physicsObject);
    }

    public void WakeObjects(nint controller)
    {
        if (controller != 0)
            Controller(controller)->WakeObjects((void*)controller);
    }

    public void SetConstraintEventHandler(nint environment, nint handler)
    {
        if (environment != 0)
            Env(environment)->SetConstraintEventHandler((void*)environment, (void*)handler);
    }

    public Float3x4 PositionMatrix(nint physicsObject)
    {
        Float3x4 m = default;
        if (physicsObject != 0)
            Object(physicsObject)->GetPositionMatrix((void*)physicsObject, &m);
        return m;
    }

    public Vector Position(nint physicsObject)
    {
        Vector position = default;
        if (physicsObject != 0)
            Object(physicsObject)->GetPosition((void*)physicsObject, &position, null);
        return position;
    }

    public Vector WorldToLocal(nint physicsObject, Vector world)
    {
        Vector local = default;
        if (physicsObject != 0)
            Object(physicsObject)->WorldToLocal((void*)physicsObject, &local, &world);
        return local;
    }

    public (Vector Linear, Vector Angular) VelocityOffset(nint physicsObject, Vector force, Vector worldPosition)
    {
        Vector linear = default, angular = default;
        if (physicsObject != 0)
            Object(physicsObject)->CalculateVelocityOffset((void*)physicsObject, &force, &worldPosition, &linear, &angular);
        return (linear, angular);
    }

    public void Wake(nint physicsObject)
    {
        if (physicsObject != 0)
            Object(physicsObject)->Wake((void*)physicsObject);
    }

    public ushort GameFlags(nint physicsObject)
        => physicsObject == 0 ? (ushort)0 : Object(physicsObject)->GetGameFlags((void*)physicsObject);

    public void SetGameFlags(nint physicsObject, ushort flags)
    {
        if (physicsObject != 0)
            Object(physicsObject)->SetGameFlags((void*)physicsObject, flags);
    }

    public bool IsMoveable(nint physicsObject)
        => physicsObject != 0 && Object(physicsObject)->IsMoveable((void*)physicsObject) != 0;

    public float Mass(nint physicsObject)
        => physicsObject == 0 ? 0f : Object(physicsObject)->GetMass((void*)physicsObject);

    public (Vector Linear, Vector Angular) Velocity(nint physicsObject)
    {
        Vector linear = default, angular = default;
        if (physicsObject != 0)
            Object(physicsObject)->GetVelocity((void*)physicsObject, &linear, &angular);
        return (linear, angular);
    }

    public float LinearDrag(nint physicsObject, Vector unitDirection)
        => physicsObject == 0 ? 0f : Object(physicsObject)->CalculateLinearDrag((void*)physicsObject, &unitDirection);

    public float AngularDrag(nint physicsObject, Vector objectSpaceRotationAxis)
        => physicsObject == 0 ? 0f : Object(physicsObject)->CalculateAngularDrag((void*)physicsObject, &objectSpaceRotationAxis);
}
