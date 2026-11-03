//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Abi;

/// <summary>
/// A player's footsteps: <c>CBasePlayer::UpdateStepSound</c>,
/// <c>GetStepSoundVelocities</c> and <c>SetStepSoundTime</c>
/// (baseplayer_shared.cpp), and the HL2MP player's step names
/// (<c>CHL2MP_Player::PlayStepSound</c>, <c>SetupPlayerSoundsByModel</c>).
/// Pure: the caller keeps the step timer, the step side and the wading skip
/// (stock's function-local <c>static int iSkipStep</c>) and emits the sound.
/// </summary>
public static class StepSoundRules
{
    /// <summary>STEPSOUNDTIME_NORMAL walking / running, STEPSOUNDTIME_ON_LADDER, STEPSOUNDTIME_WATER_KNEE (ms).</summary>
    public const float WalkStepMs = 400f, RunStepMs = 300f, LadderStepMs = 350f, WadeStepMs = 600f;

    /// <summary>A step's volume: 0 when no step falls this time, else the volume UpdateStepSound hands PlayStepSound.</summary>
    /// <param name="stepTime">m_flStepSoundTime (ms), counted down by <paramref name="frameTime"/> and reset by a step.</param>
    /// <param name="skipStep">The wading skip counter (stock's static iSkipStep), the caller's.</param>
    /// <param name="material">The ground surface's game material (CHAR_TEX_*), or -1 for no surface.</param>
    public static float Update(ref float stepTime, ref int skipStep, float frameTime, bool footsteps, int flags, int moveType,
                               int waterLevel, float speed, float groundSpeed, int material)
    {
        if (stepTime > 0)
        {
            stepTime -= 1000.0f * frameTime;
            if (stepTime < 0)
                stepTime = 0;
        }

        if (stepTime > 0)
            return 0f;

        if ((flags & (GameMovement.FlFrozen | FlAtControls)) != 0)
            return 0f;

        if (moveType is GameMovement.MoveTypeNoClip or GameMovement.MoveTypeObserver)
            return 0f;

        if (!footsteps)
            return 0f;

        bool ladder = moveType == GameMovement.MoveTypeLadder;
        bool ducking = (flags & GameMovement.FlDucking) != 0;
        (float velWalk, float velRun) = ducking || ladder ? (60f, 80f) : (90f, 220f);

        bool onGround = (flags & GameMovement.FlOnGround) != 0;
        bool movingAlongGround = groundSpeed > 0.0001f;
        bool fastEnough = speed >= velWalk;
        if (!fastEnough || !(ladder || (onGround && movingAlongGround)))
            return 0f;

        bool walking = speed < velRun;
        float vol;
        if (ladder)
        {
            vol = 0.5f;
            stepTime = LadderStepMs;
        }
        else if (waterLevel == GameMovement.WlWaist)
        {
            if (skipStep == 0)
            {
                skipStep++;
                return 0f;
            }

            if (skipStep++ == 3)
                skipStep = 0;
            vol = 0.65f;
            stepTime = WadeStepMs;
        }
        else if (waterLevel == GameMovement.WlFeet)
        {
            vol = walking ? 0.2f : 0.5f;
            stepTime = walking ? WalkStepMs : RunStepMs;
        }
        else
        {
            if (material < 0)
                return 0f;

            stepTime = walking ? WalkStepMs : RunStepMs;
            vol = material switch
            {
                'D' => walking ? 0.25f : 0.55f, // CHAR_TEX_DIRT
                'V' => walking ? 0.4f : 0.7f,   // CHAR_TEX_VENT
                _ => walking ? 0.2f : 0.5f,     // concrete, metal, grate, tile, slosh and the default
            };
        }

        // 65% volume if ducking.
        if (ducking)
            vol *= 0.65f;
        return vol;
    }

    /// <summary>FL_ATCONTROLS in the multiplayer block of const.h.</summary>
    public const int FlAtControls = 1 << 7;

    /// <summary>
    /// <c>CHL2MP_Player::PlayStepSound</c>'s gate before the name: footsteps off
    /// in multiplayer, or a ducking player, makes no step.
    /// </summary>
    public static bool Hl2mpPlays(bool footsteps, int maxClients, int flags)
        => !(maxClients > 1 && !footsteps) && (flags & GameMovement.FlDucking) == 0;

    /// <summary>
    /// The HL2MP step's script sound for the side the step side turned to
    /// (stock toggles <c>m_nStepside</c> first, then a set side is the left).
    /// </summary>
    public static string Hl2mpStepName(string prefix, bool left)
        => left ? prefix + ".RunFootstepLeft" : prefix + ".RunFootstepRight";

    /// <summary>
    /// <c>SetupPlayerSoundsByModel</c>'s prefix (g_ppszPlayerSoundPrefixNames):
    /// a human model is a citizen, else "police" the metro police, else
    /// "combine" the soldier; a model none of them names keeps the citizen
    /// (m_iPlayerSoundType's zero).
    /// </summary>
    public static string Hl2mpSoundPrefix(string? model)
    {
        if (model is null)
            return "NPC_Citizen";
        if (model.Contains("models/human", StringComparison.OrdinalIgnoreCase))
            return "NPC_Citizen";
        if (model.Contains("police", StringComparison.OrdinalIgnoreCase))
            return "NPC_MetroPolice";
        if (model.Contains("combine", StringComparison.OrdinalIgnoreCase))
            return "NPC_CombineS";
        return "NPC_Citizen";
    }
}
