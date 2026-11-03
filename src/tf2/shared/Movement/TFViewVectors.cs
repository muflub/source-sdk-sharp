//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace TF2.Shared.Movement;

/// <summary>
/// TF2's movement constants: <c>g_TFViewVectors</c>
/// (src/game/shared/tf/tf_gamerules.cpp:1310), which <c>CTFGameRules::GetViewVectors</c>
/// answers for the whole mod, the per-class eye of <c>g_TFClassViewVectors</c>
/// (:1326), and the movement convars' defaults as a TF build declares them
/// (src/game/shared/movevars_shared.cpp: <c>sv_gravity</c> 800 outside HL2,
/// <c>sv_stopspeed</c> 100, <c>sv_friction</c> 4, <c>sv_accelerate</c> and
/// <c>sv_airaccelerate</c> 10, <c>sv_maxspeed</c> 320). Properties and
/// switches, not tables (SS0010).
/// </summary>
public static class TFViewVectors
{
    /// <summary>
    /// The ground jump's height, chosen so the core's <c>sqrt(2 g h)</c> is
    /// stock's literal 289 at gravity 800 (<c>CTFGameMovement::CheckJumpButton</c>,
    /// tf_gamemovement.cpp:1315: <c>flMul = 289.0f * flJumpMod * flGroundFactor</c>).
    /// </summary>
    public const float JumpHeight = 289f * 289f / (2f * 800f);

    /// <summary>The mod's movement constants (<see cref="IMovementRules.Constants"/>).</summary>
    public static MovementParams Params => new()
    {
        JumpHeight = JumpHeight,
        Gravity = 800f,
        Friction = 4f,
        Accelerate = 10f,
        AirAccelerate = 10f,
        StopSpeed = 100f,
        MaxVelocity = 3500f,
        StepSize = 18f,
        // CGameMovement::GetAirSpeedCap's 30, which CTFGameMovement's
        // override answers outside its grapple/charge/kart arms.
        AirSpeedCap = 30f,
        DuckSpeedScale = 0.33333333f,
        MaxSpeed = 320f,

        View = new Vector(0f, 0f, 72f),
        HullMin = new Vector(-24f, -24f, 0f),
        HullMax = new Vector(24f, 24f, 82f),
        DuckHullMin = new Vector(-24f, -24f, 0f),
        DuckHullMax = new Vector(24f, 24f, 62f),
        DuckView = new Vector(0f, 0f, 45f),
        ObsHullMin = new Vector(-10f, -10f, -10f),
        ObsHullMax = new Vector(10f, 10f, 10f),
        DeadViewHeight = new Vector(0f, 0f, 14f),
    };

    /// <summary>
    /// <c>CTFPlayer::GetClassEyeHeight</c> (src/game/shared/tf/tf_player_shared.cpp:12729)
    /// before the model scale: <c>g_TFClassViewVectors[class].z</c> for Scout..Civilian,
    /// else <paramref name="standing"/> (stock's <c>VEC_VIEW_SCALED</c>).
    /// </summary>
    public static float ClassEyeHeight(TFClass playerClass, float standing) => playerClass switch
    {
        TFClass.Scout => 65f,
        TFClass.Sniper => 75f,
        TFClass.Soldier => 68f,
        TFClass.Demoman => 68f,
        TFClass.Medic => 75f,
        TFClass.HeavyWeapons => 75f,
        TFClass.Pyro => 68f,
        TFClass.Spy => 75f,
        TFClass.Engineer => 68f,
        TFClass.Civilian => 65f,
        _ => standing,
    };
}
