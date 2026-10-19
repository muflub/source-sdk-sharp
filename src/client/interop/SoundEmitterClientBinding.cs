using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// One <c>GameSound_Emit</c> from a native caller, as it crossed: stock's
/// <c>EmitSound_t</c> by its fields. <see cref="ByHandle"/> is the handle
/// overloads' route, which skips the raw-wave arm.
/// </summary>
public readonly record struct ClientSoundEmitRequest(
    int Channel, string? SoundName, float Volume, int SoundLevel, int Flags, int Pitch, int SpecialDsp,
    Vector? Origin, float SoundTime, bool WantDuration, bool EmitCloseCaption, bool WarnOnMissingCloseCaption,
    bool WarnOnDirectWaveReference, int SpeakerEntity, bool ByHandle);

/// <summary>
/// The client's sound emitter as native's last callers ask it
/// (IManagedSoundEmitterClient): the CSoundEmitterSystem row's four stages,
/// and the GameSound_* calls of the native code that still makes sounds (a
/// bullet impact, a sound patch, a broadcast). The recipients native built
/// cross as their slots.
/// </summary>
public interface IClientSoundEmitterSink
{
    /// <summary><c>CSoundEmitterSystem::Init</c>: false stops the client coming up.</summary>
    bool SystemInit();

    /// <summary><c>CSoundEmitterSystem::Shutdown</c>.</summary>
    void SystemShutdown();

    /// <summary><c>CSoundEmitterSystem::LevelInitPreEntity</c>.</summary>
    void LevelInitPreEntity();

    /// <summary><c>CSoundEmitterSystem::LevelShutdownPostEntity</c>.</summary>
    void LevelShutdownPostEntity();

    /// <summary>
    /// <c>GameSound_Emit</c>: <paramref name="handle"/> is the script handle
    /// the call carries (the EmitSound_t's own, or the caller's), which the
    /// first lookup fills in. Answers the sound's duration when one was asked
    /// for and written.
    /// </summary>
    float? Emit(ReadOnlySpan<int> slots, bool reliable, bool initMessage, int entIndex,
                in ClientSoundEmitRequest request, ref short handle);

    /// <summary><c>GameSound_Stop( entindex, channel, sample )</c>.</summary>
    void Stop(int entIndex, int channel, string? sample);

    /// <summary><c>GameSound_EmitCloseCaption( filter, entindex, token, origins, duration, warnifmissing )</c>.</summary>
    void EmitCloseCaption(ReadOnlySpan<int> slots, bool reliable, bool initMessage, int entIndex, string token,
                          float duration, bool warnIfMissing);
}

/// <summary>
/// <see cref="IManagedSoundEmitterClient"/>, managed-built: each body counts
/// its own crossing and decodes what native packed. A recipient buffer is
/// length-prefixed: [count, flags (1 reliable, 2 init message), slots...],
/// at most <see cref="MaxRecipients"/> slots. Every failure answer is 0, what
/// the generated thunk's catch answers.
/// </summary>
public sealed unsafe class SoundEmitterClientBinding(Crossings crossings, IClientSoundEmitterSink sink)
    : IManagedSoundEmitterClient
{
    /// <summary>ABSOLUTE_PLAYER_LIMIT: no filter holds more.</summary>
    public const int MaxRecipients = 255;

    /// <summary><c>nOptions</c>' bits: the EmitSound_t's three flags and the route.</summary>
    public const int OptionEmitCloseCaption = 1, OptionWarnOnMissingCloseCaption = 2,
                     OptionWarnOnDirectWaveReference = 4, OptionByHandle = 8;

    public byte ClientSoundSystemInit()
    {
        crossings.Count(CrossingOp.ClientSoundSystemInit);
        return sink.SystemInit() ? (byte)1 : (byte)0;
    }

    public void ClientSoundSystemShutdown()
    {
        crossings.Count(CrossingOp.ClientSoundSystemShutdown);
        sink.SystemShutdown();
    }

    public void ClientSoundLevelInitPreEntity()
    {
        crossings.Count(CrossingOp.ClientSoundLevelInitPreEntity);
        sink.LevelInitPreEntity();
    }

    public void ClientSoundLevelShutdownPostEntity()
    {
        crossings.Count(CrossingOp.ClientSoundLevelShutdownPostEntity);
        sink.LevelShutdownPostEntity();
    }

    public void ClientSoundEmit(void* a0, int a1, int a2, byte* a3, float a4, int a5, int a6, int a7, int a8, void* a9,
                                float a10, void* a11, int a12, int a13, void* a14)
    {
        crossings.Count(CrossingOp.ClientSoundEmit);
        if (!TryRecipients((int*)a0, out ReadOnlySpan<int> slots, out bool reliable, out bool init))
            return;

        Vector? origin = a9 == null ? null : new Vector(((float*)a9)[0], ((float*)a9)[1], ((float*)a9)[2]);
        var request = new ClientSoundEmitRequest(
            a2, a3 == null ? null : Vtable.TextOf(a3, 512) ?? string.Empty, a4, a5, a6, a7, a8, origin, a10,
            WantDuration: a11 != null,
            EmitCloseCaption: (a12 & OptionEmitCloseCaption) != 0,
            WarnOnMissingCloseCaption: (a12 & OptionWarnOnMissingCloseCaption) != 0,
            WarnOnDirectWaveReference: (a12 & OptionWarnOnDirectWaveReference) != 0,
            SpeakerEntity: a13,
            ByHandle: (a12 & OptionByHandle) != 0);

        int* pHandle = (int*)a14;
        short handle = pHandle == null ? (short)-1 : (short)*pHandle;
        float? duration = sink.Emit(slots, reliable, init, a1, request, ref handle);
        if (pHandle != null)
            *pHandle = handle;
        if (duration is { } d && a11 != null)
            *(float*)a11 = d;
    }

    public void ClientSoundStop(int a0, int a1, byte* a2)
    {
        crossings.Count(CrossingOp.ClientSoundStop);
        sink.Stop(a0, a1, a2 == null ? null : Vtable.TextOf(a2, 512) ?? string.Empty);
    }

    public void ClientSoundEmitCloseCaption(void* a0, int a1, byte* a2, float a3, int a4)
    {
        crossings.Count(CrossingOp.ClientSoundEmitCloseCaption);
        if (a2 == null || !TryRecipients((int*)a0, out ReadOnlySpan<int> slots, out bool reliable, out bool init))
            return;

        sink.EmitCloseCaption(slots, reliable, init, a1, Vtable.TextOf(a2, 512) ?? string.Empty, a3, a4 != 0);
    }

    // A refused buffer (none, or a count outside 0..MaxRecipients) is no emit:
    // stock's filter could not have held it.
    private static bool TryRecipients(int* buffer, out ReadOnlySpan<int> slots, out bool reliable, out bool init)
    {
        slots = default;
        reliable = init = false;
        if (buffer == null)
            return false;

        int count = buffer[0];
        if (count < 0 || count > MaxRecipients)
            return false;

        reliable = (buffer[1] & 1) != 0;
        init = (buffer[1] & 2) != 0;
        slots = new ReadOnlySpan<int>(buffer + 2, count);
        return true;
    }
}
