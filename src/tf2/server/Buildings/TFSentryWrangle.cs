//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Buildings;

/// <summary>
/// The Wrangler's use layer (<c>TF2.Server.Buildings</c>): the laser
/// pointer's triggers, carried to every standing sentry the command's
/// builder owns — stock asks the one <c>GetObjectOfType(OBJ_SENTRYGUN)</c>
/// returns (tf_weapon_laser_pointer.cpp:201); the walk over
/// <see cref="ITFBuildings"/> is the same ask, the engineer building one
/// sentry at a time. The aim is the command's own trace
/// (<c>UpdateLaserDot</c>, :289-300): eyes along the view to
/// <c>MAX_TRACE_LENGTH</c> on <c>MASK_SOLID</c>, the holder ignored — the
/// teammates-and-their-objects filter of :302 rides with the client lane's
/// dot rendering, the aim itself resting on whatever the wall trace ends on.
/// </summary>
[SideService(Side.Server, As = [typeof(ITFSentryWrangle)])]
public sealed class TFSentryWrangle(ITFBuildings buildings, IEnginePlayers players, IEngineEntities entities,
                                    IEngineWorldQuery world, IEntityDirectory directory) : ITFSentryWrangle
{
    /// <summary><c>MAX_TRACE_LENGTH</c> (util_shared.h): how far the dot reaches.</summary>
    public const float MaxTraceLength = 56756f;

    /// <summary>The holder's dot (<c>m_hLaserDot</c>), one per holder: made by the first ask past the deploy's half-second and taken with the holster.</summary>
    private readonly Dictionary<int, EntityHandle> _dots = new();

    public void Aim(int ownerIndex)
    {
        EntityHandle player = players.GetHandle(ownerIndex);
        if (player == EntityHandle.None)
            return;

        Vector eye = entities.GetEyePosition(player);
        Vector angles = entities.GetEyeAngles(player);
        float pitch = angles.X * (MathF.PI / 180f);
        float yaw = angles.Y * (MathF.PI / 180f);

        // The view's forward, pitch and yaw both in it (AngleVectors' answer
        // the dot rides): the degrees-to-radians trig inline.
        Vector dir = new(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));
        Vector far = eye + dir * MaxTraceLength;

        TraceResult trace = world.TraceLine(eye, far, TraceMask.Solid, player);
        Vector aim = trace.Hit ? trace.End : far;

        UpdateDot(ownerIndex, player, trace, aim);

        if (Sentries(ownerIndex) is not { } sentries)
            return;

        EntityHandle assist = PlayerHit(trace);

        foreach (TFSentryGun sentry in sentries)
        {
            sentry.LaserAim(aim);

            // The lased player becomes the turret's brief auto-aim
            // (:317-323): the pointer's own find beside the dot's word.
            if (assist != EntityHandle.None)
                sentry.SetAutoAimTarget(assist);
        }
    }

    public void Fire(int ownerIndex)
    {
        if (Sentries(ownerIndex) is not { } sentries)
            return;

        foreach (TFSentryGun sentry in sentries)
            sentry.LaserFire();
    }

    public void FireRockets(int ownerIndex)
    {
        if (Sentries(ownerIndex) is not { } sentries)
            return;

        foreach (TFSentryGun sentry in sentries)
            sentry.LaserFireRockets();
    }

    public void Release(int ownerIndex)
    {
        if (Sentries(ownerIndex) is { } sentries)
        {
            foreach (TFSentryGun sentry in sentries)
                sentry.LaserReleased();
        }

        // DestroyLaserDot (:265-284): the pointer left the hands, the dot
        // goes with it.
        if (_dots.Remove(ownerIndex, out EntityHandle handle) && entities.IsValid(handle))
            entities.Remove(handle);
    }

    /// <summary>The builder's standing sentries; null when the index names nobody.</summary>
    private IEnumerable<TFSentryGun>? Sentries(int ownerIndex)
    {
        EntityHandle builder = players.GetHandle(ownerIndex);
        if (builder == EntityHandle.None)
            return null;

        return buildings.OwnedBy(builder, TFObjectType.Sentrygun, null).OfType<TFSentryGun>();
    }

    /// <summary>
    /// <c>CreateLaserDot</c>/<c>UpdateLaserDot</c>'s dot half
    /// (<c>tf_weapon_laser_pointer.cpp:240-310</c>): the holder's one
    /// <c>env_laserdot</c>, made once and then stood where the view's trace
    /// stopped, every ask. A handle gone from the world (its own removal, a
    /// map change) is replaced on the next ask rather than reused.
    /// </summary>
    private void UpdateDot(int ownerIndex, EntityHandle player, TraceResult trace, Vector end)
    {
        EntityHandle handle;
        bool madeNow;

        if (_dots.TryGetValue(ownerIndex, out handle) && entities.IsValid(handle))
        {
            madeNow = false;
        }
        else
        {
            handle = entities.Create(EntityNames.EnvLaserdot);
            if (handle == EntityHandle.None || !entities.IsValid(handle))
                return;

            // CLaserDot::Create: stand the dot at the holder and spawn — the
            // directory learns the instance from the spawn, so the dress
            // rides after it (the dropped weapon's order).
            entities.SetOrigin(handle, entities.GetOrigin(player));
            entities.Spawn(handle);
            _dots[ownerIndex] = handle;
            madeNow = true;
        }

        if (directory.Get(handle) is not LaserDot dot)
            return;

        // SetOwnerEntity (:256): the one owner write, at the creation — the
        // holder does not change hands while the dot lives.
        if (madeNow)
            dot.OwnedBy(player);

        dot.State.Team = (byte)players.GetTeam(player);

        // SetLaserPosition (:300-310): the end of the trace, the face it
        // stopped on, and what stands on that face.
        dot.SetLaserPosition(end, trace.Normal);

        dot.TargetEntity = HurtworthyHit(trace);
    }

    /// <summary>
    /// What the view's trace stopped on, hurt-worthiness judged as the shared
    /// dot rule has it (<c>Arsenal</c>'s same arms): the world (entry 0) is
    /// nobody, and only something that takes damage counts.
    /// </summary>
    private EntityHandle HurtworthyHit(TraceResult trace)
    {
        EntityHandle ent = trace.Entity;
        bool hurtable = ent != EntityHandle.None && entities.GetIndex(ent) != 0
                        && (entities.IsPlayer(ent) || directory.Get(ent) is { TakesDamage: true });
        return hurtable ? ent : EntityHandle.None;
    }

    /// <summary>
    /// The trace's hit when it is a hurtworthy <em>player</em>
    /// (<c>DidHitNonWorldEntity</c>, m_takedamage and IsPlayer,
    /// <c>tf_weapon_laser_pointer.cpp:308-316</c>): what the turret's brief
    /// auto-aim latches onto (:317-323).
    /// </summary>
    private EntityHandle PlayerHit(TraceResult trace)
    {
        EntityHandle ent = trace.Entity;
        return ent != EntityHandle.None && entities.GetIndex(ent) != 0 && entities.IsPlayer(ent)
            ? ent
            : EntityHandle.None;
    }
}
