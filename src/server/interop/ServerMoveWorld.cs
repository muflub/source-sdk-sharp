//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The world the server's movement loop moves a player through
/// (plan_server_native_v2 T4g): the hull trace over the managed filter, the
/// point contents and surfaces, and the move helper's work, which was
/// CStandaloneMoveHelper's (shell_standalone_player.cpp) behind
/// shell_movement.cpp's WorldEffect.
///
/// <para>
/// Native decided nothing in that helper the client does not, so it is
/// managed here: the touch list's one-report-per-entity dedupe and the record
/// of what was touched, the swim and fall noises (the managed SoundEmitter's,
/// as they were), the surface change, and the fall's damage, which native
/// carries out through the shell's damage path. The rest of IMoveHelper was
/// empty on the server (animation, step sounds, splash, rumble, the duck
/// latch) and is empty here.
/// </para>
///
/// <para>
/// THREE WORDS OUTLIVE A COMMAND AND A PLAYER, as they did natively: the last
/// trace's entity (CShellGameMovement::m_LastTrace, which AddToTouched
/// replays), the last standable ground any trace found (m_pLastGround, which
/// the ground write-back names), and the point-contents cache.
/// </para>
/// </summary>
internal sealed unsafe class ServerMoveWorld(EngineSide api, IMovementServerArea movement, IMoveSoundSink sounds,
                                             IServerModule? game) : IMoveWorld
{
    private readonly MovePointContentsCache _pointContents = new();

    // The move helper's touch list (CStandaloneMoveHelper::m_TouchList): the
    // entities already reported this command, NULL included, as the native
    // pointer compare deduped them.
    private readonly List<EntityHandle> _touchList = new();

    /// <summary>The player being moved: the trace's pass entity and the helper's host.</summary>
    public EntityHandle Player { get; set; }

    /// <summary>The host's entity index: the noises' speaker.</summary>
    public int EntIndex { get; set; }

    /// <summary>The host's record, which the touches are written to.</summary>
    public ServerPlayerMove? Host { get; set; }

    /// <summary>The entity the last serviced trace hit, or none (m_LastTrace's m_pEnt).</summary>
    public EntityHandle LastHit { get; private set; }

    /// <summary>The last standable ground any trace found (m_pLastGround), or none.</summary>
    public EntityHandle LastGround { get; set; }

    /// <summary>The wall clock CheckStuck's throttle reads.</summary>
    public float Time { get; set; }

    public MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                           uint mask, int collisionGroup)
    {
        float* s = stackalloc float[3] { start.X, start.Y, start.Z };
        float* e = stackalloc float[3] { end.X, end.Y, end.Z };
        float* lo = stackalloc float[3] { mins.X, mins.Y, mins.Z };
        float* hi = stackalloc float[3] { maxs.X, maxs.Y, maxs.Z };

        MoveTraceBlob blob = default;
        api.MoveTraceServer(movement, s, e, lo, hi, mask, collisionGroup, Player, &blob,
                            out EntityHandle hit, out EntityHandle ground);

        // THE WHOLE TRACE'S ENTITY, KEPT, so AddToTouched can report it: every
        // AddToTouched the loop makes follows the trace it means.
        LastHit = hit;

        // REMEMBERED, because SetGroundEntity has to be able to name it: the
        // last trace that found standable ground fed every SetGroundEntity.
        if (ground != EntityHandle.None)
            LastGround = ground;

        return new MoveTrace
        {
            Fraction = blob.Fraction,
            EndPos = new Vector(blob.EndPosX, blob.EndPosY, blob.EndPosZ),
            PlaneNormal = new Vector(blob.PlaneNormalX, blob.PlaneNormalY, blob.PlaneNormalZ),
            PlaneDist = blob.PlaneDist,
            StartSolid = blob.StartSolid != 0,
            AllSolid = blob.AllSolid != 0,
            SurfaceProps = blob.SurfaceProps,
            SurfaceFlags = blob.SurfaceFlags,
            Contents = blob.Contents,
            HasGround = blob.HasGround != 0,
            GroundVelocity = new Vector(blob.GroundVelocityX, blob.GroundVelocityY, blob.GroundVelocityZ),
            HasEntity = blob.HasEntity != 0,
            HitWorld = blob.HitWorld != 0,
        };
    }

    public int PointContents(in Vector point, int slot)
    {
        if (_pointContents.TryGet(point, slot, out int cached))
            return cached;

        int contents = EngineBound.Trace(api).PointContents(point);
        _pointContents.Store(point, slot, contents);
        return contents;
    }

    public void ResetPointContentsCache(bool optimizations) => _pointContents.Reset(optimizations);

    public MoveSurface Surface(int surfaceProps)
        => EngineBound.SurfaceProps(api).Surface(surfaceProps);

    /// <summary>A new command's helper, and the loop's own ResetTouchList.</summary>
    public void ResetTouchList() => _touchList.Clear();

    /// <summary>
    /// The same dedupe stock keeps: one report per entity per command, of the
    /// last serviced trace; a report of an entity is recorded on the host
    /// (stock's CBasePlayer::Touch decides from it, PlayerShadowBodies here).
    /// </summary>
    public void AddToTouched(in MoveTrace trace, in Vector velocity)
    {
        EntityHandle hit = LastHit;

        if (_touchList.Contains(hit))
            return;

        _touchList.Add(hit);

        if (hit != EntityHandle.None)
            Host?.RecordTouch(hit);
    }

    /// <summary>The swim stroke: "Player.Swim" from the host, as native's START_SOUND_SWIM played it.</summary>
    public void StartSound(in Vector origin, string sound)
        => sounds.MoveSoundScript(EntIndex, origin, "Player.Swim");

    public void PlayerSetAnimation(int activity) { }

    /// <summary>
    /// APPLY the landing; MoveMath.FallDamage decided it. Through the shell's
    /// damage path (native), then the noise; whether they got up.
    /// </summary>
    public bool PlayerFallingDamage(float damage)
    {
        bool alive = api.PlayerFallDamage(movement, Player, damage);

        // From where the shell is after the hit, as the helper's
        // StartSound( m_pHost->GetAbsOrigin(), ... ) asked it.
        if (damage > 0f)
            sounds.MoveSoundScript(EntIndex, api.EntityGetOrigin(Player), "Player.FallDamage");

        return alive;
    }

    public void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force) { }

    public void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps) { }

    public void Splash() { }

    public void RumbleEffect(byte index, byte data, byte flags) { }

    public float EngineTime() => Time;

    public void ResetDuckLatched() { }

    /// <summary>env_player_surface_trigger's funnel (ss::SetPlayerSurface): a material, to the module.</summary>
    public void SetPlayerSurface(int material)
    {
        if (SurfaceChange(material, Player) is { } changed)
            game?.OnPlayerSurfaceChanged(Player, changed);
    }

    /// <summary>
    /// ss::SetPlayerSurface's rule: the material as the signed char the funnel
    /// took, and nothing for no material (a player in the air: it stops
    /// bunny hoppers escaping triggers) or no player.
    /// </summary>
    internal static int? SurfaceChange(int material, EntityHandle player)
        => (sbyte)material == 0 || player == EntityHandle.None ? null : (sbyte)material;
}
