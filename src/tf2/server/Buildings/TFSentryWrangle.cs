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
                                    IEngineWorldQuery world) : ITFSentryWrangle
{
    /// <summary><c>MAX_TRACE_LENGTH</c> (util_shared.h): how far the dot reaches.</summary>
    public const float MaxTraceLength = 56756f;

    public void Aim(int ownerIndex)
    {
        if (Sentries(ownerIndex) is not { } sentries)
            return;

        Vector aim = TraceAim(ownerIndex);
        foreach (TFSentryGun sentry in sentries)
            sentry.LaserAim(aim);
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
        if (Sentries(ownerIndex) is not { } sentries)
            return;

        foreach (TFSentryGun sentry in sentries)
            sentry.LaserReleased();
    }

    /// <summary>The builder's standing sentries; null when the index names nobody.</summary>
    private IEnumerable<TFSentryGun>? Sentries(int ownerIndex)
    {
        EntityHandle builder = players.GetHandle(ownerIndex);
        if (builder == EntityHandle.None)
            return null;

        return buildings.OwnedBy(builder, TFObjectType.Sentrygun, null).OfType<TFSentryGun>();
    }

    private Vector TraceAim(int ownerIndex)
    {
        EntityHandle player = players.GetHandle(ownerIndex);
        Vector eye = entities.GetEyePosition(player);
        Vector angles = entities.GetEyeAngles(player);
        float pitch = angles.X * (MathF.PI / 180f);
        float yaw = angles.Y * (MathF.PI / 180f);

        // The view's forward, pitch and yaw both in it (AngleVectors' answer
        // the dot rides): the degrees-to-radians trig inline.
        Vector dir = new(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch));
        Vector end = eye + dir * MaxTraceLength;

        TraceResult trace = world.TraceLine(eye, end, TraceMask.Solid, player);
        return trace.Hit ? trace.End : end;
    }
}
