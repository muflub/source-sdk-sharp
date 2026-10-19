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

    /// <summary>
    /// <c>GetParametersForSound( name, params, gender )</c> into
    /// <paramref name="parameters"/>, in place; false when the script has no
    /// such entry.
    /// </summary>
    bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender);

    /// <summary>
    /// <c>GetParametersForSoundEx( name, handle, params, gender, isbeingemitted )</c>:
    /// fills <paramref name="parameters"/> in place, and the script's handle
    /// into <paramref name="handle"/> when it was -1. False when the script
    /// has no such entry.
    /// </summary>
    bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters,
                              int gender, bool beingEmitted);

    /// <summary>
    /// <c>InternalGetParametersForSound( index )</c>'s channel and wave names,
    /// in the entry's order (each through <c>GetWaveName</c>); null for an
    /// index the table has no entry at.
    /// </summary>
    SoundScriptEntry? Entry(int index);

    /// <summary><c>GetWavFileForSound( name, gender )</c>, or null.</summary>
    string? WavFileForSound(string name, int gender);

    /// <summary><c>LookupSoundLevel( name )</c>: the script's soundlevel, or SNDLVL_NONE (0).</summary>
    int LookupSoundLevel(string name);

    /// <summary><c>GetActorGender( model )</c>: gender_t, 0 for none or a null model.</summary>
    int ActorGender(string? model);
}

/// <summary>
/// What <c>CSoundParametersInternal</c> holds that the emitter's stop reads:
/// the channel and every wave of the entry (the rndwave list), named.
/// </summary>
public sealed record SoundScriptEntry(int Channel, IReadOnlyList<string> Waves);

public sealed unsafe class SoundEmitterBase : IVtableSoundEmitter
{
    public const string InterfaceVersion = "VSoundEmitter002";

    private readonly void* _self;
    private readonly SoundEmitterSystemFwd* _fwd;

    internal SoundEmitterBase(void* self, SoundEmitterSystemFwd* table)
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

    public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
        fixed (SoundParametersLayout* into = &parameters)
            return _fwd->GetParametersForSound(_self, p, into, gender, 0) != 0;
    }

    public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters,
                                     int gender, bool beingEmitted)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
        fixed (short* h = &handle)
        fixed (SoundParametersLayout* into = &parameters)
            return _fwd->GetParametersForSoundEx(_self, p, h, into, gender, beingEmitted ? (byte)1 : (byte)0) != 0;
    }

    public SoundScriptEntry? Entry(int index)
    {
        byte* entry = (byte*)_fwd->InternalGetParametersForSound(_self, index);
        if (entry == null)
            return null;

        int count = SoundParametersInternalLayout.WaveCount(entry);
        ushort* symbols = SoundParametersInternalLayout.Waves(entry);
        var waves = new List<string>(count);

        for (int i = 0; i < count; i++)
        {
            // GetWaveName takes a CUtlSymbol &: the symbol's own two bytes,
            // which SoundFile holds first.
            ushort* symbol = (ushort*)((byte*)symbols + i * SoundParametersInternalLayout.SoundFileSize);
            waves.Add(Vtable.TextOf(_fwd->GetWaveName(_self, symbol), 512) ?? string.Empty);
        }

        return new SoundScriptEntry(SoundParametersInternalLayout.Channel(entry), waves);
    }

    public string? WavFileForSound(string name, int gender)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return Vtable.TextOf(_fwd->GetWavFileForSound_2(_self, p, gender), 512);
    }

    public int LookupSoundLevel(string name)
    {
        byte[] bytes = Vtable.Utf8(name);

        fixed (byte* p = bytes)
            return _fwd->LookupSoundLevel(_self, p);
    }

    public int ActorGender(string? model)
    {
        if (model is null)
            return _fwd->GetActorGender(_self, null);

        byte[] bytes = Vtable.Utf8(model);

        fixed (byte* p = bytes)
            return _fwd->GetActorGender(_self, p);
    }
}

/// <summary>
/// <c>CSoundParametersInternal</c> (isoundemittersystembase.h, <c>#pragma pack(1)</c>)
/// at the offsets the emitter's stop reads: the wave list and the channel.
/// vtable_index_agreement_test's SoundParametersInternalLayout holds them to
/// the header through its own inline accessors.
/// </summary>
public static unsafe class SoundParametersInternalLayout
{
    /// <summary><c>sizeof( CSoundParametersInternal )</c>.</summary>
    public const int SizeInBytes = 36;

    /// <summary><c>m_pSoundNames</c>: the wave array, or the one wave itself when there is one.</summary>
    public const int SoundNamesOffset = 0;

    /// <summary><c>m_nSoundNames</c>, a uint16.</summary>
    public const int SoundNameCountOffset = 16;

    /// <summary><c>channel</c>, a uint16.</summary>
    public const int ChannelOffset = 30;

    /// <summary><c>sizeof( SoundFile )</c>: the symbol (uint16), the gender, the flag.</summary>
    public const int SoundFileSize = 4;

    /// <summary><c>NumSoundNames()</c>.</summary>
    public static int WaveCount(byte* entry) => *(ushort*)(entry + SoundNameCountOffset);

    /// <summary><c>GetChannel()</c>.</summary>
    public static int Channel(byte* entry) => *(ushort*)(entry + ChannelOffset);

    /// <summary>
    /// <c>GetSoundNames()</c>: with ONE wave the SoundFile is stored in the
    /// pointer's own bytes, so the list is the field's address; otherwise it
    /// is what the field points at.
    /// </summary>
    public static ushort* Waves(byte* entry)
        => WaveCount(entry) == 1
            ? (ushort*)(entry + SoundNamesOffset)
            : *(ushort**)(entry + SoundNamesOffset);
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

    /// <summary><c>soundname</c> as text: the bytes up to the first NUL.</summary>
    public string Name
    {
        get
        {
            fixed (byte* p = SoundName)
            {
                int length = 0;
                while (length < 128 && p[length] != 0)
                    length++;
                return System.Text.Encoding.UTF8.GetString(p, length);
            }
        }
        set
        {
            fixed (byte* p = SoundName)
            {
                var span = new Span<byte>(p, 128);
                span.Clear();
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
                int written = Math.Min(bytes.Length, 127);
                bytes.AsSpan(0, written).CopyTo(span);
                p[written] = 0;
            }
        }
    }

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
