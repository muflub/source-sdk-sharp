//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System;
using System.Collections.Generic;
using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Server.Interop;

namespace SourceSharp.Server;

/// <summary>
/// The motion-controller table (plan_physics_bones_native_zero 2.4), ported
/// from shell_motioncontrollers.cpp. A controller is a force applied every
/// physics tick: vphysics asks managed's IMotionEvent for it per object it
/// acts on, and <see cref="Simulate"/> answers from the controller's
/// <see cref="MotionState"/>. Every controller shares the one managed-built
/// IMotionEvent table, and vphysics hands it the controller it asks for
/// (<c>m_handler-&gt;Simulate( this, ... )</c>),
/// which is the key the state is found by.
/// </summary>
public sealed class PhysicsMotionControllers : IEngineMotionControllers, ICustomMotionControllers, IMotionAnswers, IGameLifecycle, ISaveCustom
{
    private readonly PhysicsJointTable _table;
    private readonly IVPhysicsJoints _vphysics;
    private readonly IPhysicsJointWorld _world;
    private readonly ILog _log;

    /// <summary>The controllers by the vphysics pointer Simulate is handed.</summary>
    private readonly Dictionary<nint, MotionEntry> _byController = new();

    public PhysicsMotionControllers(PhysicsJointTable table, IVPhysicsJoints vphysics, IPhysicsJointWorld world, ILog log)
    {
        _table = table;
        _vphysics = vphysics;
        _world = world;
        _log = log;
    }

    /// <summary>
    /// The IMotionEvent every controller is created around: the managed-built
    /// table's address, set once the side registry has built it
    /// (ServerModule.BuildManagedRoot). 0 refuses to create one.
    /// </summary>
    public nint Handler { get; set; }

    /// <summary>Simulate calls answered, for the per-tick cost readout.</summary>
    public long Simulated { get; private set; }

    /// <summary>
    /// One live controller. The entity is kept so the motor can read the
    /// object's inertia when its parameters change: a handle rather than an
    /// object pointer, because a prop can rebuild its physics.
    /// </summary>
    internal sealed class MotionEntry(nint controller, EntityHandle entity, MotionState state)
    {
        public nint Controller { get; } = controller;
        public EntityHandle Entity { get; } = entity;
        public MotionState State { get; } = state;
    }

    private MotionEntry? Find(uint motion, MotionControllerKind? kind = null)
    {
        var entry = _table.Lookup<MotionEntry>(motion, PhysicsJointKind.Controller);
        if (entry is null || (kind is { } k && entry.State.Kind != k))
            return null;
        return entry;
    }

    public int Count() => _table.CountOf(PhysicsJointKind.Controller);

    /// <summary>A live controller's state, for the dev readout and the facts; null for a stale id.</summary>
    public MotionState? StateOf(uint motion) => Find(motion)?.State;

    /// <summary>
    /// <see cref="EntityHandle.None"/> asks for a controller with NOTHING
    /// attached, which is what a volume wants. Any other handle that resolves
    /// to no physics object still reports: an entity that died between the
    /// decision and this call is a bug, and the warning tells them apart.
    /// </summary>
    public uint Create(EntityHandle entity, int kind)
    {
        nint env = _world.Environment();
        if (env == 0)
            return 0u;

        nint physicsObject = 0;
        if (entity != EntityHandle.None)
        {
            physicsObject = _world.PhysicsObject(entity);
            if (physicsObject == 0)
            {
                _log.Warning("[SourceSharp] motion controller: entity has no physics object\n");
                return 0u;
            }
        }

        if (kind < (int)MotionControllerKind.ConstantForce || kind > (int)MotionControllerKind.Medium)
        {
            _log.Warning($"[SourceSharp] motion controller: unknown kind {kind}\n");
            return 0u;
        }

        if (Handler == 0)
            return 0u;

        nint controller = _vphysics.CreateMotionController(env, Handler);
        if (controller == 0)
            return 0u;

        if (physicsObject != 0)
            _vphysics.AttachObject(controller, physicsObject, true);

        var entry = new MotionEntry(controller, entity, new MotionState((MotionControllerKind)kind));
        uint id = _table.Bind(PhysicsJointKind.Controller, entry);

        if (id == 0u)
        {
            // Refused rather than made room for: an id the caller cannot hold
            // would leave a live controller pushing an object nothing can stop.
            _log.Warning("[SourceSharp] motion controller: the physics id table is full\n");
            _vphysics.DestroyMotionController(env, controller);
            return 0u;
        }

        _byController[controller] = entry;
        return id;
    }

    /// <summary>
    /// A <see cref="MotionControllerKind.Custom"/> controller on
    /// <paramref name="entity"/>'s physics object, simulated by
    /// <paramref name="simulate"/>: the id, or 0 when there is no environment,
    /// no object, or no room. <see cref="Destroy"/> frees it; a hot reload
    /// does not carry it.
    /// </summary>
    public uint CreateCustom(EntityHandle entity, IMotionSimulate simulate)
    {
        ArgumentNullException.ThrowIfNull(simulate);

        nint env = _world.Environment();
        if (env == 0 || Handler == 0)
            return 0u;

        nint physicsObject = _world.PhysicsObject(entity);
        if (physicsObject == 0)
            return 0u;

        nint controller = _vphysics.CreateMotionController(env, Handler);
        if (controller == 0)
            return 0u;

        _vphysics.AttachObject(controller, physicsObject, true);

        var entry = new MotionEntry(controller, entity,
                                    new MotionState(MotionControllerKind.Custom) { Custom = simulate });
        uint id = _table.Bind(PhysicsJointKind.Controller, entry);

        if (id == 0u)
        {
            _log.Warning("[SourceSharp] motion controller: the physics id table is full\n");
            _vphysics.DestroyMotionController(env, controller);
            return 0u;
        }

        _byController[controller] = entry;
        return id;
    }

    public void SetForce(uint motion, Vector linear, Vector angular, bool localSpace)
    {
        if (Find(motion, MotionControllerKind.ConstantForce) is not { } entry)
            return;

        entry.State.Linear = linear;
        entry.State.Angular = angular;
        entry.State.Local = localSpace;
        _vphysics.WakeObjects(entry.Controller);
    }

    /// <summary>
    /// Target speed about an axis. The torque ceiling depends on the object's
    /// inertia along that axis, read here from its pose as it stands.
    /// </summary>
    public void SetMotor(uint motion, Vector axis, float targetSpeed, float inertiaFactor, float torqueAcceleration)
    {
        if (Find(motion, MotionControllerKind.Motor) is not { } entry)
            return;

        MotionState state = entry.State;
        state.Axis = axis;
        state.Speed = targetSpeed;
        state.InertiaFactor = inertiaFactor;
        state.Damping = 1f;

        nint physicsObject = _world.PhysicsObject(entry.Entity);

        if (physicsObject != 0 && torqueAcceleration > 0f)
        {
            Float3x4 matrix = _vphysics.PositionMatrix(physicsObject);
            Vector motorAxisLs = JointMath.IRotate(state.Axis, matrix);
            float inertia = MotionSimulation.DotProductAbs(_world.Inertia(physicsObject), motorAxisLs);
            state.MaxTorque = inertia * inertiaFactor * torqueAcceleration;
        }

        _vphysics.WakeObjects(entry.Controller);
    }

    /// <summary>
    /// A force at a point, decomposed by vphysics into push and twist (it
    /// knows the mass distribution). Either half can be dropped afterwards.
    /// </summary>
    public (Vector Linear, Vector Angular) SetForceAtPoint(uint motion, Vector force, Vector worldPoint, int components, bool localSpace)
    {
        if (Find(motion, MotionControllerKind.ConstantForce) is not { } entry)
            return default;

        nint physicsObject = _world.PhysicsObject(entry.Entity);
        if (physicsObject == 0)
            return default;

        (Vector linear, Vector angular) = _vphysics.VelocityOffset(physicsObject, force, worldPoint);

        // In the object's own frame, the push is turned into it as stock's
        // CThrustController::CalcThrust and CalculateVelocityOffsetLocal do
        // (WorldToLocalVector); the twist already is. Native left the push in
        // world numbers and read them as local ones.
        if (localSpace)
            linear = JointMath.IRotate(linear, _vphysics.PositionMatrix(physicsObject));

        if ((components & (int)ForceComponents.Push) == 0)
            linear = Vector.Zero;
        if ((components & (int)ForceComponents.Twist) == 0)
            angular = Vector.Zero;

        entry.State.Linear = linear;
        entry.State.Angular = angular;
        entry.State.Local = localSpace;
        _vphysics.WakeObjects(entry.Controller);
        return (linear, angular);
    }

    public void SetUpright(uint motion, Vector goalAxis, float angularLimit, bool dampAllRotation, bool active)
    {
        if (Find(motion, MotionControllerKind.KeepUpright) is not { } entry)
            return;

        entry.State.WorldGoalAxis = goalAxis;
        entry.State.AngularLimit = angularLimit;
        entry.State.DampAllRotation = dampAllRotation;
        entry.State.Active = active;

        if (active)
            _vphysics.WakeObjects(entry.Controller);
    }

    /// <summary>Turns a constant force on or off without discarding what it was set to.</summary>
    public void SetActive(uint motion, bool active)
    {
        if (Find(motion, MotionControllerKind.ConstantForce) is not { } entry)
            return;

        entry.State.Active = active;

        if (active)
            _vphysics.WakeObjects(entry.Controller);
    }

    /// <summary>Quiet about being asked twice: vphysics keeps a set.</summary>
    public void Attach(uint motion, EntityHandle entity)
    {
        if (Find(motion) is not { } entry)
            return;

        nint physicsObject = _world.PhysicsObject(entity);
        if (physicsObject == 0)
            return;

        _vphysics.AttachObject(entry.Controller, physicsObject, true);
    }

    public void Detach(uint motion, EntityHandle entity)
    {
        if (Find(motion) is not { } entry)
            return;

        nint physicsObject = _world.PhysicsObject(entity);
        if (physicsObject == 0)
            return;

        _vphysics.DetachObject(entry.Controller, physicsObject);
    }

    /// <summary>
    /// The nine numbers of a medium, one setting. Setting the limit here while
    /// a ramp is in flight moves the ramp's DESTINATION and leaves it running,
    /// as stock does.
    /// </summary>
    public void SetMedium(uint motion, float gravityScale, float airDensity, float linearLimit, float linearLimitDelta,
                          float linearScale, float angularLimit, float angularScale, float linearForce,
                          Vector linearForceAngles)
    {
        if (Find(motion, MotionControllerKind.Medium) is not { } entry)
            return;

        MotionState state = entry.State;
        state.GravityScale = gravityScale;
        state.AddAirDensity = airDensity;
        state.LinearLimit = linearLimit;
        state.LinearLimitDelta = linearLimitDelta;
        state.LinearScale = linearScale;
        state.MediumAngularLimit = angularLimit;
        state.AngularScale = angularScale;
        state.LinearForce = linearForce;
        state.LinearForceAngles = linearForceAngles;

        // Something already resting in the medium would otherwise never feel
        // the change: a sleeping object is not simulated.
        _vphysics.WakeObjects(entry.Controller);
    }

    public void RampLinearLimit(uint motion, float targetLimit, float duration)
    {
        if (Find(motion, MotionControllerKind.Medium) is not { } entry)
            return;

        MotionSimulation.RampLinearLimit(entry.State, targetLimit, duration, _world.CurTime());
        _vphysics.WakeObjects(entry.Controller);
    }

    /// <summary>
    /// A property of the controller, not an argument to either force setter:
    /// it says what the numbers MEAN, for a force set through the centre and
    /// one set at a point alike.
    /// </summary>
    public void SetForceIsAcceleration(uint motion, bool acceleration)
    {
        if (Find(motion, MotionControllerKind.ConstantForce) is not { } entry)
            return;

        entry.State.Acceleration = acceleration;
        _vphysics.WakeObjects(entry.Controller);
    }

    public void Destroy(uint motion)
    {
        if (Find(motion) is not { } entry)
            return;

        _vphysics.DestroyMotionController(_world.Environment(), entry.Controller);
        _byController.Remove(entry.Controller);

        // The id first: freeing it advances the slot's serial, so every copy
        // of it anybody kept stops resolving here.
        _table.Free(motion, PhysicsJointKind.Controller);
    }

    /// <summary>
    /// IMotionEvent::Simulate, asked mid-step for each object a controller acts
    /// on. A controller this table no longer holds (the level is going) answers
    /// nothing.
    /// </summary>
    public int Simulate(nint controller, nint physicsObject, float deltaTime, out Vector linear, out Vector angular)
    {
        Simulated++;

        if (!_byController.TryGetValue(controller, out var entry))
        {
            linear = Vector.Zero;
            angular = Vector.Zero;
            return (int)MotionSimResult.Nothing;
        }

        return (int)SimulateState(entry.State, physicsObject, deltaTime, out linear, out angular);
    }

    /// <summary>
    /// One state's answer for one object, over this table's vphysics and
    /// world: what <see cref="Simulate"/> does once it has found the
    /// controller, and what the cost bench (<c>cs_sv_physics_bench</c>) times.
    /// </summary>
    public MotionSimResult SimulateState(MotionState state, nint physicsObject, float deltaTime,
                                         out Vector linear, out Vector angular)
        => MotionSimulation.Simulate(state, physicsObject, deltaTime, _vphysics, _world, out linear, out angular);

    /// <summary>
    /// Everything goes at level change: the environment is torn down with the
    /// level, so the controllers are already invalid by the time this runs.
    /// </summary>
    public void LevelShutdown()
    {
        _table.FreeAll(PhysicsJointKind.Controller);
        _byController.Clear();
    }

    int IGameLifecycle.LifecycleOrder => (int)LifecycleStage.PhysicsMotionControllers;

    void IGameLifecycle.OnLevelShutdown() => LevelShutdown();

    /// <summary>
    /// A reload keeps the environment, its controllers and the one IMotionEvent
    /// block they hold (re-patched in place); the entities carry their ids. So
    /// each controller's row crosses with its id, its pointer and its state.
    /// </summary>
    void ISaveCustom.OnSave(ISaveWriter writer)
    {
        foreach (var (id, entry) in _table.All<MotionEntry>(PhysicsJointKind.Controller))
        {
            MotionState s = entry.State;

            // A custom controller's Simulate is its owner's object, which a
            // reload does not rebuild: the row would restore a controller that
            // nothing simulates.
            if (s.Kind == MotionControllerKind.Custom)
                continue;

            writer.AddRow()
                  .Set("id", unchecked((int)id))
                  .Set("ptr", ((long)entry.Controller).ToString(CultureInfo.InvariantCulture))
                  .Set("entity", entry.Entity)
                  .Set("kind", (int)s.Kind)
                  .Set("state", string.Join(' ', Fields(s)));
        }
    }

    void ISaveCustom.OnRestore(ISaveReader reader)
    {
        LevelShutdown();

        foreach (ReloadRow row in reader.Rows)
        {
            if (!long.TryParse(row.GetString("ptr"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long raw)
                || raw == 0)
                continue;

            var state = new MotionState((MotionControllerKind)row.GetInt("kind"));
            Restore(state, row.GetString("state"));

            var entry = new MotionEntry((nint)raw, row.GetHandle("entity"), state);
            if (_table.Restore(unchecked((uint)row.GetInt("id")), PhysicsJointKind.Controller, entry))
                _byController[entry.Controller] = entry;
        }
    }

    /// <summary>Every float of a state, round-trip formatted, in one fixed order.</summary>
    private static IEnumerable<string> Fields(MotionState s)
    {
        foreach (float f in Floats(s))
            yield return BitConverter.SingleToInt32Bits(f).ToString(CultureInfo.InvariantCulture);
    }

    private static float[] Floats(MotionState s) =>
    [
        s.Linear.X, s.Linear.Y, s.Linear.Z, s.Angular.X, s.Angular.Y, s.Angular.Z,
        s.Local ? 1f : 0f, s.Active ? 1f : 0f, s.Acceleration ? 1f : 0f,
        s.Axis.X, s.Axis.Y, s.Axis.Z, s.Speed, s.MaxTorque, s.InertiaFactor,
        s.LastSpeed, s.LastAcceleration, s.LastForce, s.Damping,
        s.WorldGoalAxis.X, s.WorldGoalAxis.Y, s.WorldGoalAxis.Z,
        s.LocalTestAxis.X, s.LocalTestAxis.Y, s.LocalTestAxis.Z,
        s.AngularLimit, s.DampAllRotation ? 1f : 0f,
        s.GravityScale, s.AddAirDensity, s.LinearLimit, s.LinearLimitDelta, s.LinearScale,
        s.MediumAngularLimit, s.AngularScale, s.LinearForce,
        s.LinearForceAngles.X, s.LinearForceAngles.Y, s.LinearForceAngles.Z,
        s.LinearLimitTime, s.LinearLimitStart, s.LinearLimitStartTime,
    ];

    private static void Restore(MotionState s, string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var f = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            f[i] = int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bits)
                ? BitConverter.Int32BitsToSingle(bits) : 0f;

        if (f.Length < 41)
            return;

        int k = 0;
        s.Linear = new Vector(f[k++], f[k++], f[k++]);
        s.Angular = new Vector(f[k++], f[k++], f[k++]);
        s.Local = f[k++] != 0f;
        s.Active = f[k++] != 0f;
        s.Acceleration = f[k++] != 0f;
        s.Axis = new Vector(f[k++], f[k++], f[k++]);
        s.Speed = f[k++];
        s.MaxTorque = f[k++];
        s.InertiaFactor = f[k++];
        s.LastSpeed = f[k++];
        s.LastAcceleration = f[k++];
        s.LastForce = f[k++];
        s.Damping = f[k++];
        s.WorldGoalAxis = new Vector(f[k++], f[k++], f[k++]);
        s.LocalTestAxis = new Vector(f[k++], f[k++], f[k++]);
        s.AngularLimit = f[k++];
        s.DampAllRotation = f[k++] != 0f;
        s.GravityScale = f[k++];
        s.AddAirDensity = f[k++];
        s.LinearLimit = f[k++];
        s.LinearLimitDelta = f[k++];
        s.LinearScale = f[k++];
        s.MediumAngularLimit = f[k++];
        s.AngularScale = f[k++];
        s.LinearForce = f[k++];
        s.LinearForceAngles = new Vector(f[k++], f[k++], f[k++]);
        s.LinearLimitTime = f[k++];
        s.LinearLimitStart = f[k++];
        s.LinearLimitStartTime = f[k];
    }
}
