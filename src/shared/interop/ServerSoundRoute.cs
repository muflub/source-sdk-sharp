namespace SourceSharp.Abi;

/// <summary>
/// The server's managed sound emitter (plan_server_native_v2 T4a): what the
/// sound area's server members reach once their native bodies left
/// (shell_sound.cpp's emits, shell_soundenvelope.cpp's patches). The side's
/// own routing -- a shell-less entity's emit placed at its origin -- stays in
/// <see cref="EngineSide"/> and runs first; this answers what used to cross.
/// </summary>
public interface IServerSoundRoute
{
    /// <summary>A sound from an entity, heard by the PAS of its centre at the script's level.</summary>
    void EntityEmitSound(EntityHandle entity, string soundName);

    /// <summary>Every wave of the sound stopped on the index it played from.</summary>
    void EntityStopSound(EntityHandle entity, string soundName);

    /// <summary>A sound from the world at a point.</summary>
    void EmitSoundAtPosition(string soundName, Vector origin);

    /// <summary>A script from the world at a point, on a channel, at a scale of its volume; false for no such script.</summary>
    bool EmitWorldSoundScript(string soundName, Vector origin, int channel, float volume);

    /// <summary>An ambient channel started, retuned or stopped (UTIL_EmitAmbientSound).</summary>
    void EmitAmbientSound(int entIndex, Vector origin, string soundName, float volume, int soundLevel, int flags, int pitch);

    /// <summary>A sound patch on the entity: its handle, or 0.</summary>
    int SoundPatchCreate(EntityHandle entity, int channel, string soundName, int soundLevel);

    /// <summary>One emit of a patch at the envelope's values.</summary>
    void SoundPatchEmit(int patch, float volume01, float pitch, int flags, float startTime, bool emitCaption);

    /// <summary>A playing patch stopped.</summary>
    void SoundPatchStop(int patch);

    /// <summary>The patch's entity index (negative when gone) and whether anyone listens.</summary>
    void SoundPatchState(int patch, out int entIndex, out bool filterActive);

    /// <summary>The patch forgotten.</summary>
    void SoundPatchFree(int patch);

    /// <summary>
    /// A sound precached by name: a soundscript entry's waves, or a path to
    /// a wave directly (plan_server_native_v2 T4a, the precache path; was
    /// the IShimPrecache::PrecacheSound slot).
    /// </summary>
    void PrecacheSound(string soundName);
}
