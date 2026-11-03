//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Client.Sounds;

/// <summary>Where a client step is played: the step's script at a volume, from the player, to the local player.</summary>
public interface IClientStepSoundSink
{
    /// <summary>
    /// <c>CHL2MP_Player::PlayStepSound</c>'s emit on the client: the script's
    /// wave, level and pitch at <paramref name="volume"/> on CHAN_BODY, from
    /// <paramref name="entIndex"/> at <paramref name="origin"/>.
    /// </summary>
    void EmitStep(int entIndex, string script, in Vector origin, float volume);
}

/// <summary>
/// The client's footsteps (the CLIENT_DLL arms of <c>CBasePlayer::UpdateStepSound</c>
/// and <c>CHL2MP_Player::PlayStepSound</c>): the local player's from its
/// predicted movement, played on the first prediction of a command only, and
/// every other player's from its networked movement once a frame
/// (<c>C_BasePlayer::Simulate</c>). In multiplayer the server sends a step only
/// to the PAS of the feet less the PVS; the PVS's clients make it here.
/// <see cref="StepSoundRules"/> decides, as on the server; this keeps each
/// player's step side and, for the other players, their step timer.
/// </summary>
public sealed class ClientStepSounds(IClientStepSoundSink sink, Func<bool> footsteps, Func<int> maxClients, Func<int, string?> playerModel)
{
    private readonly Dictionary<int, State> _players = new();

    // The wading skip, one for every player as stock's function-local static was.
    private int _skipStep;

    /// <summary>Steps played since the level began, for the facts and the sound log.</summary>
    public int Played { get; private set; }

    private sealed class State
    {
        public float StepSoundTime;
        public bool StepSide;
    }

    private State Of(int entIndex)
    {
        if (!_players.TryGetValue(entIndex, out State? s))
            _players[entIndex] = s = new State();
        return s;
    }

    /// <summary>
    /// <c>CBasePlayer::UpdateStepSound</c> over a step timer the caller keeps
    /// (the local player's is its predicted move state's): the timer, then a
    /// step at the surface's volume (<paramref name="material"/>, -1 for none)
    /// when one falls. Answers the step's volume, 0 for none.
    /// </summary>
    public float Update(ref float stepTime, float frameTime, int flags, int moveType, int waterLevel, in Vector velocity, int material)
    {
        float groundSpeed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        float speed = MathF.Sqrt(groundSpeed * groundSpeed + velocity.Z * velocity.Z);
        return StepSoundRules.Update(ref stepTime, ref _skipStep, frameTime, footsteps(), flags, moveType, waterLevel, speed, groundSpeed, material);
    }

    /// <summary>
    /// <c>C_BasePlayer::Simulate</c>'s step for a player other than the local
    /// one: its own timer over this frame, from its networked movement, and the
    /// step played when one falls.
    /// </summary>
    public void UpdateOther(int entIndex, in Vector origin, in Vector velocity, float frameTime, int flags, int moveType, int waterLevel, int material)
    {
        State s = Of(entIndex);
        float vol = Update(ref s.StepSoundTime, frameTime, flags, moveType, waterLevel, velocity, material);
        if (vol > 0f)
            Play(entIndex, origin, vol, flags);
    }

    /// <summary>
    /// <c>CHL2MP_Player::PlayStepSound</c> after the prediction's gate: none
    /// with footsteps off in multiplayer or while ducking; the step side turns
    /// and the step is played.
    /// </summary>
    public void Play(int entIndex, in Vector origin, float volume, int flags)
    {
        if (!StepSoundRules.Hl2mpPlays(footsteps(), maxClients(), flags))
            return;

        State s = Of(entIndex);
        s.StepSide = !s.StepSide;
        sink.EmitStep(entIndex, StepSoundRules.Hl2mpStepName(StepSoundRules.Hl2mpSoundPrefix(playerModel(entIndex)), s.StepSide), origin, volume);
        Played++;
    }

    /// <summary>A player gone: its timer and side start again with the next one in its slot.</summary>
    public void Forget(int entIndex) => _players.Remove(entIndex);
}
