//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>The route over the server's <see cref="ServerPlayerMoves"/>.</summary>
public sealed class ServerPlayerMovement : IServerPlayerMovementRoute
{
    private readonly EngineSide api;
    private readonly ILog _log;
    private readonly IEntitiesServerArea _entities;
    private readonly ServerPlayerMoves moves;
    private readonly ServerPlayerCommands commands;
    private readonly HashSet<EntityHandle> _refused = new();

    internal ServerPlayerMovement(EngineSide api, ILog log, IEntitiesServerArea entities, ServerPlayerMoves moves,
                                  ServerPlayerCommands commands)
    {
        this.api = api ?? throw new ArgumentNullException(nameof(api));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        this.moves = moves ?? throw new ArgumentNullException(nameof(moves));
        this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    /// <summary>The route over <paramref name="engine"/>'s side and its entities area, taken here once.</summary>
    public static ServerPlayerMovement For(ModuleEngine engine, ServerPlayerMoves moves, ServerPlayerCommands commands)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return new ServerPlayerMovement(engine.Api, engine.Log, engine.Attachment.Areas.Require<IEntitiesServerArea>(), moves, commands);
    }

    /// <summary>The lift off the floor an upward push gives (standalone_basevelocity.h's SOURCESHARP_BASEVELOCITY_UNSTICK, until T4g).</summary>
    public const float Unstick = 1.0f;

    /// <summary>The record of <paramref name="entity"/> when it is a player, else null.</summary>
    public ServerPlayerMove? Player(EntityHandle entity)
        => entity != EntityHandle.None && api.EntityIsPlayer(_entities, entity) ? moves.For(entity) : null;

    public bool PlayerState(EntityHandle entity, out MoveType moveType, out bool onGround)
    {
        moveType = MoveType.None;
        onGround = false;

        if (Player(entity) is not { } m)
            return false;

        moveType = (MoveType)m.Move.MoveType;
        onGround = (m.Move.Flags & GameMovement.FlOnGround) != 0;
        return true;
    }

    public MoveType SetMoveType(EntityHandle player, MoveType moveType)
    {
        if (Player(player) is not { } m)
            return MoveType.None;

        var was = (MoveType)m.Move.MoveType;
        m.Move.MoveType = (int)moveType;
        return was;
    }

    public void SetSlowMovement(EntityHandle player, bool slow)
    {
        if (Player(player) is { } m)
            m.Move.SlowMovement = slow;
    }

    public void SetSuitPower(EntityHandle player, float power)
        => moves.For(player).Move.SuitPower = power;

    public void SwallowUse(EntityHandle player)
        => moves.For(player).Move.OldButtons |= GameMovement.InUse;

    public void QueueUseDenySound(EntityHandle player)
        => moves.For(player).Move.PlayUseDenySound = true;

    public bool TakeUseDenySound(EntityHandle player)
    {
        MoveState s = moves.For(player).Move;
        bool due = s.PlayUseDenySound;
        s.PlayUseDenySound = false;
        return due;
    }

    public void SetObserverMode(EntityHandle player, int mode)
    {
        if (Player(player) is not { } m)
            return;

        m.Move.ObserverMode = mode;
        m.Move.MoveType = mode != 0 ? GameMovement.MoveTypeNoClip : GameMovement.MoveTypeWalk;
    }

    /// <summary>
    /// shell_worldquery.cpp's player arm (standalone_basevelocity.h): a push
    /// adds to a base velocity not yet consumed this frame and replaces one
    /// that was; an upward push on a player standing on something lifts them
    /// off it first, or the move would cancel it; FL_BASEVELOCITY says it is
    /// this frame's.
    ///
    /// Only a player has the channel: a managed brush is moved by its own
    /// stepper or the solver, neither of which reads base velocity, and a
    /// handle that names nothing has nothing to push. Either refusal is said
    /// once per entity (the native arm's CSaidOncePerId), because a belt pushes
    /// every frame and a push field at 20Hz, and a warning every tick is the
    /// noise that gets a diagnostic switched off.
    /// </summary>
    public bool AddBaseVelocity(EntityHandle entity, Vector velocity)
    {
        if (Player(entity) is not { } m)
        {
            if (FirstRefusal(entity))
                _log.Warning($"[SourceSharp] AddBaseVelocity: '{api.EntityGetClassName(_entities, entity)}' (handle " +
                            $"{entity.Value}) is not a player, so it has no base-velocity channel; the push " +
                            "was ignored. Push a physics object with ApplyForceCenter, or move it.\n");

            return false;
        }

        MoveState s = m.Move;
        Vector push = BaseVelocityPush(s.Flags, s.BaseVelocity, velocity);

        if (ReleasesGround(push.Z, (s.Flags & GameMovement.FlOnGround) != 0))
        {
            m.Ground = EntityHandle.None;
            s.Flags &= ~GameMovement.FlOnGround;
            api.EntitySetOrigin(entity, api.EntityGetOrigin(entity) + new Vector(0f, 0f, Unstick));
        }

        s.BaseVelocity = push;
        s.Flags |= ServerCommandRules.FlBaseVelocity;
        return true;
    }

    /// <summary>
    /// Stock's <c>CBaseEntity::ApplyAbsVelocityImpulse</c> (baseentity_shared.cpp)
    /// on a player, which is never MOVETYPE_VPHYSICS, so stock's arm is the one
    /// that adds the impulse to <c>GetAbsVelocity()</c>. A player's velocity is
    /// its record's (plan_server_native_v2 T4g): the shadow's, which the
    /// physics-object arm would write, is overwritten by the next move.
    ///
    /// <para>
    /// The impulse passes stock's safety check first: a zero impulse adds
    /// nothing, <c>CheckEntityVelocity</c> clamps a fast one and discards an
    /// absurd one, with stock's warning. <paramref name="clearGround"/> is the
    /// caller's <c>SetGroundEntity( NULL )</c> after it (CTriggerPush's
    /// push-once arm): the ground and FL_ONGROUND go, and nothing more. No
    /// unstick: that is the base-velocity arm's (<see cref="AddBaseVelocity"/>),
    /// not this one's. It is let go even when the impulse is not added, as the
    /// caller's separate call would be.
    /// </para>
    /// </summary>
    public bool ApplyVelocityImpulse(EntityHandle entity, Vector impulse, bool clearGround)
    {
        if (Player(entity) is not { } m)
            return false;

        MoveState s = m.Move;

        if (impulse.X != 0f || impulse.Y != 0f || impulse.Z != 0f)
        {
            if (EntityPhysicsMotion.CheckEntityVelocity(ref impulse) < 0)
                _log.Warning(FormattableString.Invariant(
                    $"Discarding ApplyAbsVelocityImpulse({impulse.X:F6},{impulse.Y:F6},{impulse.Z:F6}) on {entity}\n"));
            else
                s.Velocity += impulse;
        }

        if (clearGround)
        {
            m.Ground = EntityHandle.None;
            s.Flags &= ~GameMovement.FlOnGround;
        }

        return true;
    }

    /// <summary>Whether this is the first push refused for <paramref name="entity"/>.</summary>
    internal bool FirstRefusal(EntityHandle entity) => _refused.Add(entity);

    /// <summary>StandaloneBaseVelocity_Accumulate: added to an unconsumed base, else replacing it.</summary>
    public static Vector BaseVelocityPush(int flags, Vector current, Vector push)
        => (flags & ServerCommandRules.FlBaseVelocity) != 0 ? current + push : push;

    /// <summary>StandaloneBaseVelocity_ReleasesGround: an upward push on a grounded player.</summary>
    public static bool ReleasesGround(float pushZ, bool onGround) => pushZ > 0.0f && onGround;

    public void ViewPunch(Vector center, Vector punch, float radius, bool inAir)
    {
        var globals = new Globals(api.GlobalVarsPointer);
        int maxClients = globals.IsAvailable ? globals.MaxClients : 0;

        for (int i = 1; i <= maxClients; i++)
        {
            EntityHandle player = api.GetPlayerHandle(i);
            if (player == EntityHandle.None || Player(player) is not { } m)
                continue;

            if (!PunchReachesPlayer((m.Move.Flags & GameMovement.FlOnGround) != 0, inAir))
                continue;

            float scale = PunchScale((center - api.EntityGetOrigin(player)).Length, radius);
            if (!InRange(scale))
                continue;

            // CBasePlayer::ViewPunch: a velocity on the view angle, which the
            // movement loop's DecayPunchAngle springs back.
            m.Move.PunchAngleVel += punch * scale * 20f;
            commands.Publish(player, m);
        }
    }

    /// <summary>The falloff's answer outside the radius (view_effect_reach.h's OUT_OF_RANGE, until T4g).</summary>
    public const float OutOfRange = -1.0f;

    public static bool InRange(float strength) => strength >= 0.0f;

    /// <summary>A punch's strength at a distance: full with no radius, none past it, falling off inside.</summary>
    public static float PunchScale(float distance, float radius)
    {
        if (radius <= 0.0f)
            return 1.0f;

        if (distance > radius)
            return OutOfRange;

        return 1.0f - (distance / radius);
    }

    /// <summary>A punch reaches a grounded player, or anyone when it is an air punch.</summary>
    public static bool PunchReachesPlayer(bool onGround, bool punchInAir) => onGround || punchInAir;
}

/// <summary>Installs the player movement route on a composition's own side (null removes it).</summary>
public static class ServerPlayerMovementRouting
{
    public static void Route(IServiceLocator services, IServerPlayerMovementRoute? route)
        => services.Require<SideRoute<IServerPlayerMovementRoute>>().Fill(route);
}
