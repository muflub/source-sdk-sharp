using System.Text;

using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// <see cref="ISoundscapeAudio"/> over IShimSoundscapeClient and the console's
/// two named-convar api. A missing shim plays nothing.
/// </summary>
internal sealed unsafe class SoundscapeAudioSlots(ConsoleArea console, SoundscapeClientArea soundscapeClient) : ISoundscapeAudio
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s + "\0");




    public void SetRoomType(int roomType)
    {
        soundscapeClient.SoundscapeSetRoomType(roomType);
    }

    public void SetPlayerDsp(int dspType)
    {
        soundscapeClient.SoundscapeSetPlayerDsp(dspType);
    }

    public void SetConVar(string name, string value)
    {
        fixed (byte* n = Utf8(name))
        fixed (byte* v = Utf8(value))
            console.SetNamedConVarValue(n, v);
    }

    public void RevertConVar(string name)
    {
        fixed (byte* n = Utf8(name))
            console.RevertNamedConVar(n);
    }

    public void MainView(out Vector origin, out Vector forward, out Vector right)
    {
        float* v = stackalloc float[9];
        for (int i = 0; i < 9; i++)
            v[i] = 0;
        soundscapeClient.SoundscapeMainView(v);
        origin = new Vector(v[0], v[1], v[2]);
        forward = new Vector(v[3], v[4], v[5]);
        right = new Vector(v[6], v[7], v[8]);
    }

    public bool MakingResLists => soundscapeClient.SoundscapeMakingResLists() != 0;

    public void TouchSoundFile(string wave)
    {
        fixed (byte* p = Utf8(wave))
            soundscapeClient.SoundscapeTouchSoundFile(p);
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
