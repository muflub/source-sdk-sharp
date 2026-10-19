//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

namespace SourceSharp.Shared.Anim;

/// <summary>
/// Which posture a player's body is in, as a name rather than a sequence
/// index.
///
/// <para>
/// An index is a fact about ONE model file; a posture is a fact about the
/// player. Resolving one to the other is the last step and it happens against
/// whatever model that player happens to be wearing, so a mod that gives two
/// players different models gets the right animation on each without this
/// enum growing an entry.
/// </para>
///
/// <para>
/// Moved here from the client's <c>PlayerAnimation.cs</c> by phase 0.10: the
/// posture is now the HL2 <see cref="IAnimStateLayer"/>'s answer
/// (<see cref="Hl2PostureLayer"/>), and that layer runs on the server too, so
/// the name it answers with cannot live behind the client's door. The
/// <see cref="PlayerPosture.Jump"/> entry means what it always meant — off
/// the ground, however they got there — and is the state the stock jump flag
/// (<c>m_bJumping</c>, set by <c>PLAYERANIMEVENT_JUMP</c>) maps onto on both
/// sides.
/// </para>
/// </summary>
public enum PlayerPosture
{
    /// <summary>Standing still.</summary>
    Idle,

    /// <summary>Standing still, crouched.</summary>
    CrouchIdle,

    /// <summary>Moving at less than <see cref="Hl2PostureLayer.RunFraction"/> of their cap.</summary>
    Walk,

    /// <summary>Moving at or above it.</summary>
    Run,

    /// <summary>Moving, crouched. There is one crouched gait, not two.</summary>
    CrouchWalk,

    /// <summary>Off the ground, however they got there.</summary>
    Jump,
}

/// <summary>
/// What a player's body should be doing this instant: one posture, one
/// playback rate and one strafe direction.
/// </summary>
/// <param name="Posture">Which gait.</param>
/// <param name="PlaybackRate">
/// How fast to run it, 1 meaning the animator's own speed. Only meaningful once
/// the caller has the sequence's authored ground speed — see
/// <see cref="Hl2PostureLayer.Rate"/>.
/// </param>
/// <param name="MoveYaw">
/// Which way they are travelling relative to the way the body faces, in
/// degrees in (-180, 180]. 0 is straight ahead, 90 is a left strafe. This is
/// the <c>move_yaw</c> pose parameter, and it is what makes a sidestep a
/// sidestep rather than a walk cycle played sideways.
/// </param>
public readonly record struct PlayerPose(PlayerPosture Posture, float PlaybackRate, float MoveYaw);
