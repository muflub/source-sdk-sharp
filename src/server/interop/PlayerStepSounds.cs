//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's footsteps for the players it moves: <c>CBasePlayer::UpdateStepSound</c>'s
/// timer over the player's move state, and <c>CHL2MP_Player::PlayStepSound</c>'s server
/// arm (<see cref="StepSoundRules"/> decides; this keeps the state and hands the
/// step to the sounds, which send it to the PAS of the feet less the PVS).
/// </summary>
public sealed class PlayerStepSounds
{
    // The wading skip, one for every player as stock's function-local static was.
    private int _skipStep;

    /// <summary>
    /// <c>CHL2MP_Player::PlayStepSound</c>: none with footsteps off in
    /// multiplayer or while ducking; the step side turns and the step goes out.
    /// HL2MP's step ignores the surface and the force flag.
    /// </summary>
    public void Play(ServerPlayerMove host, int entIndex, in Vector origin, float volume, IMoveSoundSink sounds)
    {
        if (!StepSoundRules.Hl2mpPlays(sounds.Footsteps, sounds.MaxClients, host.Move.Flags))
            return;

        host.StepSide = !host.StepSide;
        sounds.StepSound(entIndex, origin, volume, host.StepSide);
    }

    /// <summary>
    /// <c>CBasePlayer::UpdateStepSound</c>: the step timer, then a step at the
    /// surface's volume (<paramref name="material"/>, -1 for none) when one falls.
    /// </summary>
    public void Update(ServerPlayerMove host, int entIndex, in Vector origin, in Vector velocity, int material, IMoveSoundSink sounds)
    {
        MoveState s = host.Move;
        float groundSpeed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        float speed = MathF.Sqrt(groundSpeed * groundSpeed + velocity.Z * velocity.Z);
        float vol = StepSoundRules.Update(ref s.StepSoundTime, ref _skipStep, s.FrameTime, sounds.Footsteps, s.Flags, s.MoveType,
                                          s.WaterLevel, speed, groundSpeed, material);
        if (vol > 0f)
            Play(host, entIndex, origin, vol, sounds);
    }
}
