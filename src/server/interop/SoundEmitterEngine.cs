using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// The server's half of the emitter's engine (plan_server_native_v2 T4a):
/// <see cref="ISoundEmitterEngine"/>, which both sides' emitters share, and
/// the PAS question the server's recipient rules start from, which the
/// client never asks.
/// </summary>
public interface IServerSoundEmitterEngine : ISoundEmitterEngine
{
    /// <summary><c>engine->Message_DetermineMulticastRecipients( usepas, origin, bits )</c>.</summary>
    void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits);
}

/// <summary>
/// <see cref="IServerSoundEmitterEngine"/> over the forwarder wrappers, and the one
/// native <c>IRecipientFilter</c> every emit hands the engine: the sink is
/// pointed at the emit's recipients for exactly the EmitSound call.
/// </summary>
public sealed class SoundEmitterEngine(IFwdSoundEmitter scripts, IFwdEngineSoundServer sound,
                                       IFwdEngineServer server, RecipientFilterSink sink, nint nativeFilter)
    : IServerSoundEmitterEngine
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
