//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//
using SourceSharp.Abi;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// vphysics' PreCollision and PostCollision, answered inside the step
/// (plan_physics_managed B2.2), as stock's CCollisionEvent did them
/// (physics.cpp) and native's POST_COLLISION record carried them:
///
/// <list type="bullet">
/// <item>PreCollision: a player-held object striking something owned that is
/// not a player is raised to at least 10 in/s and 1 deg/s, so the hit
/// registers; then each side's velocity before the impact is kept.</item>
/// <item>PostCollision: the impact, answered by the collision response
/// (sounds, dust, shake) and posted to the router with everything the
/// response decides from (the contact, the two sides' mass, flags,
/// staticness, owner move type, velocities before and after, and inertia),
/// because neither object outlives the callback.</item>
/// </list>
///
/// An impact with no managed owner on either side is not recorded, as the
/// ring refused it.
/// </summary>
public sealed class PhysicsCollisionImpacts(IVPhysicsObjects objects, IEngineEntityPhysics physics,
                                            IEngineNetworking entities, PhysicsCollisionResponse response,
                                            Action<PhysicsEvent> post) : IPhysicsCollisionSink
{
    /// <summary>FVPHYSICS_PLAYER_HELD.</summary>
    private const ushort PlayerHeld = 0x0004;

    /// <summary>CALLBACK_SHADOW_COLLISION (vphysics_interface.h).</summary>
    private const ushort CallbackShadowCollision = 0x0010;

    private const float HeldMinSpeed = 10f;
    private const float HeldMinAngularSpeed = 1f;

    // The pair PreCollision read, for the PostCollision that follows it: the
    // solver calls the two back to back for one contact (stock kept them on
    // m_gameEvent).
    private Vector _preLinear0, _preLinear1, _preAngular0, _preAngular1;

    public void PreCollision(in VCollisionEvent ev)
    {
        _preLinear0 = _preLinear1 = _preAngular0 = _preAngular1 = Vector.Zero;
        for (int side = 0; side < 2; side++)
        {
            nint obj = ev.Object(side);
            if (obj == 0)
                continue;

            if ((objects.GameFlags(obj) & PlayerHeld) != 0)
            {
                EntityHandle other = Owner(ev.Object(1 - side));
                if (other != EntityHandle.None && (entities.EntityGetState(other, out _) & EntityState.Player) == 0)
                {
                    var (linear, angular) = objects.Velocity(obj);
                    objects.SetVelocity(obj, AtLeast(linear, HeldMinSpeed), AtLeast(angular, HeldMinAngularSpeed));
                }
            }

            var (preLinear, preAngular) = objects.Velocity(obj);
            if (side == 0)
                (_preLinear0, _preAngular0) = (preLinear, preAngular);
            else
                (_preLinear1, _preAngular1) = (preLinear, preAngular);
        }
    }

    public void PostCollision(in VCollisionEvent ev)
    {
        EntityHandle owner0 = Owner(ev.Object0);
        EntityHandle owner1 = Owner(ev.Object1);
        if (owner0 == EntityHandle.None && owner1 == EntityHandle.None)
            return;

        Vector point = objects.ContactPoint(ev.InternalData);
        Vector normal = objects.SurfaceNormal(ev.InternalData);
        var record = new PhysicsEvent
        {
            KindRaw = (uint)PhysicsEventKind.PostCollision,
            Entity0 = owner0 == EntityHandle.None ? PhysicsEvent.NoEntity : owner0.Value,
            Entity1 = owner1 == EntityHandle.None ? PhysicsEvent.NoEntity : owner1.Value,
            SurfaceProps0 = (uint)ev.SurfaceProps0,
            SurfaceProps1 = (uint)ev.SurfaceProps1,
            PointX = point.X, PointY = point.Y, PointZ = point.Z,
            NormalX = normal.X, NormalY = normal.Y, NormalZ = normal.Z,
            Speed = ev.CollisionSpeed,
            DeltaCollisionTime = ev.DeltaCollisionTime,
        };

        if (ev.Object0 != 0)
        {
            nint obj = ev.Object0;
            record.Mass0 = objects.Mass(obj);
            record.GameFlags0 = objects.GameFlags(obj);
            record.Static0 = objects.IsStatic(obj) ? 1 : 0;
            record.MoveType0 = MoveTypeOf(owner0);
            (record.PreVelocity0X, record.PreVelocity0Y, record.PreVelocity0Z) = (_preLinear0.X, _preLinear0.Y, _preLinear0.Z);
            (record.PreAngularVelocity0X, record.PreAngularVelocity0Y, record.PreAngularVelocity0Z) = (_preAngular0.X, _preAngular0.Y, _preAngular0.Z);
            Vector after = objects.Velocity(obj).Linear;
            (record.PostVelocity0X, record.PostVelocity0Y, record.PostVelocity0Z) = (after.X, after.Y, after.Z);
            Vector inertia = physics.Inertia(obj);
            (record.Inertia0X, record.Inertia0Y, record.Inertia0Z) = (inertia.X, inertia.Y, inertia.Z);
            record.Flags |= SideFlags(obj, PhysicsEvent.FlagMoveable0, PhysicsEvent.FlagShadow0);
        }

        if (ev.Object1 != 0)
        {
            nint obj = ev.Object1;
            record.Mass1 = objects.Mass(obj);
            record.GameFlags1 = objects.GameFlags(obj);
            record.Static1 = objects.IsStatic(obj) ? 1 : 0;
            record.MoveType1 = MoveTypeOf(owner1);
            (record.PreVelocity1X, record.PreVelocity1Y, record.PreVelocity1Z) = (_preLinear1.X, _preLinear1.Y, _preLinear1.Z);
            (record.PreAngularVelocity1X, record.PreAngularVelocity1Y, record.PreAngularVelocity1Z) = (_preAngular1.X, _preAngular1.Y, _preAngular1.Z);
            Vector after = objects.Velocity(obj).Linear;
            (record.PostVelocity1X, record.PostVelocity1Y, record.PostVelocity1Z) = (after.X, after.Y, after.Z);
            Vector inertia = physics.Inertia(obj);
            (record.Inertia1X, record.Inertia1Y, record.Inertia1Z) = (inertia.X, inertia.Y, inertia.Z);
            record.Flags |= SideFlags(obj, PhysicsEvent.FlagMoveable1, PhysicsEvent.FlagShadow1);
        }

        if (ev.IsCollision != 0)
            record.Flags |= PhysicsEvent.FlagCollision;
        if (ev.IsShadowCollision != 0)
            record.Flags |= PhysicsEvent.FlagShadowCollision;

        response.Respond(in record);
        post(record);
    }

    private EntityHandle Owner(nint obj) => obj == 0 ? EntityHandle.None : PhysicsGameData.Owner(objects.GameData(obj));

    /// <summary>The owner's MOVETYPE_*, or none for an unowned side.</summary>
    private int MoveTypeOf(EntityHandle owner)
    {
        if (owner == EntityHandle.None)
            return 0;

        entities.EntityGetState(owner, out MoveType moveType);
        return (int)moveType;
    }

    private uint SideFlags(nint obj, uint moveable, uint shadow)
        => (objects.IsMoveable(obj) ? moveable : 0u)
         | ((objects.CallbackFlags(obj) & CallbackShadowCollision) != 0 ? shadow : 0u);

    /// <summary>VectorNormalize, then the length raised to <paramref name="min"/>; zero stays zero.</summary>
    private static Vector AtLeast(Vector v, float min)
    {
        float length = v.Length;
        return length > 0f ? v * (MathF.Max(length, min) / length) : v;
    }
}
