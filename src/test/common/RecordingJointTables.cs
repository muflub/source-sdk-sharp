using SourceSharp.Abi;

namespace SourceSharp.Tests;

/// <summary>
/// The constraint, spring and motion-controller tables, recorded into a
/// <see cref="RecordingEngine"/> under the member names they had when they
/// were engine areas ("Constraints.Create", "MotionControllers.SetForce", ...),
/// so an entity's conversation with them still reads as one ordered log
/// beside its engine calls (plan_physics_bones_native_zero 2.4: the tables
/// are the server's own now, and the double's assembly cannot see them).
/// </summary>
public sealed class RecordingJointTables(RecordingEngine engine)
    : IEngineConstraints, IEngineSprings, IEngineMotionControllers
{
    // Constraints
    int IEngineConstraints.Count() => engine.RecordAnswer<int>("Constraints.Count");
    uint IEngineConstraints.Create(EntityHandle a, EntityHandle b, int kind, ReadOnlySpan<float> parameters) => engine.RecordAnswer<uint>("Constraints.Create", a, b, kind, parameters.ToArray());
    void IEngineConstraints.Destroy(uint constraint) => engine.RecordCall("Constraints.Destroy", constraint);
    bool IEngineConstraints.IsValid(uint constraint) => engine.RecordAnswer<bool>("Constraints.IsValid", constraint);
    void IEngineConstraints.SetLinearMotor(uint constraint, float unitsPerSecond, float systemLoadScale) => engine.RecordCall("Constraints.SetLinearMotor", constraint, unitsPerSecond, systemLoadScale);
    void IEngineConstraints.SetHingeVelocity(uint constraint, float degreesPerSecond, float systemLoadScale) => engine.RecordCall("Constraints.SetHingeVelocity", constraint, degreesPerSecond, systemLoadScale);

    // Springs
    int IEngineSprings.Count() => engine.RecordAnswer<int>("Springs.Count");
    uint IEngineSprings.Create(EntityHandle a, EntityHandle b, Vector start, Vector end, float constant, float damping, float relativeDamping, float naturalLength, bool onlyStretch) => engine.RecordAnswer<uint>("Springs.Create", a, b, start, end, constant, damping, relativeDamping, naturalLength, onlyStretch);
    void IEngineSprings.Destroy(uint spring) => engine.RecordCall("Springs.Destroy", spring);
    bool IEngineSprings.IsValid(uint spring) => engine.RecordAnswer<bool>("Springs.IsValid", spring);
    void IEngineSprings.SetConstant(uint spring, float constant) => engine.RecordCall("Springs.SetConstant", spring, constant);
    void IEngineSprings.SetDamping(uint spring, float damping) => engine.RecordCall("Springs.SetDamping", spring, damping);
    void IEngineSprings.SetLength(uint spring, float length) => engine.RecordCall("Springs.SetLength", spring, length);

    // MotionControllers
    void IEngineMotionControllers.Attach(uint motion, EntityHandle entity) => engine.RecordCall("MotionControllers.Attach", motion, entity);
    int IEngineMotionControllers.Count() => engine.RecordAnswer<int>("MotionControllers.Count");
    uint IEngineMotionControllers.Create(EntityHandle entity, int kind) => engine.RecordAnswer<uint>("MotionControllers.Create", entity, kind);
    void IEngineMotionControllers.Destroy(uint motion) => engine.RecordCall("MotionControllers.Destroy", motion);
    void IEngineMotionControllers.Detach(uint motion, EntityHandle entity) => engine.RecordCall("MotionControllers.Detach", motion, entity);
    void IEngineMotionControllers.RampLinearLimit(uint motion, float targetLimit, float duration) => engine.RecordCall("MotionControllers.RampLinearLimit", motion, targetLimit, duration);
    void IEngineMotionControllers.SetActive(uint motion, bool active) => engine.RecordCall("MotionControllers.SetActive", motion, active);
    void IEngineMotionControllers.SetForce(uint motion, Vector linear, Vector angular, bool localSpace) => engine.RecordCall("MotionControllers.SetForce", motion, linear, angular, localSpace);
    (Vector Linear, Vector Angular) IEngineMotionControllers.SetForceAtPoint(uint motion, Vector force, Vector worldPoint, int components, bool localSpace) => engine.RecordAnswer<(Vector, Vector)>("MotionControllers.SetForceAtPoint", motion, force, worldPoint, components, localSpace);
    void IEngineMotionControllers.SetForceIsAcceleration(uint motion, bool acceleration) => engine.RecordCall("MotionControllers.SetForceIsAcceleration", motion, acceleration);
    void IEngineMotionControllers.SetMedium(uint motion, float gravityScale, float airDensity, float linearLimit, float linearLimitDelta, float linearScale, float angularLimit, float angularScale, float linearForce, Vector linearForceAngles) => engine.RecordCall("MotionControllers.SetMedium", motion, gravityScale, airDensity, linearLimit, linearLimitDelta, linearScale, angularLimit, angularScale, linearForce, linearForceAngles);
    void IEngineMotionControllers.SetMotor(uint motion, Vector axis, float targetSpeed, float inertiaFactor, float torqueAcceleration) => engine.RecordCall("MotionControllers.SetMotor", motion, axis, targetSpeed, inertiaFactor, torqueAcceleration);
    void IEngineMotionControllers.SetUpright(uint motion, Vector goalAxis, float angularLimit, bool dampAllRotation, bool active) => engine.RecordCall("MotionControllers.SetUpright", motion, goalAxis, angularLimit, dampAllRotation, active);
}
