namespace SourceSharp.Abi;

/// <summary>
/// The client's <c>IEngineSound</c> (<c>IEngineSoundClient003</c>): what the
/// managed client sound emitter, the tracers and the soundscapes emit and stop
/// through (sound_emitter.cpp's client emit path, moved). The same class and
/// forwarder table as the server's (<see cref="EngineSoundFwd"/>, both
/// libraries), under the client's version string.
/// </summary>
public interface IFwdEngineSoundClient
{
    /// <summary>
    /// <c>EmitSound( filter, entindex, channel, sample, volume, soundlevel,
    /// flags, pitch, specialdsp, origin, NULL, NULL, true, soundtime,
    /// speakerentity )</c>: the soundlevel overload. <paramref name="filter"/>
    /// is a native <c>IRecipientFilter</c> the engine calls back.
    /// </summary>
    void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel,
                   int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity);

    /// <summary>
    /// <c>EmitSound( filter, entindex, channel, sample, volume, attenuation,
    /// flags, pitch, specialdsp, origin, direction, NULL )</c>: the attenuation
    /// overload, with a direction (a tracer's whiz).
    /// </summary>
    void EmitSoundAttenuation(nint filter, int entIndex, int channel, string sample, float volume, float attenuation,
                              int flags, int pitch, int specialDsp, Vector? origin, Vector? direction);

    /// <summary><c>StopSound( entindex, channel, sample )</c>.</summary>
    void StopSound(int entIndex, int channel, string sample);

    /// <summary><c>GetSoundDuration( sample )</c>, in seconds.</summary>
    float SoundDuration(string sample);

    /// <summary><c>EmitAmbientSound( sample, volume, pitch, flags, soundtime )</c>: the client's, with no entity.</summary>
    void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime);
}

/// <summary><see cref="IFwdEngineSoundClient"/> over the engine's object, through its forwarders.</summary>
public sealed unsafe class EngineSoundClient : IFwdEngineSoundClient
{
    /// <summary>The client's IEngineSound (IEngineSound.h's IENGINESOUND_CLIENT_INTERFACE_VERSION).</summary>
    public const string InterfaceVersion = "IEngineSoundClient003";

    private readonly void* _self;
    private readonly EngineSoundFwd* _fwd;

    internal EngineSoundClient(void* self, EngineSoundFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The client's engine sound, or null with the reason (null on the server).</summary>
    internal static IFwdEngineSoundClient? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<EngineSoundFwd>(factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out EngineSoundFwd* table, out why)
            ? new EngineSoundClient(self, table)
            : null;

    /// <inheritdoc/>
    public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel,
                          int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity)
    {
        if (filter == 0)
            throw new ArgumentException("EmitSound needs the native recipient filter", nameof(filter));

        Vector at = origin ?? default;

        fixed (byte* p = Vtable.Utf8(sample))
            _fwd->EmitSound_2(_self, (void*)filter, entIndex, channel, p, volume, soundLevel, flags, pitch,
                              specialDsp, origin.HasValue ? &at : null, null, null, 1, soundTime, speakerEntity);
    }

    /// <inheritdoc/>
    public void EmitSoundAttenuation(nint filter, int entIndex, int channel, string sample, float volume,
                                     float attenuation, int flags, int pitch, int specialDsp, Vector? origin,
                                     Vector? direction)
    {
        if (filter == 0)
            throw new ArgumentException("EmitSound needs the native recipient filter", nameof(filter));

        Vector at = origin ?? default;
        Vector toward = direction ?? default;

        // The header's defaults for the rest: no origin list, update the
        // positions, now, no speaker.
        fixed (byte* p = Vtable.Utf8(sample))
            _fwd->EmitSound(_self, (void*)filter, entIndex, channel, p, volume, attenuation, flags, pitch, specialDsp,
                            origin.HasValue ? &at : null, direction.HasValue ? &toward : null, null, 1, 0.0f, -1);
    }

    /// <inheritdoc/>
    public void StopSound(int entIndex, int channel, string sample)
    {
        fixed (byte* p = Vtable.Utf8(sample))
            _fwd->StopSound(_self, entIndex, channel, p);
    }

    /// <inheritdoc/>
    public float SoundDuration(string sample)
    {
        fixed (byte* p = Vtable.Utf8(sample))
            return _fwd->GetSoundDuration(_self, p);
    }

    /// <inheritdoc/>
    public void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime)
    {
        fixed (byte* p = Vtable.Utf8(sample))
            _fwd->EmitAmbientSound(_self, p, volume, pitch, flags, soundTime);
    }
}
