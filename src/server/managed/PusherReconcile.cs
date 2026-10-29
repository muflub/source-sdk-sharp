//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared.Bones;

namespace SourceSharp.Server;

/// <summary>
/// CBaseEntity::VPhysicsUpdatePusher and CheckPushedEntity: after the solver
/// has run, a mover whose shadow could not get where it was sent was blocked
/// by vphysics. It goes back by the share of the step its shadow lost (or to
/// where it started the frame, with what it pushed, when it pushed somebody)
/// and is told what blocked it, as a door told so reverses.
///
/// <para>
/// Only a mover that is not SOLID_BSP ("SOLID_BSP cannot compute consistent
/// collisions wrt vphysics, so don't allow vphysics to block"), only an
/// unparented one (stock reconciles from the hierarchy's root), and once a
/// frame (stock: on the final vphysics tick).
/// </para>
/// </summary>
public sealed class PusherReconcile(EntityMotion motion, IEngineEntities entities, IEngineEntityPhysics physics,
                                    IVPhysicsPusherCalls pusher, IVPhysicsSolverObjects objects)
{
    private const int MaxContacts = 32;

    /// <summary>How many reconciles found the mover blocked. Instrumentation.</summary>
    public int Blocked { get; private set; }

    /// <summary>The solver moved this entity's object; a pusher reconciles with what it found.</summary>
    public void Updated(EntityHandle entity)
    {
        if (!motion.TryPusherStep(entity, out EntityMotion.PusherStep step) || !motion.BeginReconcile(entity))
            return;

        if (entities.GetSolidType(entity) == SolidType.Bsp || entities.GetParent(entity) != EntityHandle.None)
            return;

        nint shadow = physics.PhysicsObject(entity);
        if (shadow == 0 || !pusher.TryShadow(shadow, out ShadowState state))
            return;

        float movetime = step.FrameTime;
        if (movetime <= 0f)
            return;

        Vector origin = entities.GetOrigin(entity);
        QAngle angles = QAngle.FromVector(entities.GetAngles(entity));

        float fraction = Fraction(entity, step, state, origin, angles, movetime);
        if (fraction < 0f)
            return;

        // add 1% noise for bouncing in collision
        float moveback = movetime * fraction;
        if (moveback <= 0f || moveback < movetime * 0.01f)
            return;

        EntityHandle blocker = EntityHandle.None;
        nint other = pusher.ContactObject(shadow);
        if (other != 0)
            blocker = PhysicsGameData.Owner(objects.GameData(other));

        IReadOnlyList<(EntityHandle Entity, Vector PushVec)> list = motion.PushListOf(entity);
        if (step.HasList)
        {
            // The whole frame undone: the mover where it started, each entity
            // it pushed back by its push, and the blocker the one pressing
            // hardest against the push that was not itself pushed.
            motion.MoveBack(entity, step.StartOrigin, step.StartAngles);
            foreach (var (pushed, pushVec) in list)
                motion.UndoPush(pushed, pushVec);

            EntityHandle hardest = FindPhysicsBlocker(shadow, list, list.Count > 0 ? list[0].PushVec : Vector.Zero);
            if (hardest != EntityHandle.None)
                blocker = hardest;
        }
        else
        {
            Vector back = origin;
            QAngle turned = angles;
            if (!Same(step.Velocity, Vector.Zero))
                back = origin - step.Velocity * moveback;
            if (step.AngularVelocity != default)
            {
                turned = new QAngle(angles.Pitch - step.AngularVelocity.Pitch * moveback,
                                    angles.Yaw - step.AngularVelocity.Yaw * moveback,
                                    angles.Roll - step.AngularVelocity.Roll * moveback);
            }

            motion.MoveBack(entity, back, turned);
        }

        Blocked++;
        if (blocker != EntityHandle.None)
            motion.PhysicsBlocked(entity, blocker);
    }

    /// <summary>
    /// CheckPushedEntity for the root: how much of the step to give back, or
    /// -1 when the shadow kept up. A position slammed by something outside
    /// the push resends the shadow instead.
    /// </summary>
    private float Fraction(EntityHandle entity, in EntityMotion.PusherStep step, in ShadowState state,
                           Vector origin, QAngle angles, float movetime)
    {
        float fraction = -1f;

        if (!Same(step.Velocity, Vector.Zero))
        {
            // project error onto the axis of movement
            float speed = step.Velocity.Length;
            Vector dir = step.Velocity * (1f / speed);
            float targetAmount = state.Target.Dot(dir);
            float currentAmount = state.Position.Dot(dir);
            float entityAmount = origin.Dot(dir);

            // A target out of step with the entity is a position slammed from
            // outside the push: stock resends the shadow and gives nothing back,
            // which UpdateShadows' teleport does here the next frame.
            if (targetAmount - entityAmount <= 1f)
            {
                float dist = targetAmount - currentAmount;
                if (dist > 1f)
                {
                    float expectedDist = speed * movetime;
                    fraction = expectedDist <= 0f ? 1f : Math.Clamp(dist / expectedDist, 0f, 1f);
                }
            }
        }

        if (step.AngularVelocity != default)
        {
            float deltaAngle = BoneSetupMath.RotationDeltaAxisAngle(Bone(state.Angles), Bone(angles)).Angle;
            if (MathF.Abs(deltaAngle) > 0.5f)
            {
                float deltaTarget = BoneSetupMath.RotationDeltaAxisAngle(Bone(state.Angles), Bone(state.TargetAngles)).Angle;
                if (MathF.Abs(deltaTarget) > 0.01f)
                {
                    float expectedDist = Length(step.AngularVelocity) * movetime;
                    float t = expectedDist != 0f ? MathF.Abs(deltaAngle / expectedDist) : 1f;
                    fraction = MathF.Max(fraction, Math.Clamp(t, 0f, 1f));
                }
            }
        }

        return fraction;
    }

    /// <summary>
    /// FindPhysicsBlocker: of what the shadow touches, the one pressing hardest
    /// against the push that the push did not move; else the first contact.
    /// </summary>
    private EntityHandle FindPhysicsBlocker(nint shadow, IReadOnlyList<(EntityHandle Entity, Vector PushVec)> list,
                                            Vector pushVel)
    {
        Span<SnapshotContact> contacts = stackalloc SnapshotContact[MaxContacts];
        int count = objects.Contacts(shadow, contacts);

        EntityHandle blocker = EntityHandle.None;
        float maxForce = 0f;
        for (int i = 0; i < count; i++)
        {
            EntityHandle other = PhysicsGameData.Owner(objects.GameData(contacts[i].Other));
            bool inList = false;
            foreach (var (pushed, _) in list)
            {
                if (pushed == other)
                {
                    inList = true;
                    break;
                }
            }

            float dot = pushVel.Dot(contacts[i].Normal * contacts[i].NormalForce);
            if (blocker == EntityHandle.None || (!inList && dot > maxForce))
            {
                blocker = other;
                if (!inList)
                    maxForce = dot;
            }
        }

        return blocker;
    }

    private static BoneAngle Bone(QAngle a) => new(a.Pitch, a.Yaw, a.Roll);

    private static float Length(QAngle a) => MathF.Sqrt(a.Pitch * a.Pitch + a.Yaw * a.Yaw + a.Roll * a.Roll);

    private static bool Same(in Vector a, in Vector b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
}
