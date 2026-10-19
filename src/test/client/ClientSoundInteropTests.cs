using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client sound emitter's two edges, over fakes: the engine calls it makes
/// through IEngineSoundClient003's forwarders (<see cref="EngineSoundClient"/>,
/// <see cref="ClientSoundEmitterEngine"/>), and what native's last callers hand
/// it across IManagedSoundEmitterClient (<see cref="SoundEmitterClientBinding"/>):
/// the length-prefixed recipients, the EmitSound_t's fields and flags, the
/// handle and duration written back.
/// </summary>
public sealed unsafe class ClientSoundInteropTests : IDisposable
{
    // ---- what the fakes saw (the test assembly may keep statics) ----
    private static nint s_Filter, s_OriginPointer, s_Direction, s_Origins;
    private static int s_EntIndex, s_Channel, s_SoundLevel, s_Flags, s_Pitch, s_Dsp, s_Speaker, s_Calls;
    private static float s_Volume, s_Attenuation, s_SoundTime;
    private static string? s_Sample;
    private static Vector s_Origin, s_Toward;
    private static byte s_UpdatePositions;

    [UnmanagedCallersOnly]
    private static void EmitSoundLevel(void* self, void* filter, int ent, int channel, byte* sample, float volume,
                                       int level, int flags, int pitch, int dsp, void* origin, void* direction,
                                       void* origins, byte update, float time, int speaker)
    {
        s_Calls++;
        s_Filter = (nint)filter;
        (s_EntIndex, s_Channel, s_SoundLevel, s_Flags, s_Pitch, s_Dsp, s_Speaker) = (ent, channel, level, flags, pitch, dsp, speaker);
        (s_Volume, s_SoundTime, s_UpdatePositions) = (volume, time, update);
        s_Sample = Marshal.PtrToStringUTF8((nint)sample);
        s_OriginPointer = (nint)origin;
        s_Origin = origin == null ? default : *(Vector*)origin;
        (s_Direction, s_Origins) = ((nint)direction, (nint)origins);
    }

    [UnmanagedCallersOnly]
    private static void EmitSoundAttenuation(void* self, void* filter, int ent, int channel, byte* sample, float volume,
                                             float attenuation, int flags, int pitch, int dsp, void* origin,
                                             void* direction, void* origins, byte update, float time, int speaker)
    {
        s_Calls++;
        s_Filter = (nint)filter;
        (s_EntIndex, s_Channel, s_Flags, s_Pitch, s_Dsp, s_Speaker) = (ent, channel, flags, pitch, dsp, speaker);
        (s_Volume, s_Attenuation, s_SoundTime, s_UpdatePositions) = (volume, attenuation, time, update);
        s_Sample = Marshal.PtrToStringUTF8((nint)sample);
        s_Origin = origin == null ? default : *(Vector*)origin;
        s_Toward = direction == null ? default : *(Vector*)direction;
        (s_Direction, s_Origins) = ((nint)direction, (nint)origins);
    }

    [UnmanagedCallersOnly]
    private static void EmitAmbient(void* self, byte* sample, float volume, int pitch, int flags, float time)
    {
        s_Calls++;
        s_Sample = Marshal.PtrToStringUTF8((nint)sample);
        (s_Volume, s_Pitch, s_Flags, s_SoundTime) = (volume, pitch, flags, time);
    }

    private readonly List<nint> _frees = [];

    public ClientSoundInteropTests()
    {
        s_Filter = s_OriginPointer = s_Direction = s_Origins = -1;
        s_Sample = null;
        s_Calls = 0;
    }

    public void Dispose()
    {
        foreach (nint p in _frees)
            NativeMemory.Free((void*)p);
    }

    private T* Zeroed<T>() where T : unmanaged
    {
        var p = (T*)NativeMemory.AllocZeroed(1, (nuint)sizeof(T));
        _frees.Add((nint)p);
        return p;
    }

    private EngineSoundClient Sound()
    {
        EngineSoundFwd* fwd = Zeroed<EngineSoundFwd>();
        fwd->EmitSound_2 = &EmitSoundLevel;
        fwd->EmitSound = &EmitSoundAttenuation;
        fwd->EmitAmbientSound = &EmitAmbient;
        return new EngineSoundClient(Zeroed<long>(), fwd);
    }

    //-------------------------------------------------------------------------
    // IEngineSoundClient003's forwarders
    //-------------------------------------------------------------------------

    [Fact]
    public void TheSoundlevelEmitHandsTheFilterAndEveryArgumentThrough()
    {
        Sound().EmitSound(0x1234, 5, 6, "player/footstep1.wav", 0.5f, 80, 4, 110, 2, new Vector(1, 2, 3), 7.5f, 9);

        Assert.Equal((nint)0x1234, s_Filter);
        Assert.Equal((5, 6, 80, 4, 110, 2, 9), (s_EntIndex, s_Channel, s_SoundLevel, s_Flags, s_Pitch, s_Dsp, s_Speaker));
        Assert.Equal((0.5f, 7.5f, "player/footstep1.wav"), (s_Volume, s_SoundTime, s_Sample));
        Assert.Equal(new Vector(1, 2, 3), s_Origin);
        Assert.Equal(((nint)0, (nint)0, (byte)1), (s_Direction, s_Origins, s_UpdatePositions));
    }

    [Fact]
    public void TheAttenuationEmitCarriesTheDirectionAndTheHeadersDefaults()
    {
        Sound().EmitSoundAttenuation(0x99, 0, 6, "weapons/fx/nearmiss/bulletltor03.wav", 0.75f, 0.8f, 0, 105, 0,
                                     new Vector(4, 5, 6), new Vector(0, 1, 0));

        Assert.Equal(((nint)0x99, 0, 6, 0, 105, 0), (s_Filter, s_EntIndex, s_Channel, s_Flags, s_Pitch, s_Dsp));
        Assert.Equal((0.75f, 0.8f), (s_Volume, s_Attenuation));
        Assert.Equal((new Vector(4, 5, 6), new Vector(0, 1, 0)), (s_Origin, s_Toward));
        Assert.Equal(((nint)0, (byte)1, 0.0f, -1), (s_Origins, s_UpdatePositions, s_SoundTime, s_Speaker));
    }

    [Fact]
    public void TheAmbientEmitIsTheClientsFiveArguments()
    {
        Sound().EmitAmbientSound("ambient/hum.wav", 0.25f, 90, 128, 3.5f);

        Assert.Equal(("ambient/hum.wav", 0.25f, 90, 128, 3.5f), (s_Sample, s_Volume, s_Pitch, s_Flags, s_SoundTime));
    }

    [Fact]
    public void EveryEmitRefusesANullFilter()
    {
        Assert.Throws<ArgumentException>(() => Sound().EmitSound(0, 0, 0, "x.wav", 1f, 75, 0, 100, 0, null, 0f, -1));
        Assert.Throws<ArgumentException>(() => Sound().EmitSoundAttenuation(0, 0, 0, "x.wav", 1f, 0.8f, 0, 100, 0, null, null));
        Assert.Equal(0, s_Calls);
    }

    //-------------------------------------------------------------------------
    // ClientSoundEmitterEngine: the sink names the emit's recipients for the call
    //-------------------------------------------------------------------------

    private sealed class SinkReader(RecipientFilterSink sink) : IVtableEngineSoundClient
    {
        public List<string> Seen { get; } = [];

        private string Read(nint filter)
            => $"{filter}:{sink.GetRecipientCount()}:{sink.GetRecipientIndex(0)}:{sink.IsReliable()}";

        public void EmitSound(nint filter, int entIndex, int channel, string sample, float volume, int soundLevel,
                              int flags, int pitch, int specialDsp, Vector? origin, float soundTime, int speakerEntity)
            => Seen.Add("emit " + Read(filter) + " " + sample);

        public void EmitSoundAttenuation(nint filter, int entIndex, int channel, string sample, float volume,
                                         float attenuation, int flags, int pitch, int specialDsp, Vector? origin,
                                         Vector? direction)
            => Seen.Add("attn " + Read(filter) + " " + sample);

        public void StopSound(int entIndex, int channel, string sample) => Seen.Add($"stop {entIndex} {channel} {sample}");

        public float SoundDuration(string sample) => 1.5f;

        public void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime)
            => Seen.Add($"ambient {sample} {volume} {pitch} {flags} {soundTime}");
    }

    private sealed class Slots(int[] slots, bool reliable) : IRecipientList
    {
        public bool IsReliable => reliable;
        public bool IsInitMessage => false;
        public int Count => slots.Length;
        public int IndexAt(int slot) => slots[slot];
    }

    [Fact]
    public void AManagedEmitHandsTheNativeFilterWhileTheSinkHoldsItsRecipients()
    {
        var sink = new RecipientFilterSink();
        var sound = new SinkReader(sink);
        var engine = new ClientSoundEmitterEngine(null!, sound, null!, sink, 0x5000);

        engine.EmitSound(new Slots([1], true), 0, 4, "a.wav", 1f, 75, 0, 100, 0, null, 0f, -1);
        engine.EmitSoundAttenuation(new Slots([2], false), 0, 6, "b.wav", 1f, 0.8f, 0, 100, 0, null, null);

        Assert.Equal(["emit 20480:1:1:1 a.wav", "attn 20480:1:2:0 b.wav"], sound.Seen);
        Assert.Null(sink.Current);
    }

    [Fact]
    public void TheClientsAmbientEmitDropsTheEntityOriginAndLevel()
    {
        var sink = new RecipientFilterSink();
        var sound = new SinkReader(sink);
        var engine = new ClientSoundEmitterEngine(null!, sound, null!, sink, 0x5000);

        engine.EmitAmbientSound(7, new Vector(1, 2, 3), "ambient/hum.wav", 0.5f, 90, 2, 110, 4f);

        Assert.Equal(["ambient ambient/hum.wav 0.5 110 2 4"], sound.Seen);
    }

    //-------------------------------------------------------------------------
    // IManagedSoundEmitterClient: what native packed
    //-------------------------------------------------------------------------

    private sealed class Recorder : IClientSoundEmitterSink
    {
        public List<string> Calls { get; } = [];
        public float? Duration;
        public short WriteHandle = -1;

        public bool SystemInit()
        {
            Calls.Add("init");
            return true;
        }

        public void SystemShutdown() => Calls.Add("shutdown");

        public void LevelInitPreEntity() => Calls.Add("levelinit");

        public void LevelShutdownPostEntity() => Calls.Add("levelshutdown");

        public float? Emit(ReadOnlySpan<int> slots, bool reliable, bool initMessage, int entIndex,
                           in ClientSoundEmitRequest request, ref short handle)
        {
            Calls.Add($"emit [{string.Join(",", slots.ToArray())}] {reliable} {initMessage} {entIndex} {request} h{handle}");
            if (WriteHandle != -1)
                handle = WriteHandle;
            return Duration;
        }

        public void Stop(int entIndex, int channel, string? sample) => Calls.Add($"stop {entIndex} {channel} {sample ?? "null"}");

        public void EmitCloseCaption(ReadOnlySpan<int> slots, bool reliable, bool initMessage, int entIndex, string token,
                                     float duration, bool warnIfMissing)
            => Calls.Add($"caption [{string.Join(",", slots.ToArray())}] {reliable} {entIndex} {token} {duration} {warnIfMissing}");
    }

    private static byte* Utf8(string text) => (byte*)Marshal.StringToCoTaskMemUTF8(text);

    [Fact]
    public void AnEmitDecodesTheRecipientsTheFieldsAndTheOptions()
    {
        var recorder = new Recorder();
        var crossings = new Crossings();
        IManagedSoundEmitterClient binding = new SoundEmitterClientBinding(crossings, recorder);
        int* recipients = stackalloc int[] { 2, 1, 1, 3 };
        float* origin = stackalloc float[] { 1, 2, 3 };
        int handle = 17;
        byte* name = Utf8("Weapon_Pistol.Single");
        try
        {
            binding.ClientSoundEmit(recipients, 4, 1, name, 0.5f, 80, 2, 105, 6, origin, 9f, null,
                                    SoundEmitterClientBinding.OptionEmitCloseCaption | SoundEmitterClientBinding.OptionWarnOnDirectWaveReference
                                    | SoundEmitterClientBinding.OptionByHandle, 7, &handle);
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)name);
        }

        var expected = new ClientSoundEmitRequest(1, "Weapon_Pistol.Single", 0.5f, 80, 2, 105, 6, new Vector(1, 2, 3), 9f,
                                                  WantDuration: false, EmitCloseCaption: true, WarnOnMissingCloseCaption: false,
                                                  WarnOnDirectWaveReference: true, SpeakerEntity: 7, ByHandle: true);
        Assert.Equal([$"emit [1,3] True False 4 {expected} h17"], recorder.Calls);
        Assert.Equal(1L, crossings.Read(CrossingOp.ClientSoundEmit));
    }

    [Fact]
    public void TheHandleAndTheDurationAreWrittenBackOnlyWhenTheEmitAnswersThem()
    {
        var recorder = new Recorder { WriteHandle = 6, Duration = 2.5f };
        IManagedSoundEmitterClient binding = new SoundEmitterClientBinding(new Crossings(), recorder);
        int* recipients = stackalloc int[] { 0, 0 };
        int handle = -1;
        float duration = -1f;

        binding.ClientSoundEmit(recipients, 0, 0, null, 1f, 75, 0, 100, 0, null, 0f, &duration, 0, -1, &handle);
        Assert.Equal((6, 2.5f), (handle, duration));
        Assert.Contains("WantDuration = True", recorder.Calls[0]);

        recorder.Duration = null;
        duration = -1f;
        binding.ClientSoundEmit(recipients, 0, 0, null, 1f, 75, 0, 100, 0, null, 0f, &duration, 0, -1, &handle);
        Assert.Equal(-1f, duration);
    }

    [Fact]
    public void ARecipientBufferNativeCouldNotHaveBuiltIsNoEmit()
    {
        var recorder = new Recorder();
        IManagedSoundEmitterClient binding = new SoundEmitterClientBinding(new Crossings(), recorder);
        int* tooMany = stackalloc int[] { SoundEmitterClientBinding.MaxRecipients + 1, 0 };
        int* negative = stackalloc int[] { -1, 0 };
        int* none = stackalloc int[] { 0, 2 };

        binding.ClientSoundEmit(tooMany, 0, 0, null, 1f, 75, 0, 100, 0, null, 0f, null, 0, -1, null);
        binding.ClientSoundEmit(negative, 0, 0, null, 1f, 75, 0, 100, 0, null, 0f, null, 0, -1, null);
        binding.ClientSoundEmit(null, 0, 0, null, 1f, 75, 0, 100, 0, null, 0f, null, 0, -1, null);
        Assert.Empty(recorder.Calls);

        // The positive arm: an empty filter is an emit (stock emits to nobody).
        binding.ClientSoundEmit(none, 0, 0, null, 1f, 75, 0, 100, 0, null, 0f, null, 0, -1, null);
        Assert.Equal(["emit [] False True 0 " + new ClientSoundEmitRequest(0, null, 1f, 75, 0, 100, 0, null, 0f, false, false, false, false, -1, false) + " h-1"],
                     recorder.Calls);
    }

    [Fact]
    public void TheStagesStopAndCaptionReachTheSinkEachCountingItsCrossing()
    {
        var recorder = new Recorder();
        var crossings = new Crossings();
        IManagedSoundEmitterClient binding = new SoundEmitterClientBinding(crossings, recorder);
        int* recipients = stackalloc int[] { 1, 1, 2 };
        byte* sample = Utf8("vo/line.wav");
        try
        {
            Assert.Equal((byte)1, binding.ClientSoundSystemInit());
            binding.ClientSoundLevelInitPreEntity();
            binding.ClientSoundStop(3, 6, sample);
            binding.ClientSoundStop(3, 6, null);
            binding.ClientSoundEmitCloseCaption(recipients, 3, sample, 1.5f, 1);
            binding.ClientSoundEmitCloseCaption(recipients, 3, null, 1.5f, 1);
            binding.ClientSoundLevelShutdownPostEntity();
            binding.ClientSoundSystemShutdown();
        }
        finally
        {
            Marshal.FreeCoTaskMem((nint)sample);
        }

        Assert.Equal(["init", "levelinit", "stop 3 6 vo/line.wav", "stop 3 6 null", "caption [2] True 3 vo/line.wav 1.5 True",
                      "levelshutdown", "shutdown"], recorder.Calls);
        Assert.Equal((1L, 1L, 2L, 2L, 1L, 1L), (crossings.Read(CrossingOp.ClientSoundSystemInit), crossings.Read(CrossingOp.ClientSoundLevelInitPreEntity),
                                           crossings.Read(CrossingOp.ClientSoundStop), crossings.Read(CrossingOp.ClientSoundEmitCloseCaption),
                                           crossings.Read(CrossingOp.ClientSoundLevelShutdownPostEntity), crossings.Read(CrossingOp.ClientSoundSystemShutdown)));
    }
}
