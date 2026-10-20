using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// Every engine call the client's sound emitter system makes: the script
/// table's load, unload and level overrides, the engine's level name and the
/// file system's question, each one forwarder call.
/// </summary>
public interface IClientSoundSystemEngine
{
    /// <summary><c>soundemitterbase->ModInit()</c>: false when the scripts could not be loaded.</summary>
    bool ModInit();

    /// <summary><c>soundemitterbase->ModShutdown()</c>.</summary>
    void ModShutdown();

    /// <summary><c>engine->GetLevelName()</c>: "maps/&lt;map&gt;.bsp".</summary>
    string LevelName();

    /// <summary><c>filesystem->FileExists( name, path )</c>.</summary>
    bool FileExists(string name, string pathId);

    /// <summary><c>soundemitterbase->AddSoundOverrides( script, preload )</c>.</summary>
    void AddSoundOverrides(string scriptFile, bool preload);

    /// <summary><c>soundemitterbase->ClearSoundOverrides()</c>.</summary>
    void ClearSoundOverrides();
}

/// <summary>
/// The client's engine sound calls that do not go through the emitter: a
/// soundscape's ambient wave and a tracer's whiz, which stock played straight
/// on <c>enginesound</c>.
/// </summary>
public interface IClientEngineSound
{
    /// <summary><c>enginesound->EmitAmbientSound( sample, volume, pitch, flags, soundtime )</c>.</summary>
    void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime = 0.0f);

    /// <summary>
    /// <c>enginesound->EmitSound( filter, entindex, channel, sample, volume,
    /// attenuation, flags, pitch, specialdsp, origin, direction, NULL )</c>,
    /// over <paramref name="recipients"/>.
    /// </summary>
    void EmitSoundAttenuation(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                              float attenuation, int flags, int pitch, int specialDsp, Vector? origin,
                              Vector? direction);

    /// <summary><c>enginesound->StopSound( entindex, channel, sample )</c>.</summary>
    void StopSound(int entIndex, int channel, string sample);
}

/// <summary>
/// <see cref="ISoundEmitterEngine"/> on the client, over the forwarder
/// wrappers, and the one native <c>IRecipientFilter</c> every managed emit
/// hands the engine (libss_fwd_client's, over <see cref="RecipientFilterSink"/>):
/// the sink is pointed at the emit's recipients for exactly the EmitSound call.
/// The client's EmitAmbientSound takes no entity, origin or level.
/// </summary>
public sealed class ClientSoundEmitterEngine(IFwdSoundEmitter scripts, IFwdEngineSoundClient sound,
                                             IFwdEngineClient engine, RecipientFilterSink sink, nint nativeFilter)
    : ISoundEmitterEngine, IClientEngineSound
{
    public int SoundIndex(string name) => scripts.SoundIndex(name);

    public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender)
        => scripts.ParametersForSound(name, ref parameters, gender);

    public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters, int gender,
                                     bool beingEmitted)
        => scripts.ParametersForSoundEx(name, ref handle, ref parameters, gender, beingEmitted);

    public SoundScriptEntry? Entry(int index) => scripts.Entry(index);

    public string? WavFileForSound(string name, int gender) => scripts.WavFileForSound(name, gender);

    public int LookupSoundLevel(string name) => scripts.LookupSoundLevel(name);

    public int ActorGender(string? model) => scripts.ActorGender(model);

    public void EmitSound(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                          int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime,
                          int speakerEntity)
    {
        using (sink.Use(recipients))
            sound.EmitSound(nativeFilter, entIndex, channel, sample, volume, soundLevel, flags, pitch, specialDsp,
                            origin, soundTime, speakerEntity);
    }

    public void StopSound(int entIndex, int channel, string sample) => sound.StopSound(entIndex, channel, sample);

    public float SoundDuration(string sample) => sound.SoundDuration(sample);

    public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel, int flags,
                                 int pitch, float soundTime)
        => sound.EmitAmbientSound(sample, volume, pitch, flags, soundTime);

    public int SentenceIndexFromName(string name) => engine.SentenceIndexFromName(name);

    // IClientEngineSound
    public void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime = 0.0f)
        => sound.EmitAmbientSound(sample, volume, pitch, flags, soundTime);

    public void EmitSoundAttenuation(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                                     float attenuation, int flags, int pitch, int specialDsp, Vector? origin,
                                     Vector? direction)
    {
        using (sink.Use(recipients))
            sound.EmitSoundAttenuation(nativeFilter, entIndex, channel, sample, volume, attenuation, flags, pitch,
                                       specialDsp, origin, direction);
    }
}

/// <summary><see cref="IClientSoundSystemEngine"/> over the script table, the engine client and the file system.</summary>
public sealed class ClientSoundSystemEngine(IFwdSoundEmitter scripts, IFwdEngineClient engine,
                                            IFwdBaseFileSystem files) : IClientSoundSystemEngine
{
    public bool ModInit() => scripts.ModInit();

    public void ModShutdown() => scripts.ModShutdown();

    public string LevelName() => engine.LevelName() ?? string.Empty;

    public bool FileExists(string name, string pathId) => files.FileExists(name, pathId);

    public void AddSoundOverrides(string scriptFile, bool preload) => scripts.AddSoundOverrides(scriptFile, preload);

    public void ClearSoundOverrides() => scripts.ClearSoundOverrides();
}
