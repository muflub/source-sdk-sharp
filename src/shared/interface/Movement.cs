//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// Everything <c>ProcessMovement</c> asks of the world, and everything it tells
/// the world.
///
/// <para>
/// One interface rather than a pile of static calls because the parity fixture
/// depends on it: the acceptance for this step replays the managed loop against
/// a RECORDING of the native one, feeding back the same trace answers the engine
/// gave the C++, and that is only possible if every world question goes through
/// a seam. The live implementation forwards to the trace ABI; the fixture
/// implementation reads the recording.
/// </para>
///
/// <para>
/// The bottom half — the sounds, the animation, the touch list, the fall damage
/// — is EFFECTS, not questions. It is on this interface rather than inlined
/// because the movement loop is the only thing that knows when they happen, and
/// a replay must be able to swallow them. <c>PlayerFallingDamage</c> is the one
/// effect with an answer, and it is the answer stock's <c>CheckFalling</c>
/// branches on.
/// </para>
/// </summary>
public interface IMoveWorld
{
    /// <summary><c>TracePlayerBBox</c> / <c>TryTouchGround</c>: a swept hull.</summary>
    MoveTrace Trace(in Vector start, in Vector end, in Vector mins, in Vector maxs,
                    uint mask, int collisionGroup);

    /// <summary>
    /// <c>GetPointContentsCached</c>. The slot is stock's per-player cache
    /// index and is passed through rather than hidden, because the cache is
    /// what makes three point queries per frame into one on a standing player
    /// and a replay has to see the same number of them.
    /// </summary>
    int PointContents(in Vector point, int slot);

    /// <summary><c>IPhysicsSurfaceProps</c>, for the four fields the loop reads.</summary>
    MoveSurface Surface(int surfaceProps);

    void ResetTouchList();
    void AddToTouched(in MoveTrace trace, in Vector velocity);
    void StartSound(in Vector origin, string sound);
    void PlayerSetAnimation(int activity);

    /// <summary>
    /// <c>MoveHelper()-&gt;PlayerFallingDamage()</c> — false if it killed them.
    ///
    /// <para>
    /// <b>THE AMOUNT IS THIS SIDE'S AND TRAVELS WITH THE CALL.</b> It used to
    /// be recomputed natively, in the shim's own copy of stock's formula, from
    /// the player's <c>m_flFallVelocity</c> member — which is the value from
    /// BEFORE the move that is landing them, because the loop's own fall
    /// velocity has not been written back yet. So the threshold that armed the
    /// call and the number that scaled the damage read two different inputs.
    /// <see cref="MoveMath.FallDamage"/> is the one formula and it is handed
    /// the very velocity the arming test used.
    /// </para>
    ///
    /// <para>
    /// It stays an EFFECT rather than becoming a damage call this side makes,
    /// and that is the prediction argument: the client replays this loop over
    /// every unacknowledged command, and a landing must hurt exactly once, on
    /// the server. The seam is what makes the client's implementation a no-op
    /// that still answers "alive" — see <c>shell_prediction.cpp</c>'s move
    /// helper — so no side check is needed in the loop itself.
    /// </para>
    /// </summary>
    /// <param name="damage">
    /// How much the landing does, from <see cref="MoveMath.FallDamage"/>.
    /// </param>
    bool PlayerFallingDamage(float damage);

    void PlayStepSound(in Vector origin, int surfaceProps, float volume, bool force);
    void UpdateStepSound(in Vector origin, in Vector velocity, int surfaceProps);
    void Splash();
    void RumbleEffect(byte index, byte data, byte flags);

    /// <summary>
    /// <c>engine-&gt;Time()</c>, which is WALL time and not the tick clock.
    /// Read by exactly one thing — CheckStuck's "too soon?" throttle — and it
    /// is on this interface rather than taken from the state because it is not
    /// predicted and must not look as though it were.
    /// </summary>
    float EngineTime();
    void ResetDuckLatched();
    void SetPlayerSurface(int material);
}

/// <summary>
/// The constants the loop reads out of convars and gamerules: pushed once,
/// because none of them can change inside a movement frame and a lookup per
/// frame per player is a cost with no answer behind it.
/// </summary>
public struct MoveConstants
{
    public float Friction, Accelerate, AirAccelerate, StopSpeed, MaxVelocity, MaxSpeed;
    public float Bounce, RollAngle, RollSpeed, NoClipSpeed, NoClipAccelerate;
    public float SpecSpeed, SpecAccelerate;
    public float AirSpeedCap, DuckSpeedScale;

    /// <summary><c>g_bMovementOptimizations</c> — stock's default is on.</summary>
    public bool Optimizations;

    public bool SpecNoClip;

    /// <summary>
    /// <c>gpGlobals-&gt;maxClients</c>, and it is NOT a diagnostic.
    /// <c>CheckJumpButton</c> starts the whole duck-jump family
    /// (<c>m_flJumpTime</c>, <c>m_bInDuckJump</c>) only when this is 1, so in a
    /// multiplayer build that family is unreachable — which a recording of 3458
    /// live commands confirmed by containing not one row with
    /// <c>m_flJumpTime &gt; 0</c>.
    /// </summary>
    public int MaxClients;

    /// <summary>
    /// <c>gpGlobals-&gt;interval_per_tick</c>.
    ///
    /// <para>
    /// NOT a cosmetic constant. The four <c>CheckInterval</c> periods are
    /// written in SECONDS and divided by it at compile time, so the stuck check
    /// runs every <c>(int)(1.0f / 0.015f)</c> = 66 commands rather than every
    /// one — and a port that assumed "every command" issues a trace the C++
    /// never issued, which is the first thing the parity fixture found.
    /// </para>
    /// </summary>
    public float TickInterval;

    /// <summary>The hull, from gamerules. <c>VEC_*_SCALED</c> multiplies each by the model scale.</summary>
    public Vector View, HullMin, HullMax, DuckHullMin, DuckHullMax, DuckView;
    public Vector ObsHullMin, ObsHullMax, DeadViewHeight;
}
