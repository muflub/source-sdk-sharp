using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// The engine calls a soundscape ends in: the B4 executors. Native carries
/// each one out as stock's <c>C_SoundscapeSystem</c> made it
/// (shell_soundscape_audio_client.cpp), and none of them decides anything.
/// </summary>
public interface ISoundscapeAudio
{
    /// <summary><c>enginesound-&gt;SetRoomType( local player, roomType )</c>.</summary>
    void SetRoomType(int roomType);

    /// <summary><c>enginesound-&gt;SetPlayerDSP( local player, dspType, false )</c>.</summary>
    void SetPlayerDsp(int dspType);

    /// <summary>An engine convar set by name (<c>ConVar::SetValue</c>).</summary>
    void SetConVar(string name, string value);

    /// <summary>An engine convar back to its default (<c>ConVar::Revert</c>).</summary>
    void RevertConVar(string name);

    /// <summary><c>MainViewOrigin/Forward/Right()</c>: where the listener sits.</summary>
    void MainView(out Vector origin, out Vector forward, out Vector right);

    /// <summary><c>CommandLine()-&gt;FindParm( "-makereslists" )</c>.</summary>
    bool MakingResLists { get; }

    /// <summary><c>filesystem-&gt;GetFileTime( "sound/" + PSkipSoundChars( wave ), "GAME" )</c>: a reslist touch.</summary>
    void TouchSoundFile(string wave);
}

/// <summary>
/// The sounds a soundscape plays and stops, which the managed client sound
/// emitter plays (sound_emitter.cpp's client emit path, moved): an ambient
/// wave straight on the engine, a positioned one through the emitter.
/// </summary>
public interface ISoundscapeSounds
{
    /// <summary><c>enginesound-&gt;EmitAmbientSound( wave, volume, pitch, flags )</c>.</summary>
    void EmitAmbient(string wave, float volume, int pitch, int flags);

    /// <summary>
    /// <c>GameSound_Emit</c> to the local player from <c>SOUND_FROM_WORLD</c> on
    /// <c>CHAN_STATIC</c>, with the <c>EmitSound_t</c> fields named here.
    /// </summary>
    void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin);

    /// <summary><c>GameSound_Stop( SOUND_FROM_WORLD, CHAN_STATIC, wave )</c>.</summary>
    void StopWorld(string wave);
}
