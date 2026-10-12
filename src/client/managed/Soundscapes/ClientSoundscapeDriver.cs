using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared.Soundscapes;

namespace SourceSharp.Client.Soundscapes;

/// <summary>
/// The client-system shell's three hooks, landed on <see cref="ClientSoundscapes"/>:
/// the level's scripts come from the engine filesystem through the shared
/// <see cref="SoundscapeLibrary"/>, the fade time from
/// <c>soundscape_fadetime</c>.
/// </summary>
public sealed class ClientSoundscapeDriver(ClientSoundscapes soundscapes, IEngineFiles files, ILog log,
                                           SoundscapeFadetimeConVar fadeTime) : IClientSoundscapesDriver
{
    private string _mapName = string.Empty;

    /// <summary>The map the scripts were last loaded for (cl_soundscape_flush reloads them).</summary>
    public string MapName => _mapName;

    /// <summary>The library for <paramref name="mapName"/>, read through the engine filesystem.</summary>
    public SoundscapeLibrary Load(string mapName)
        => SoundscapeLibrary.Load(files.ReadGameFile, string.IsNullOrEmpty(mapName) ? null : mapName,
                                  warning => log.Warning("C_SoundscapeSystem::Init:  " + warning + "\n"));

    public void LevelInit(string mapName)
    {
        _mapName = mapName;
        soundscapes.LevelInit(Load(mapName));
    }

    public void Update(float frameTime) => soundscapes.Update(frameTime, fadeTime.Value);

    public void LevelShutdown() => soundscapes.OnStopAllSounds();
}
