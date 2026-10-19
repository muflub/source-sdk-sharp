//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared.Anim;

/// <summary>
/// How fast this player's body is configured to move: stock's
/// <c>MultiPlayerMovementData_t</c> (multiplayer_animstate.h:154-161), the
/// constructor argument of <c>CMultiPlayerAnimState</c> that the mod fills
/// per game class (HL2MP hands in walk 150 / run 320 / no sprint / feet
/// turning at <c>mp_feetyawrate</c>).
///
/// <para>
/// "Set speeds to -1 if they are not used" is stock's comment and survives
/// as <see cref="Unused"/>: a game with no sprint sets it to -1 and nothing
/// divides by it. These are CONFIGURATION, not measurement — the speed
/// cap the player actually has is <see cref="IAnimTarget.MaxSpeed"/>, and
/// HL2's walk/run split is decided against THAT (see
/// <see cref="Hl2PostureLayer.RunFraction"/>), not against
/// <see cref="RunSpeed"/>; <see cref="WalkSpeed"/> and friends exist for the
/// layers that want the authored numbers (a sprint layer, a mod's own rule).
/// </para>
/// </summary>
public sealed class AnimStateParams
{
    /// <summary>Stock's "not used" marker (<c>-1</c>).</summary>
    public const float Unused = -1f;

    /// <summary>HL2MP's authored <c>+speed</c> (player.cpp's HL2MP table).</summary>
    public const float Hl2WalkSpeed = 150f;

    /// <summary>HL2MP's authored run speed.</summary>
    public const float Hl2RunSpeed = 320f;

    /// <summary>HL2MP has no sprint: stock's <c>HL2MPClimberAnimState</c>'s movement data says so with -1.</summary>
    public const float Hl2SprintSpeed = Unused;

    /// <summary>
    /// <c>mp_feetyawrate</c>'s default (720, base_playeranimstate.cpp:41):
    /// degrees a second the feet/upper body may turn — stock's own literal
    /// at the <c>ConvergeYawAngles</c> call, multiplayer_animstate.cpp:1770.
    /// </summary>
    public const float DefaultBodyYawRate = 720f;
    /// <summary>
    /// The HL2MP numbers: the ones the core's players are configured with.
    /// A FACTORY and not a shared instance — the class is mutable (stock's
    /// <c>SetRunSpeed</c> and friends exist precisely so a game can change
    /// them), and one mutable static shared by every player would be
    /// statics-with-a-side door, which the build refuses (SS0001).
    /// </summary>
    public static AnimStateParams Hl2Defaults() => new()
    {
        WalkSpeed = Hl2WalkSpeed,
        RunSpeed = Hl2RunSpeed,
        SprintSpeed = Hl2SprintSpeed,
        BodyYawRate = DefaultBodyYawRate,
    };

    /// <summary>The <c>m_flWalkSpeed</c> slot.</summary>
    public float WalkSpeed = Hl2WalkSpeed;

    /// <summary>The <c>m_flRunSpeed</c> slot.</summary>
    public float RunSpeed = Hl2RunSpeed;

    /// <summary>The <c>m_flSprintSpeed</c> slot; <see cref="Unused"/> when the game has no sprint.</summary>
    public float SprintSpeed = Hl2SprintSpeed;

    /// <summary>
    /// The <c>m_flBodyYawRate</c> slot: how fast the feet may converge on
    /// the goal yaw. Stock passed it in but converged at its own hard-coded
    /// 720 (its own comment: <c>/*DOD_BODYYAW_RATE*/720.0f</c>); this port
    /// uses the configured value, which is what the field is FOR, and says
    /// so in a divergence row.
    /// </summary>
    public float BodyYawRate = DefaultBodyYawRate;

    /// <summary>Stock's <c>SetWalkSpeed</c> / <c>SetRunSpeed</c> / <c>SetSprintSpeed</c>.</summary>
    public void SetSpeeds(float walk, float run, float sprint)
    {
        WalkSpeed = walk;
        RunSpeed = run;
        SprintSpeed = sprint;
    }
}
