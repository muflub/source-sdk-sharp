using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The movement's two sounds (IMoveHelper::StartSound: a fall, a swim stroke),
/// which the managed move helper hands the managed SoundEmitter: the player's
/// index, where it is, and the sound. Who hears it is decided there.
///
/// <para>
/// Native's move helper reached it through IManagedSoundEmitter until the
/// movement was managed; that interface left in the serialised ABI pass
/// (docs/abi-retirements.md, 2026-10-20) and this contract stayed for
/// <c>ServerMoveWorld</c>.
/// </para>
/// </summary>
public interface IMoveSoundSink
{
    /// <summary>StartSound( origin, channel, sample, volume, soundlevel, flags, pitch ).</summary>
    void MoveSound(int entIndex, Vector origin, int channel, string sample, float volume, int soundLevel, int flags,
                   int pitch);

    /// <summary>StartSound( origin, soundname ).</summary>
    void MoveSoundScript(int entIndex, Vector origin, string soundName);

    /// <summary><c>sv_footsteps</c>.</summary>
    bool Footsteps { get; }

    /// <summary>gpGlobals-&gt;maxClients.</summary>
    int MaxClients { get; }

    /// <summary>
    /// <c>CHL2MP_Player::PlayStepSound</c>'s emit: the player's step on the
    /// side given, at the volume given, to the PAS of the feet less its PVS
    /// on a multiplayer server.
    /// </summary>
    void StepSound(int entIndex, Vector origin, float volume, bool left);
}
