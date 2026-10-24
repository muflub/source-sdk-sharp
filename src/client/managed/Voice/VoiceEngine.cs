using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Voice;

/// <summary><see cref="IVoiceEngine"/> over the engine client and voice_modenable.</summary>
public sealed class VoiceEngine(IFwdEngineClient engine, VoiceModEnableConVar? modEnable) : IVoiceEngine
{
    public string? GuidOf(int client)
        => engine.TryGetPlayerInfo(client, out var info) ? info.Guid : null;

    public void ServerCommand(string line, bool reliable) => engine.VoiceServerCommand(line, reliable);

    public void EnableVoice() => modEnable?.SetValue(true);
}

/// <summary><see cref="IVoiceBanFile"/> as the mod directory's voice_ban.dt (where stock's file system wrote it).</summary>
public sealed class VoiceBanFile(string gameDir, ILog log) : IVoiceBanFile
{
    private string Path => System.IO.Path.Combine(gameDir, VoiceBanList.FileName);

    public byte[]? Read() => File.Exists(Path) ? File.ReadAllBytes(Path) : null;

    public void Write(byte[] file)
    {
        try
        {
            File.WriteAllBytes(Path, file);
        }
        catch (IOException ex)
        {
            log.Warning($"[SourceSharp/Voice] could not write {Path}: {ex.Message}\n");
        }
    }
}

/// <summary>The engine's VoiceStatus to every listener: the HUD's speakers and the voice manager.</summary>
public sealed class VoiceStatusFanOut(params IVoiceStatusEvents[] listeners) : IVoiceStatusEvents
{
    public void Status(int entIndex, bool talking)
    {
        foreach (var l in listeners)
            l.Status(entIndex, talking);
    }
}
