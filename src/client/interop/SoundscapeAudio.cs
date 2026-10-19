using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="ISoundscapeAudio"/> over IShimSoundscapeClient and the console's
/// two named-convar api. A missing shim plays nothing.
/// </summary>
internal sealed unsafe class SoundscapeAudioSlots(ConsoleArea? console, SoundscapeClientArea? soundscapeClient) : ISoundscapeAudio
{
    // The shim areas this calls, taken by constructor (plan_shim_areas_split
    // phase 5). Null on a client that never attached (an unattached engine
    // double): a guarded call answers its default and any other throws, as
    // they did through the side's attach guard.
    private ConsoleArea Console => console ?? throw ShimAreaGuard.NotAttached(nameof(Console));
    private SoundscapeClientArea SoundscapeClient => soundscapeClient ?? throw ShimAreaGuard.NotAttached(nameof(SoundscapeClient));

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");

    public void EmitAmbient(string wave, float volume, int pitch, int flags)
    {
        if (console is null)
            return;
        fixed (byte* p = Utf8(wave))
            SoundscapeClient.SoundscapeEmitAmbient(p, volume, pitch, flags);
    }

    public void EmitWorld(string wave, float volume, int soundLevel, int pitch, int flags, Vector origin)
    {
        if (console is null)
            return;
        float* o = stackalloc float[] { origin.X, origin.Y, origin.Z };
        fixed (byte* p = Utf8(wave))
            SoundscapeClient.SoundscapeEmitWorld(p, volume, soundLevel, pitch, flags, o);
    }

    public void StopWorld(string wave)
    {
        if (console is null)
            return;
        fixed (byte* p = Utf8(wave))
            SoundscapeClient.SoundscapeStopWorld(p);
    }

    public void SetRoomType(int roomType)
    {
        if ((console is not null))
            SoundscapeClient.SoundscapeSetRoomType(roomType);
    }

    public void SetPlayerDsp(int dspType)
    {
        if ((console is not null))
            SoundscapeClient.SoundscapeSetPlayerDsp(dspType);
    }

    public void SetConVar(string name, string value)
    {
        if (console is null)
            return;
        fixed (byte* n = Utf8(name))
        fixed (byte* v = Utf8(value))
            Console.SetNamedConVarValue(n, v);
    }

    public void RevertConVar(string name)
    {
        if (console is null)
            return;
        fixed (byte* n = Utf8(name))
            Console.RevertNamedConVar(n);
    }

    public void MainView(out Vector origin, out Vector forward, out Vector right)
    {
        float* v = stackalloc float[9];
        for (int i = 0; i < 9; i++)
            v[i] = 0;
        if ((console is not null))
            SoundscapeClient.SoundscapeMainView(v);
        origin = new Vector(v[0], v[1], v[2]);
        forward = new Vector(v[3], v[4], v[5]);
        right = new Vector(v[6], v[7], v[8]);
    }

    public bool MakingResLists => (console is not null) && SoundscapeClient.SoundscapeMakingResLists() != 0;

    public void TouchSoundFile(string wave)
    {
        if (console is null)
            return;
        fixed (byte* p = Utf8(wave))
            SoundscapeClient.SoundscapeTouchSoundFile(p);
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
