namespace SourceSharp.Abi;

/// <summary>
/// The engine's sound script table, <c>ISoundEmitterSystemBase</c> — the two
/// members the server's model components need: whether a script sound exists,
/// and its name as the table spells it. plan_server_novalve.md P3.
/// </summary>
public interface IVtableSoundEmitter
{
    /// <summary>The script sound's index, or -1 when the table has no such sound.</summary>
    int SoundIndex(string name);

    /// <summary>The name at an index, as the table spells it; null for none.</summary>
    string? SoundName(int index);

    /// <summary>
    /// <c>GetParametersForSound( name, params, GetActorGender( NULL ) )</c>:
    /// a sound script's volume, pitch, pitch range and soundlevel, or null
    /// when the script has no such entry.
    /// </summary>
    SoundScriptParams? ScriptParameters(string name);
}

public sealed unsafe class SoundEmitterBase : IVtableSoundEmitter
{
    public const string InterfaceVersion = "VSoundEmitter002";

    private readonly void* _self;
    private readonly SoundEmitterSystemFwd* _fwd;

    private SoundEmitterBase(void* self, SoundEmitterSystemFwd* table)
    {
        _self = self;
        _fwd = table;
    }

    /// <summary>
    /// From the engine's factory, through <paramref name="engine"/>'s own side
    /// (plan_abi_hotreload D8); null, with the reason, when it does not answer.
    /// </summary>
    public static IVtableSoundEmitter? Acquire(ModuleEngine engine, out string why)
        => Acquire((engine ?? throw new ArgumentNullException(nameof(engine))).Api.Interfaces, out why);

    /// <summary>The same, through one side's own factory: what EngineBound holds per side.</summary>
    internal static IVtableSoundEmitter? Acquire(NativeInterfaceFactory factory, out string why)
        => Forwarders.TryAcquire<SoundEmitterSystemFwd>(factory, SourceSharpFactoryType.AppSystem,
               InterfaceVersion, out void* self, out SoundEmitterSystemFwd* table, out why)
            ? new SoundEmitterBase(self, table)
            : null;

    public int SoundIndex(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _fwd->GetSoundIndex(_self, p);
    }

    public string? SoundName(int index) => Vtable.TextOf(_fwd->GetSoundName(_self, index), 512);

    // Through the forwarders since 2026-10-19, where the SoundScriptParameters
    // slot made the same two calls (GameSound_GetParameters with no actor).
    public SoundScriptParams? ScriptParameters(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var parameters = SoundParametersLayout.Stock();
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
        {
            int gender = _fwd->GetActorGender(_self, null);
            if (_fwd->GetParametersForSound(_self, p, &parameters, gender, 0) == 0)
                return null;
        }

        return new SoundScriptParams(parameters.Volume, parameters.Pitch, parameters.PitchLow,
                                     parameters.PitchHigh, parameters.SoundLevel);
    }
}

/// <summary>
/// <c>CSoundParameters</c> (public/SoundEmitterSystem/isoundemittersystembase.h)
/// at its offsets, filled in place by GetParametersForSound.
/// vtable_index_agreement_test's SoundParametersLayout holds the offsets to the
/// header; SoundScriptParametersFoldTests holds them here.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit, Size = SizeInBytes)]
public unsafe struct SoundParametersLayout
{
    /// <summary><c>sizeof( CSoundParameters )</c>.</summary>
    public const int SizeInBytes = 164;

    [System.Runtime.InteropServices.FieldOffset(0)] public int Channel;
    [System.Runtime.InteropServices.FieldOffset(4)] public float Volume;
    [System.Runtime.InteropServices.FieldOffset(8)] public int Pitch;
    [System.Runtime.InteropServices.FieldOffset(12)] public int PitchLow;
    [System.Runtime.InteropServices.FieldOffset(16)] public int PitchHigh;
    [System.Runtime.InteropServices.FieldOffset(20)] public int SoundLevel;
    [System.Runtime.InteropServices.FieldOffset(24)] public byte PlayToOwnerOnly;
    [System.Runtime.InteropServices.FieldOffset(28)] public int Count;
    [System.Runtime.InteropServices.FieldOffset(32)] public fixed byte SoundName[128];
    [System.Runtime.InteropServices.FieldOffset(160)] public int DelayMsec;

    /// <summary>
    /// What CSoundParameters' constructor writes: CHAN_AUTO, VOL_NORM,
    /// PITCH_NORM three times, SNDLVL_NORM, an empty name, the rest zero.
    /// </summary>
    public static SoundParametersLayout Stock() => new()
    {
        Channel = 0,
        Volume = 1.0f,
        Pitch = 100,
        PitchLow = 100,
        PitchHigh = 100,
        SoundLevel = 75,
    };
}
