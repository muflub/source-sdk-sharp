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
    /// <summary><c>enginesound-&gt;EmitAmbientSound( wave, volume, pitch, flags )</c>.</summary>
    void EmitAmbient(string wave, float volume, int pitch, int flags);

    /// <summary>
    /// <c>GameSound_Emit</c> to the local player from <c>SOUND_FROM_WORLD</c> on
    /// <c>CHAN_STATIC</c>, with the <c>EmitSound_t</c> fields named here.
    /// </summary>
    void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin);

    /// <summary><c>GameSound_Stop( SOUND_FROM_WORLD, CHAN_STATIC, wave )</c>.</summary>
    void StopWorld(string wave);

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
/// <see cref="ISoundscapeAudio"/> over IShimSoundscapeClient and the console's
/// two named-convar api. A missing shim plays nothing.
/// </summary>
internal sealed unsafe class SoundscapeAudioSlots(EngineSide api) : ISoundscapeAudio
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public void EmitAmbient(string wave, float volume, int pitch, int flags)
    {
        if (!api.ClientAttached)
            return;
        fixed (byte* p = Utf8(wave))
            api.ClientShims.SoundscapeEmitAmbient(p, volume, pitch, flags);
    }

    public void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin)
    {
        if (!api.ClientAttached)
            return;
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = Utf8(wave))
            api.ClientShims.SoundscapeEmitWorld(p, volume, soundLevel, pitch, flags, o);
    }

    public void StopWorld(string wave)
    {
        if (!api.ClientAttached)
            return;
        fixed (byte* p = Utf8(wave))
            api.ClientShims.SoundscapeStopWorld(p);
    }

    public void SetRoomType(int roomType)
    {
        if (api.ClientAttached)
            api.ClientShims.SoundscapeSetRoomType(roomType);
    }

    public void SetPlayerDsp(int dspType)
    {
        if (api.ClientAttached)
            api.ClientShims.SoundscapeSetPlayerDsp(dspType);
    }

    public void SetConVar(string name, string value)
    {
        if (!(api.ServerAttached || api.ClientAttached))
            return;
        fixed (byte* n = Utf8(name))
        fixed (byte* v = Utf8(value))
            if (api.ServerShimsOrNull is { } __att1) __att1.SetNamedConVarValue(n, v);
            else api.ClientShims.SetNamedConVarValue(n, v);
    }

    public void RevertConVar(string name)
    {
        if (!(api.ServerAttached || api.ClientAttached))
            return;
        fixed (byte* n = Utf8(name))
            if (api.ServerShimsOrNull is { } __att2) __att2.RevertNamedConVar(n);
            else api.ClientShims.RevertNamedConVar(n);
    }

    public void MainView(out Vector origin, out Vector forward, out Vector right)
    {
        float* v = stackalloc float[9];
        for (int i = 0; i < 9; i++)
            v[i] = 0;
        if (api.ClientAttached)
            api.ClientShims.SoundscapeMainView(v);
        origin = new Vector(v[0], v[1], v[2]);
        forward = new Vector(v[3], v[4], v[5]);
        right = new Vector(v[6], v[7], v[8]);
    }

    public bool MakingResLists => api.ClientAttached && api.ClientShims.SoundscapeMakingResLists() != 0;

    public void TouchSoundFile(string wave)
    {
        if (!api.ClientAttached)
            return;
        fixed (byte* p = Utf8(wave))
            api.ClientShims.SoundscapeTouchSoundFile(p);
    }
}

/// <summary>
/// What the client-system shell drives: stock's <c>C_SoundscapeSystem</c>'s
/// three engine-timed hooks (its OnRestore has no caller: CClientSystem has none, and nothing restores a save in multiplayer).
/// </summary>
public interface IClientSoundscapesDriver
{
    /// <summary><c>LevelInitPreEntity</c>: load the map's scripts and start clean.</summary>
    void LevelInit(string mapName);

    /// <summary><c>Update( frametime )</c>, from the client-systems walk.</summary>
    void Update(float frameTime);

    /// <summary><c>LevelShutdownPostEntity</c>.</summary>
    void LevelShutdown();
}
