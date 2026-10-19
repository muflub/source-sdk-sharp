using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Every engine call the managed SoundEmitter makes (plan_server_native_v2
/// T4a): the script table's, the server engine sound's and the engine
/// server's, each one forwarder call. The emitter decides; this only carries
/// out, so a test can stand in for the engine with a recorder.
/// </summary>
public interface ISoundEmitterEngine
{
    /// <summary><c>soundemitterbase->GetSoundIndex( name )</c>: -1 for no such script.</summary>
    int SoundIndex(string name);

    /// <summary><c>GetParametersForSound( name, params, gender )</c>.</summary>
    bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender);

    /// <summary><c>GetParametersForSoundEx( name, handle, params, gender, isbeingemitted )</c>.</summary>
    bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters, int gender,
                              bool beingEmitted);

    /// <summary><c>InternalGetParametersForSound( index )</c>'s channel and waves.</summary>
    SoundScriptEntry? Entry(int index);

    /// <summary><c>GetWavFileForSound( name, gender )</c>.</summary>
    string? WavFileForSound(string name, int gender);

    /// <summary><c>LookupSoundLevel( name )</c>.</summary>
    int LookupSoundLevel(string name);

    /// <summary><c>GetActorGender( model )</c>.</summary>
    int ActorGender(string? model);

    /// <summary><c>enginesound->EmitSound( filter, ... )</c>, the soundlevel overload, over <paramref name="recipients"/>.</summary>
    void EmitSound(IRecipientList recipients, int entIndex, int channel, string sample, float volume, int soundLevel,
                   int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity);

    /// <summary><c>enginesound->StopSound( entindex, channel, sample )</c>.</summary>
    void StopSound(int entIndex, int channel, string sample);

    /// <summary><c>enginesound->GetSoundDuration( sample )</c>.</summary>
    float SoundDuration(string sample);

    /// <summary><c>engine->EmitAmbientSound( ... )</c>.</summary>
    void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel, int flags,
                          int pitch, float soundTime);

    /// <summary><c>engine->SentenceIndexFromName( name )</c>.</summary>
    int SentenceIndexFromName(string name);

    /// <summary><c>engine->Message_DetermineMulticastRecipients( usepas, origin, bits )</c>.</summary>
    void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits);
}

/// <summary>
/// <see cref="ISoundEmitterEngine"/> over the forwarder wrappers, and the one
/// native <c>IRecipientFilter</c> every emit hands the engine: the sink is
/// pointed at the emit's recipients for exactly the EmitSound call.
/// </summary>
public sealed class SoundEmitterEngine(IVtableSoundEmitter scripts, IVtableEngineSoundServer sound,
                                       IVtableEngineServer server, RecipientFilterSink sink, nint nativeFilter)
    : ISoundEmitterEngine
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
        => server.EmitAmbientSound(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime);

    public int SentenceIndexFromName(string name) => server.SentenceIndexFromName(name);

    public void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits)
        => server.MulticastRecipients(usePas, origin, playerBits);
}
