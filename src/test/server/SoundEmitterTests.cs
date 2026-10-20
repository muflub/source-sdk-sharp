using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Server.Interop;
using SourceSharp.Shared;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The managed SoundEmitter against stock's CSoundEmitterSystem emit path
/// (SoundEmitterSystem.cpp) and sound_emitter.cpp's server arm, which it
/// replaces (plan_server_native_v2 T4a). The engine is a recorder; each fact
/// names the stock rule it holds.
/// </summary>
public sealed class SoundEmitterTests
{
    //-------------------------------------------------------------------------
    // The fakes
    //-------------------------------------------------------------------------

    internal sealed record Emitted(string Recipients, bool Reliable, int EntIndex, int Channel, string Sample,
                                   float Volume, int SoundLevel, int Flags, int Pitch, int Dsp, Vector? Origin,
                                   float SoundTime, int Speaker);

    internal sealed record Ambient(int EntIndex, Vector Origin, string Sample, float Volume, int SoundLevel,
                                   int Flags, int Pitch, float SoundTime);

    internal sealed class Script
    {
        public int Index;
        public int Channel = 0;
        public float Volume = 1.0f;
        public int SoundLevel = 75;
        public int Pitch = 100;
        public int DelayMsec;
        public string Wave = "";
        public string[] Waves = [];
        public Func<int, string>? WaveForGender;
    }

    internal sealed class FakeEngine : IServerSoundEmitterEngine
    {
        public readonly Dictionary<string, Script> Scripts = new(StringComparer.Ordinal);
        public readonly List<Emitted> Emits = [];
        public readonly List<(int EntIndex, int Channel, string Sample)> Stops = [];
        public readonly List<Ambient> Ambients = [];
        public readonly List<string> Asked = [];
        public readonly Dictionary<string, int> Sentences = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> WavFiles = new(StringComparer.Ordinal);
        public Func<string, float> Duration = _ => 1.5f;
        public uint[] PasBits = new uint[IFwdEngineServer.PlayerBitWords];
        public int GenderOf(string? model) => model?.Contains("female", StringComparison.Ordinal) == true ? 2 : 1;
        public int LastGender = -1;
        public short LastHandleIn;

        private Script? ByIndex(int index) => Scripts.Values.FirstOrDefault(s => s.Index == index);

        public int SoundIndex(string name)
        {
            Asked.Add("SoundIndex " + name);
            return Scripts.TryGetValue(name, out var s) ? s.Index : -1;
        }

        public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender)
        {
            Asked.Add("Parameters " + name);
            LastGender = gender;
            if (!Scripts.TryGetValue(name, out var s))
                return false;
            Fill(s, ref parameters, gender);
            return true;
        }

        public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters,
                                         int gender, bool beingEmitted)
        {
            Asked.Add("ParametersEx " + name);
            LastHandleIn = handle;
            LastGender = gender;
            Script? s = handle != -1 ? ByIndex(handle) : Scripts.GetValueOrDefault(name);
            if (s is null)
                return false;
            handle = (short)s.Index;
            Fill(s, ref parameters, gender);
            return true;
        }

        private static void Fill(Script s, ref SoundParametersLayout parameters, int gender)
        {
            parameters.Channel = s.Channel;
            parameters.Volume = s.Volume;
            parameters.SoundLevel = s.SoundLevel;
            parameters.Pitch = s.Pitch;
            parameters.DelayMsec = s.DelayMsec;
            parameters.Name = s.WaveForGender?.Invoke(gender) ?? s.Wave;
        }

        public SoundScriptEntry? Entry(int index)
            => ByIndex(index) is { } s ? new SoundScriptEntry(s.Channel, s.Waves) : null;

        public string? WavFileForSound(string name, int gender) => WavFiles.GetValueOrDefault(name);

        public int LookupSoundLevel(string name) => Scripts.TryGetValue(name, out var s) ? s.SoundLevel : 0;

        public int ActorGender(string? model)
        {
            Asked.Add("ActorGender " + (model ?? "(null)"));
            return GenderOf(model);
        }

        public void EmitSound(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                              int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime,
                              int speakerEntity)
        {
            string slots = string.Join(",", Enumerable.Range(0, recipients.Count).Select(recipients.IndexAt));
            Emits.Add(new Emitted(slots, recipients.IsReliable, entIndex, channel, sample, volume, soundLevel, flags,
                                  pitch, specialDsp, origin, soundTime, speakerEntity));
        }

        public void StopSound(int entIndex, int channel, string sample) => Stops.Add((entIndex, channel, sample));

        public float SoundDuration(string sample) => Duration(sample);

        public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel,
                                     int flags, int pitch, float soundTime)
            => Ambients.Add(new Ambient(entIndex, origin, sample, volume, soundLevel, flags, pitch, soundTime));

        public int SentenceIndexFromName(string name) => Sentences.TryGetValue(name, out int i) ? i : -1;

        public void MulticastRecipients(bool usePas, Vector origin, Span<uint> playerBits)
        {
            Asked.Add($"Multicast {usePas}");
            PasBits.CopyTo(playerBits);
        }
    }

    internal sealed class Listeners : ISoundListeners
    {
        public int MaxClients { get; set; } = 8;
        public readonly Dictionary<int, Vector> Ears = [];
        public readonly Dictionary<int, string?> Captions = [];

        public bool IsConnected(int playerIndex) => Ears.ContainsKey(playerIndex);

        public bool TryEars(int playerIndex, out Vector ears) => Ears.TryGetValue(playerIndex, out ears);

        public string? CloseCaptionSetting(int playerIndex) => Captions.GetValueOrDefault(playerIndex);
    }

    internal sealed class Captions : ICloseCaptions
    {
        public bool Wanted { get; set; }
        public readonly List<(int[] Recipients, bool Reliable, string Token, float Duration, bool FromPlayer,
                              bool WarnIfMissing, int Gender)> Sent = [];

        public int Emit(ReadOnlySpan<int> recipients, bool reliable, string token, float duration, bool fromPlayer,
                        bool warnIfMissing, int gender)
        {
            Sent.Add((recipients.ToArray(), reliable, token, duration, fromPlayer, warnIfMissing, gender));
            return recipients.Length;
        }
    }

    private sealed class Clock : IGameClock
    {
        public float Now { get; set; } = 10.0f;
    }

    private readonly FakeEngine _engine = new();
    private readonly Listeners _listeners = new();
    private readonly Captions _captions = new();
    private readonly Clock _clock = new();
    private readonly Dictionary<int, string> _models = [];
    private readonly List<string> _msgs = [];
    private readonly List<string> _devMsgs = [];
    private bool _trace;

    private SoundEmitter Emitter()
        => new(_engine, new SoundRecipients(_engine, _listeners), _captions, _clock,
               i => _models.GetValueOrDefault(i), () => _trace, _msgs.Add, _devMsgs.Add);

    private RecipientFilter Filter(params int[] slots)
    {
        var filter = new RecipientFilter(_listeners.MaxClients);
        foreach (int slot in slots)
            filter.Add(slot);
        return filter;
    }

    private Script Door()
    {
        var s = new Script { Index = 7, Channel = 4, Volume = 0.8f, SoundLevel = 80, Pitch = 95, Wave = "doors/move1.wav" };
        _engine.Scripts["Doors.Move1"] = s;
        return s;
    }

    //-------------------------------------------------------------------------
    // EmitSound: raw waves
    //-------------------------------------------------------------------------

    [Fact]
    public void ARawWaveGoesToTheEngineWithTheCallersParameters()
    {
        var ep = new EmitSoundParams
        {
            Channel = 2, SoundName = "ambient/alarm.wav", Volume = 0.5f, SoundLevel = 90, Flags = 0, Pitch = 120,
            SpecialDsp = 3, Origin = new Vector(1, 2, 3), SoundTime = 4f, SpeakerEntity = 6,
        };

        Emitter().Emit(Filter(1, 2), 5, ep);

        Assert.Equal(new Emitted("1,2", false, 5, 2, "ambient/alarm.wav", 0.5f, 90, 0, 120, 3, new Vector(1, 2, 3), 4f, 6),
                     Assert.Single(_engine.Emits));
        Assert.DoesNotContain(_engine.Asked, a => a.StartsWith("SoundIndex", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("music/theme.MP3")]
    [InlineData("AMBIENT/X.WAV")]
    [InlineData("!HG_ALERT1")]
    public void AnMp3AWavInAnyCaseAndASentenceAreRawToo(string name)
    {
        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = name });

        Assert.Equal(name, Assert.Single(_engine.Emits).Sample);
    }

    [Fact]
    public void AWaveNamedDirectlyIsReportedOnceOnTheDeveloperConsole()
    {
        var emitter = Emitter();

        emitter.Emit(Filter(1), 5, new EmitSoundParams { SoundName = "a/b.wav", WarnOnDirectWaveReference = true });
        emitter.Emit(Filter(1), 5, new EmitSoundParams { SoundName = "a/b.wav", WarnOnDirectWaveReference = true });

        Assert.Equal(["Emitsound directly referenced wave a/b.wav (should use game_sounds.txt system instead)\n"], _devMsgs);
    }

    [Fact]
    public void AnMp3NamedDirectlyIsNotReported()
    {
        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "a/b.mp3", WarnOnDirectWaveReference = true });

        Assert.Empty(_devMsgs);
    }

    [Fact]
    public void ARawWavesDurationIsWrittenWhenAsked()
    {
        _engine.Duration = s => s == "a/b.wav" ? 3.25f : 0f;
        var ep = new EmitSoundParams { SoundName = "a/b.wav", WantDuration = true };

        Emitter().Emit(Filter(1), 5, ep);

        Assert.Equal(3.25f, ep.Duration);
    }

    //-------------------------------------------------------------------------
    // EmitSound: scripts
    //-------------------------------------------------------------------------

    [Fact]
    public void AScriptIsLookedUpOnceAndItsHandleKeptInTheParameters()
    {
        Door();
        var emitter = Emitter();
        var ep = new EmitSoundParams { SoundName = "Doors.Move1" };

        emitter.Emit(Filter(1), 5, ep);
        emitter.Emit(Filter(1), 5, ep);

        Assert.Equal((short)7, ep.ScriptHandle);
        Assert.Single(_engine.Asked, a => a == "SoundIndex Doors.Move1");
        Assert.Equal(2, _engine.Emits.Count);
    }

    [Fact]
    public void AScriptTheTableDoesNotHavePlaysNothing()
    {
        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "No.Such" });

        Assert.Empty(_engine.Emits);
    }

    [Fact]
    public void AScriptPlaysItsOwnChannelVolumeLevelPitchAndWaveWithTheCallersFlags()
    {
        Door();

        Emitter().Emit(Filter(1, 3), 5, new EmitSoundParams
        {
            SoundName = "Doors.Move1", Channel = 1, Volume = 0.1f, SoundLevel = 20, Pitch = 50, Flags = 1 << 5,
            SpecialDsp = 2, Origin = new Vector(7, 8, 9), SpeakerEntity = 4,
        });

        Assert.Equal(new Emitted("1,3", false, 5, 4, "doors/move1.wav", 0.8f, 80, 1 << 5, 95, 2, new Vector(7, 8, 9), 0f, 4),
                     Assert.Single(_engine.Emits));
    }

    [Fact]
    public void ChangePitchAndChangeVolumeTakeTheCallersNumbers()
    {
        Door();

        Emitter().Emit(Filter(1), 5, new EmitSoundParams
        {
            SoundName = "Doors.Move1", Volume = 0.25f, Pitch = 140, Flags = SoundEmitter.SndChangePitch | SoundEmitter.SndChangeVol,
        });

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal((0.25f, 140), (emit.Volume, emit.Pitch));
    }

    [Fact]
    public void TheScriptsDelayBecomesAStartTimeWhenTheCallerGaveNone()
    {
        Door().DelayMsec = 250;
        _clock.Now = 10.0f;

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Equal(10.25f, Assert.Single(_engine.Emits).SoundTime);
    }

    [Fact]
    public void TheCallersStartTimeWinsOverTheScriptsDelay()
    {
        Door().DelayMsec = 250;

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1", SoundTime = 3.0f });

        Assert.Equal(3.0f, Assert.Single(_engine.Emits).SoundTime);
    }

    [Fact]
    public void TheSpeakersGenderComesFromTheModelOfTheEntityAtTheIndex()
    {
        Door().WaveForGender = g => g == 2 ? "vo/female.wav" : "vo/male.wav";
        _models[5] = "models/humans/female_01.mdl";

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Contains("ActorGender models/humans/female_01.mdl", _engine.Asked);
        Assert.Equal(2, _engine.LastGender);
        Assert.Equal("vo/female.wav", Assert.Single(_engine.Emits).Sample);
    }

    [Fact]
    public void AnIndexWithNoEntityHasNoGenderAndAsksForNone()
    {
        Door();

        Emitter().Emit(Filter(1), 9, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Equal(0, _engine.LastGender);
        Assert.DoesNotContain(_engine.Asked, a => a.StartsWith("ActorGender", StringComparison.Ordinal));
    }

    [Fact]
    public void AScriptWithAnEmptyWavePlaysNothing()
    {
        Door().Wave = "";

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Empty(_engine.Emits);
    }

    [Fact]
    public void AScriptsDurationIsItsWavesWhenAsked()
    {
        Door();
        _engine.Duration = s => s == "doors/move1.wav" ? 2.5f : 0f;
        var ep = new EmitSoundParams { SoundName = "Doors.Move1", WantDuration = true };

        Emitter().Emit(Filter(1), 5, ep);

        Assert.Equal(2.5f, ep.Duration);
    }

    [Fact]
    public void ANamedEmitGoesStraightToTheHandleSoARawWaveNamedThatWayPlaysNothing()
    {
        // Stock's CBaseEntity::EmitSound( filter, index, soundname, ... ) calls
        // the handle overload, so the raw-wave branch of EmitSound is not taken.
        Emitter().Emit(Filter(1), 5, "a/b.wav");

        Assert.Contains("ParametersEx a/b.wav", _engine.Asked);
        Assert.Empty(_engine.Emits);
    }

    [Fact]
    public void ANamedEmitPlaysAScriptFromItsOrigin()
    {
        Door();

        Emitter().Emit(Filter(2), 5, "Doors.Move1", new Vector(1, 1, 1));

        var emit = Assert.Single(_engine.Emits);
        Assert.Equal(("doors/move1.wav", (Vector?)new Vector(1, 1, 1)), (emit.Sample, emit.Origin));
        Assert.Equal((short)-1, _engine.LastHandleIn);
    }

    //-------------------------------------------------------------------------
    // The trace
    //-------------------------------------------------------------------------

    [Fact]
    public void TheTraceLineNamesTheScriptTheWaveTheEntityAndTheRecipients()
    {
        Door();
        _trace = true;

        Emitter().Emit(Filter(1, 2, 3), 5, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Equal(["(sv) EmitSound:  'Doors.Move1' emitted as 'doors/move1.wav' (ent 5, 3 recipients)\n"], _msgs);
    }

    [Fact]
    public void NothingIsTracedWithTheTraceOff()
    {
        Door();

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Empty(_msgs);
    }

    //-------------------------------------------------------------------------
    // Close captions
    //-------------------------------------------------------------------------

    [Fact]
    public void AScriptsCaptionGoesToTheRecipientsWhoDidNotTurnCaptionsOff()
    {
        Door();
        _captions.Wanted = true;
        _listeners.Captions[1] = "1";
        _listeners.Captions[2] = "0";
        _listeners.Captions[3] = "";   // not reported yet: kept
        _engine.Duration = _ => 1.75f;
        RecipientFilter filter = Filter(1, 2, 3);

        Emitter().Emit(filter, 5, new EmitSoundParams { SoundName = "Doors.Move1", WarnOnMissingCloseCaption = true });

        var sent = Assert.Single(_captions.Sent);
        Assert.Equal([1, 3], sent.Recipients);
        Assert.Equal(("Doors.Move1", 1.75f, false, true), (sent.Token, sent.Duration, sent.FromPlayer, sent.WarnIfMissing));
        Assert.Equal([1, 2, 3], filter.Slots);   // the caller's filter is untouched
    }

    [Fact]
    public void NoCaptionIsSentForAChangeOfPitchOrVolume()
    {
        Door();
        _captions.Wanted = true;

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1", Flags = SoundEmitter.SndChangeVol });

        Assert.Empty(_captions.Sent);
    }

    [Fact]
    public void NoCaptionIsSentWhenCaptionsAreNotWantedOrTheEmitDeclinesOne()
    {
        Door();
        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1" });
        _captions.Wanted = true;
        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1", EmitCloseCaption = false });

        Assert.Empty(_captions.Sent);
    }

    [Fact]
    public void NoCaptionIsSentWhenEveryRecipientTurnedThemOff()
    {
        Door();
        _captions.Wanted = true;
        _listeners.Captions[1] = "0";

        Emitter().Emit(Filter(1), 5, new EmitSoundParams { SoundName = "Doors.Move1" });

        Assert.Empty(_captions.Sent);
    }

    [Fact]
    public void ACaptionsNegativeDurationIsItsWavesOrTwoSeconds()
    {
        _captions.Wanted = true;
        _engine.WavFiles["Has.Wave"] = "x.wav";
        _engine.Duration = s => s == "x.wav" ? 4f : 0f;
        var emitter = Emitter();

        emitter.EmitCloseCaption(Filter(1), 5, "Has.Wave", -1f, false);
        emitter.EmitCloseCaption(Filter(1), 5, "No.Wave", -1f, false);

        Assert.Equal([4f, 2f], _captions.Sent.Select(s => s.Duration));
    }

    [Fact]
    public void ACaptionCarriesTheSpeakersGender()
    {
        _captions.Wanted = true;
        _models[5] = "models/female.mdl";

        Emitter().EmitCloseCaption(Filter(1), 5, "T", 1f, false);

        Assert.Equal(2, Assert.Single(_captions.Sent).Gender);
    }

    //-------------------------------------------------------------------------
    // StopSound
    //-------------------------------------------------------------------------

    [Fact]
    public void ARawWaveIsStoppedAsNamedOnTheCallersChannel()
    {
        Emitter().Stop(5, 3, "a/loop.wav");

        Assert.Equal((5, 3, "a/loop.wav"), Assert.Single(_engine.Stops));
    }

    [Fact]
    public void AScriptStopsEveryWaveOfItsEntryOnTheEntrysChannel()
    {
        Door().Waves = ["doors/move1.wav", "doors/move2.wav"];

        Emitter().Stop(5, 1, "Doors.Move1");

        Assert.Equal([(5, 4, "doors/move1.wav"), (5, 4, "doors/move2.wav")], _engine.Stops);
    }

    [Fact]
    public void AScriptTheTableDoesNotHaveStopsNothing()
    {
        Emitter().Stop(5, "No.Such");

        Assert.Empty(_engine.Stops);
    }

    [Fact]
    public void AStopIsTracedPerWave()
    {
        Door().Waves = ["doors/move1.wav"];
        _trace = true;

        Emitter().Stop(5, "Doors.Move1");

        Assert.Equal(["(sv) StopSound:  'Doors.Move1' stopped as 'doors/move1.wav' (ent 5)\n"], _msgs);
    }

    //-------------------------------------------------------------------------
    // Ambient sounds
    //-------------------------------------------------------------------------

    [Fact]
    public void ASentenceAmbientGoesByTheSentencesIndex()
    {
        _engine.Sentences["HG_ALERT1"] = 12;

        Emitter().UtilEmitAmbient(5, new Vector(1, 2, 3), "!HG_ALERT1", 0.5f, 80, 0, 100);

        Assert.Equal(new Ambient(5, new Vector(1, 2, 3), "!12", 0.5f, 80, 0, 100, 0f), Assert.Single(_engine.Ambients));
    }

    [Fact]
    public void AnUnknownSentencePlaysNothing()
    {
        Emitter().UtilEmitAmbient(5, default, "!NOPE", 0.5f, 80, 0, 100);

        Assert.Empty(_engine.Ambients);
    }

    [Fact]
    public void ARawAmbientWavePlaysAtTheCallersLevelAndAnswersItsDuration()
    {
        _engine.Duration = _ => 6f;

        float duration = Emitter().UtilEmitAmbient(5, new Vector(1, 2, 3), "ambient/fan.wav", 0.4f, 60, 0, 90);

        Assert.Equal(new Ambient(5, new Vector(1, 2, 3), "ambient/fan.wav", 0.4f, 60, 0, 90, 0f), Assert.Single(_engine.Ambients));
        Assert.Equal(6f, duration);
    }

    [Fact]
    public void AScriptAmbientPlaysTheScriptsWaveVolumeLevelAndPitch()
    {
        Door();

        Emitter().UtilEmitAmbient(5, new Vector(1, 2, 3), "Doors.Move1", 0.1f, 20, 0, 50);

        Assert.Equal(new Ambient(5, new Vector(1, 2, 3), "doors/move1.wav", 0.8f, 80, 0, 95, 0f), Assert.Single(_engine.Ambients));
        Assert.Equal(0, _engine.LastGender);
    }

    [Fact]
    public void AScriptAmbientTakesTheCallersPitchAndVolumeWhenItsFlagsSaySo()
    {
        Door();

        Emitter().UtilEmitAmbient(5, default, "Doors.Move1", 0.1f, 20, SoundEmitter.SndChangePitch | SoundEmitter.SndChangeVol, 50);

        var ambient = Assert.Single(_engine.Ambients);
        Assert.Equal((0.1f, 50), (ambient.Volume, ambient.Pitch));
    }

    [Fact]
    public void AScriptAmbientsStartIsCaptionedToEveryPlayerReliably()
    {
        Door();
        _captions.Wanted = true;
        _listeners.Ears[1] = default;
        _listeners.Ears[4] = default;
        _engine.Duration = _ => 3f;

        Emitter().UtilEmitAmbient(5, default, "Doors.Move1", 1f, 75, 0, 100);

        var sent = Assert.Single(_captions.Sent);
        Assert.Equal([1, 4], sent.Recipients);
        Assert.Equal((true, "Doors.Move1", 3f, false), (sent.Reliable, sent.Token, sent.Duration, sent.WarnIfMissing));
    }

    [Theory]
    [InlineData(SoundEmitter.SndStop)]
    [InlineData(SoundEmitter.SndChangeVol)]
    [InlineData(SoundEmitter.SndChangePitch)]
    public void AnAmbientsStopOrChangeIsNotCaptioned(int flags)
    {
        Door();
        _captions.Wanted = true;
        _listeners.Ears[1] = default;

        Emitter().UtilEmitAmbient(5, default, "Doors.Move1", 1f, 75, flags, 100);

        Assert.Empty(_captions.Sent);
    }

    [Fact]
    public void AnAmbientScriptTheTableDoesNotHavePlaysNothing()
    {
        Emitter().UtilEmitAmbient(5, default, "No.Such", 1f, 75, 0, 100);

        Assert.Empty(_engine.Ambients);
    }

    //-------------------------------------------------------------------------
    // GameSound_GetParameters
    //-------------------------------------------------------------------------

    [Fact]
    public void AScriptsParametersAreTheNullActorsAndNullForNoSuchScript()
    {
        Door();
        var emitter = Emitter();

        var parameters = emitter.Parameters("Doors.Move1");

        Assert.NotNull(parameters);
        Assert.Equal(("doors/move1.wav", 4, 80), (parameters.Value.Name, parameters.Value.Channel, parameters.Value.SoundLevel));
        Assert.Contains("ActorGender (null)", _engine.Asked);
        Assert.Null(emitter.Parameters("No.Such"));
    }
}
