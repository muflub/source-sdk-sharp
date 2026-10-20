// The engine interfaces a composition registers (EngineServices.AddTo): each
// member is one delegation to the side's bound slot, and the compiler holds
// every interface member implemented.

namespace SourceSharp.Abi;

/// <summary>
/// <see cref="IFwdEngineClient"/> over the side's bound IVEngineClient: each call asks
/// <see cref="EngineBound.Client"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundEngineClient(EngineSide side) : IFwdEngineClient
{
    public string? LevelName()
        => EngineBound.Client(side).LevelName();

    public string? GameDirectory()
        => EngineBound.Client(side).GameDirectory();

    public uint ProtocolVersion()
        => EngineBound.Client(side).ProtocolVersion();

    public void Reconnect()
        => EngineBound.Client(side).Reconnect();

    public void RequestManagedReload()
        => EngineBound.Client(side).RequestManagedReload();

    public void ConsolePrintAt(int position, string text)
        => EngineBound.Client(side).ConsolePrintAt(position, text);

    public void ConsolePrintAt(int position, float timeToLive, float red, float green, float blue, bool fixedWidthFont, string text)
        => EngineBound.Client(side).ConsolePrintAt(position, timeToLive, red, green, blue, fixedWidthFont, text);

    public bool TryLightForPoint(in Vector position, bool clamp, out Vector light)
        => EngineBound.Client(side).TryLightForPoint(in position, clamp, out light);

    public bool IsInGame()
        => EngineBound.Client(side).IsInGame();

    public bool TryLightForPointFast(in Vector position, bool clamp, out Vector light)
        => EngineBound.Client(side).TryLightForPointFast(in position, clamp, out light);

    public bool TryGetPlayerInfo(int entIndex, out ClientPlayerInfo info)
        => EngineBound.Client(side).TryGetPlayerInfo(entIndex, out info);

    public void JoinTeam(int team)
        => EngineBound.Client(side).JoinTeam(team);

    public void ConnectFromSteamLaunch(string connectLine)
        => EngineBound.Client(side).ConnectFromSteamLaunch(connectLine);

    public void SubmitConsoleLine(string line)
        => EngineBound.Client(side).SubmitConsoleLine(line);

    public void SpectatorCommand(SpectatorKeyCommand command)
        => EngineBound.Client(side).SpectatorCommand(command);

    public void SpectatorServerCommand(string line)
        => EngineBound.Client(side).SpectatorServerCommand(line);

    public void VoiceServerCommand(string line, bool reliable)
        => EngineBound.Client(side).VoiceServerCommand(line, reliable);

    public int SentenceIndexFromName(string name)
        => EngineBound.Client(side).SentenceIndexFromName(name);

    public int LocalPlayerSlot()
        => EngineBound.Client(side).LocalPlayerSlot();
}

/// <summary>
/// <see cref="IFwdInputSystem"/> over the side's bound IInputSystem: each call asks
/// <see cref="EngineBound.InputSystem"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundInputSystem(EngineSide side) : IFwdInputSystem
{
    public string? ButtonName(int buttonCode)
        => EngineBound.InputSystem(side).ButtonName(buttonCode);
}

/// <summary>
/// <see cref="IFwdEngineSoundClient"/> over the side's bound IEngineSound (client): each call asks
/// <see cref="EngineBound.SoundClient"/>, which acquires the interface on first use
/// and throws, naming it, if the engine refuses it. What the composition
/// registers, so a consumer is handed the interface by constructor before the
/// engine serves it, and never a null.
/// </summary>
internal sealed class BoundSoundClient(EngineSide side) : IFwdEngineSoundClient
{
    public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity)
        => EngineBound.SoundClient(side).EmitSound(filter, entIndex, channel, sample, volume, soundLevel, flags, pitch, specialDsp, origin, soundTime, speakerEntity);

    public void EmitSoundAttenuation(nint filter, int entIndex, int channel, string sample, float volume, float attenuation, int flags, int pitch, int specialDsp, Vector? origin, Vector? direction)
        => EngineBound.SoundClient(side).EmitSoundAttenuation(filter, entIndex, channel, sample, volume, attenuation, flags, pitch, specialDsp, origin, direction);

    public void StopSound(int entIndex, int channel, string sample)
        => EngineBound.SoundClient(side).StopSound(entIndex, channel, sample);

    public float SoundDuration(string sample)
        => EngineBound.SoundClient(side).SoundDuration(sample);

    public void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime)
        => EngineBound.SoundClient(side).EmitAmbientSound(sample, volume, pitch, flags, soundTime);
}
