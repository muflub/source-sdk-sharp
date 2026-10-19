namespace SourceSharp.Abi;

/// <summary>
/// The server's <c>IEngineSound</c> (<c>IEngineSoundServer003</c>): what the
/// managed SoundEmitter emits and stops through (plan_server_native_v2 T4a,
/// sound_emitter.cpp's emit path moved). Server.Interop's, because its
/// forwarder table is libss_fwd_server's alone.
/// </summary>
public interface IVtableEngineSoundServer
{
    /// <summary>
    /// <c>EmitSound( filter, entindex, channel, sample, volume, soundlevel,
    /// flags, pitch, specialdsp, origin, NULL, NULL, true, soundtime,
    /// speakerentity )</c>: the soundlevel overload. <paramref name="filter"/>
    /// is the native <c>IRecipientFilter</c> the engine calls back, which
    /// the side builds over a managed table (plan T4a, shape 2c). No
    /// origin list is asked for, so nothing the engine allocates crosses.
    /// </summary>
    void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel,
                   int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity);

    /// <summary><c>StopSound( entindex, channel, sample )</c>.</summary>
    void StopSound(int entIndex, int channel, string sample);

    /// <summary><c>GetSoundDuration( sample )</c>, in seconds.</summary>
    float SoundDuration(string sample);
}

/// <summary><see cref="IVtableEngineSoundServer"/> over the engine's object, through its forwarders.</summary>
public sealed unsafe class EngineSoundServer : IVtableEngineSoundServer
{
    /// <summary>The server's IEngineSound (IEngineSound.h's IENGINESOUND_SERVER_INTERFACE_VERSION).</summary>
    public const string InterfaceVersion = "IEngineSoundServer003";

    private readonly void* _self;
    private readonly EngineSoundFwd* _fwd;

    internal EngineSoundServer(void* self, EngineSoundFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>The server's engine sound, or null with the reason (null on the client).</summary>
    public static IVtableEngineSoundServer? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>The same, through one side's own interface factory.</summary>
    internal static IVtableEngineSoundServer? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<EngineSoundFwd>(factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out EngineSoundFwd* table, out why)
            ? new EngineSoundServer(self, table)
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
}
