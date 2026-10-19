namespace SourceSharp.Abi;

/// <summary>
/// The recipients of one emit, as the engine asks for them: stock's
/// <c>IRecipientFilter</c> answers, over a list managed chose.
/// </summary>
public interface IRecipientList
{
    /// <summary><c>IsReliable()</c>.</summary>
    bool IsReliable { get; }

    /// <summary><c>IsInitMessage()</c>.</summary>
    bool IsInitMessage { get; }

    /// <summary><c>GetRecipientCount()</c>.</summary>
    int Count { get; }

    /// <summary><c>GetRecipientIndex( slot )</c>: the player's entity index, or -1 past the end.</summary>
    int IndexAt(int slot);
}

/// <summary>
/// Every engine call the shared sound emitter makes (plan_server_native_v2
/// T4a on the server, the client's port of sound_emitter.cpp on the client):
/// the script table's, the engine sound's and the side's ambient and sentence
/// calls, each one forwarder call. The emitter decides; this only carries out,
/// so a test can stand in for the engine with a recorder.
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

    /// <summary>
    /// The side's ambient emit: the server's <c>engine->EmitAmbientSound(
    /// entindex, origin, sample, volume, soundlevel, flags, pitch, soundtime )</c>,
    /// the client's <c>enginesound->EmitAmbientSound( sample, volume, pitch,
    /// flags, soundtime )</c>, which has no entity, origin or level.
    /// </summary>
    void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel, int flags,
                          int pitch, float soundTime);

    /// <summary><c>engine->SentenceIndexFromName( name )</c>: the server's and the client's engine both answer it.</summary>
    int SentenceIndexFromName(string name);
}
