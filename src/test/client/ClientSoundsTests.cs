using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Sounds;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's sound callers over the managed emitter (<see cref="ClientSounds"/>):
/// each one the call sound_emitter.cpp's caller made -- the shim executors'
/// (ClientEmitSound, PhysicsClientEmitImpactSound, FxSoundParameters /
/// FxEmitWorldSound, FxEmitTracerSound, the soundscape's three) and native's
/// last GameSound_* callers' -- recorded at the engine.
/// </summary>
public class ClientSoundsTests
{
    private sealed class Engine : ISoundEmitterEngine, IClientEngineSound
    {
        public readonly List<string> Calls = [];
        public readonly Dictionary<string, SoundParametersLayout> Scripts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, float> Durations = new(StringComparer.Ordinal);

        private static string F(float f) => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        private static string V(Vector? v) => v is { } at ? $"{F(at.X)},{F(at.Y)},{F(at.Z)}" : "";

        private static string Filter(IRecipientList r)
        {
            var slots = new List<string>();
            for (int i = 0; i < r.Count; i++)
                slots.Add(r.IndexAt(i).ToString(System.Globalization.CultureInfo.InvariantCulture));
            return (slots.Count == 0 ? "-" : string.Join(",", slots)) + (r.IsReliable ? "/1" : "/0");
        }

        public int SoundIndex(string name) => Scripts.ContainsKey(name) ? 1 : -1;

        public bool ParametersForSound(string name, ref SoundParametersLayout parameters, int gender)
        {
            if (!Scripts.TryGetValue(name, out SoundParametersLayout p))
                return false;
            parameters = p;
            return true;
        }

        public bool ParametersForSoundEx(string name, ref short handle, ref SoundParametersLayout parameters, int gender,
                                         bool beingEmitted)
        {
            Calls.Add($"paramsex {name} h{handle}");
            if (!Scripts.TryGetValue(name, out SoundParametersLayout p))
                return false;
            handle = 1;
            parameters = p;
            return true;
        }

        public SoundScriptEntry? Entry(int index) => new(6, ["a.wav", "b.wav"]);

        public string? WavFileForSound(string name, int gender) => null;

        public int LookupSoundLevel(string name) => 75;

        public int ActorGender(string? model) => 0;

        public void EmitSound(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                              int soundLevel, int flags, int pitch, int specialDsp, Vector? origin, float soundTime,
                              int speakerEntity)
            => Calls.Add($"emit {Filter(recipients)} ent{entIndex} ch{channel} {sample} v{F(volume)} l{soundLevel} f{flags} p{pitch} at{V(origin)}");

        public void StopSound(int entIndex, int channel, string sample) => Calls.Add($"stop ent{entIndex} ch{channel} {sample}");

        public float SoundDuration(string sample)
        {
            Calls.Add($"duration {sample}");
            return Durations.TryGetValue(sample, out float d) ? d : 0f;
        }

        public void EmitAmbientSound(int entIndex, Vector origin, string sample, float volume, int soundLevel, int flags,
                                     int pitch, float soundTime)
            => Calls.Add($"ambient {sample} v{F(volume)} p{pitch} f{flags}");

        public int SentenceIndexFromName(string name) => -1;

        public void EmitAmbientSound(string sample, float volume, int pitch, int flags, float soundTime = 0.0f)
            => Calls.Add($"engine-ambient {sample} v{F(volume)} p{pitch} f{flags}");

        public void EmitSoundAttenuation(IRecipientList recipients, int entIndex, int channel, string sample, float volume,
                                         float attenuation, int flags, int pitch, int specialDsp, Vector? origin,
                                         Vector? direction)
            => Calls.Add($"attn {Filter(recipients)} ent{entIndex} ch{channel} {sample} v{F(volume)} a{F(attenuation)} p{pitch} at{V(origin)} dir{V(direction)}");
    }

    private sealed class Context : IClientSoundContext
    {
        public int MaxClients => 16;
        public bool CloseCaptionOn => false;
        public int LocalPlayerSlot { get; set; } = 2;
        public bool TraceOn => false;
        public string? SpeakerModel(int entIndex) => null;
        public bool FromPlayer(int entIndex) => false;
    }

    private sealed class Clock : IGameClock
    {
        public float Now => 10f;
    }

    private sealed class SystemEngine : IClientSoundSystemEngine
    {
        public readonly List<string> Calls = [];
        public bool ModInit() { Calls.Add("modinit"); return true; }
        public void ModShutdown() => Calls.Add("modshutdown");
        public string LevelName() => "maps/dm_test.bsp";
        public bool FileExists(string name, string pathId) { Calls.Add("exists " + name); return false; }
        public void AddSoundOverrides(string scriptFile, bool preload) => Calls.Add("overrides " + scriptFile);
        public void ClearSoundOverrides() => Calls.Add("clear");
    }

    private static SoundParametersLayout Script(string wave, float volume, int level, int pitch, int channel)
    {
        var p = SoundParametersLayout.Stock();
        p.Name = wave;
        p.Volume = volume;
        p.SoundLevel = level;
        p.Pitch = pitch;
        p.PitchLow = pitch - 8;
        p.PitchHigh = pitch + 8;
        p.Channel = channel;
        return p;
    }

    private static (ClientSounds Sounds, Engine Engine, SystemEngine System, RecordingEngine Entities, Context Context) Rig(bool bind = true)
    {
        var engine = new Engine();
        engine.Scripts["Physics.Impact"] = Script("physics/impact1.wav", 0.5f, 70, 98, 4);
        engine.Scripts["Bullets.Whiz"] = Script("weapons/fx/nearmiss/bulletltor03.wav", 0.8f, 75, 102, 0);
        var entities = new RecordingEngine { Strict = false };
        var context = new Context();
        var system = new SystemEngine();
        var sounds = new ClientSounds(entities);
        if (bind)
            sounds.Bind(new ClientSoundEmitter(engine, new Clock(), context, new NoCloseCaptionHud(), _ => { }, _ => { }),
                        new ClientSoundEmitterSystem(system), engine);
        return (sounds, engine, system, entities, context);
    }

    [Fact]
    public void AnEntitysSoundIsEmittedFromItsIndexToTheViewerByScript()
    {
        var (sounds, engine, _, entities, _) = Rig();
        engine.Scripts["Weapon.Fire"] = Script("weapons/fire.wav", 1f, 80, 100, 1);
        entities.Returns("Entities.GetIndex", 7);

        sounds.EmitFrom(new EntityHandle(0x0001_0007u), "Weapon.Fire", new Vector(1, 2, 3));

        Assert.Equal(["paramsex Weapon.Fire h-1", "emit 2/0 ent7 ch1 weapons/fire.wav v1 l80 f0 p100 at1,2,3"], engine.Calls);
    }

    [Fact]
    public void AHandleThatNamesNothingSoundsFromTheWorld()
    {
        var (sounds, engine, _, entities, _) = Rig();
        engine.Scripts["Weapon.Fire"] = Script("weapons/fire.wav", 1f, 80, 100, 1);
        entities.Returns("Entities.GetIndex", -1);

        sounds.EmitFrom(EntityHandle.None, "Weapon.Fire");

        Assert.Contains("emit 2/0 ent0 ch1 weapons/fire.wav v1 l80 f0 p100 at", engine.Calls);
    }

    [Fact]
    public void AnImpactPlaysTheScriptsWaveAtItsVolumeTimesTheImpactsFromTheWorld()
    {
        var (sounds, engine, _, _, _) = Rig();

        Assert.True(sounds.EmitImpactSound("Physics.Impact", new Vector(4, 5, 6), 3, 0.5f));
        Assert.False(sounds.EmitImpactSound("No.Such", new Vector(4, 5, 6), 3, 0.5f));

        Assert.Equal(["emit 2/0 ent0 ch3 physics/impact1.wav v0.25 l70 f0 p98 at4,5,6"], engine.Calls);
    }

    [Fact]
    public void ATempentsScriptResolvesAndPlaysAtAPlace()
    {
        var (sounds, engine, _, _, _) = Rig();

        Assert.True(sounds.Parameters("Physics.Impact", out GameSoundParams p));
        Assert.Equal(new GameSoundParams("physics/impact1.wav", 0.5f, 98, 90, 106, 70, 4), p);
        Assert.False(sounds.Parameters("No.Such", out _));

        sounds.EmitAtPosition(p, 0.75f, 110, new Vector(1, 1, 1));
        Assert.Equal(["emit 2/0 ent0 ch4 physics/impact1.wav v0.75 l70 f0 p110 at1,1,1"], engine.Calls);
    }

    [Fact]
    public void ATracersWhizIsTheAttenuationOverloadAlongTheShot()
    {
        var (sounds, engine, _, _, _) = Rig();

        Assert.True(sounds.EmitTracerSound("Bullets.Whiz", new Vector(1, 2, 3), new Vector(0, 1, 0)));
        Assert.False(sounds.EmitTracerSound("No.Such", new Vector(1, 2, 3), new Vector(0, 1, 0)));

        // SNDLVL_TO_ATTN( 75 ) = 20 / 25 = 0.8; CHAN_STATIC from the world.
        Assert.Equal(["attn 2/0 ent0 ch6 weapons/fx/nearmiss/bulletltor03.wav v0.8 a0.8 p102 at1,2,3 dir0,1,0"], engine.Calls);
        Assert.Equal(4f, ClientSounds.SoundLevelToAttenuation(50));
        Assert.Equal(20f / 30f, ClientSounds.SoundLevelToAttenuation(80));
    }

    [Fact]
    public void ASoundscapePlaysItsAmbientOnTheEngineAndItsPositionedSoundsThroughTheEmitter()
    {
        var (sounds, engine, _, _, _) = Rig();

        sounds.EmitAmbient("ambient/wind.wav", 0.3f, 100, 0);
        sounds.EmitWorld("ambient/bird.wav", 0.2f, 70, 95, 1, new Vector(9, 9, 9));
        sounds.StopWorld("ambient/bird.wav");

        Assert.Equal(["engine-ambient ambient/wind.wav v0.3 p100 f0",
                      "emit 2/0 ent0 ch6 ambient/bird.wav v0.2 l70 f1 p95 at9,9,9",
                      "stop ent0 ch6 ambient/bird.wav"], engine.Calls);
    }

    [Fact]
    public void ANativeCallersEmitCarriesItsFilterItsRouteAndItsHandle()
    {
        var (sounds, engine, _, _, _) = Rig();
        engine.Durations["physics/impact1.wav"] = 1.5f;

        // GameSound_Emit( filter, ent, EmitSound_t ): the EmitSound_t's own handle filled in.
        short handle = -1;
        var request = new ClientSoundEmitRequest(0, "Physics.Impact", 1f, 0, 0, 100, 0, null, 0f, WantDuration: true,
                                                 EmitCloseCaption: true, WarnOnMissingCloseCaption: false,
                                                 WarnOnDirectWaveReference: false, SpeakerEntity: -1, ByHandle: false);
        float? duration = sounds.Emit([1, 3], true, false, 5, request, ref handle);

        Assert.Equal(1.5f, duration);
        Assert.Equal((short)1, handle);
        Assert.Contains("emit 1,3/1 ent5 ch4 physics/impact1.wav v0.5 l70 f0 p98 at", engine.Calls);

        // A raw wave by EmitSound_t takes the raw arm; by handle it is looked up as a script.
        engine.Calls.Clear();
        short none = -1;
        Assert.Null(sounds.Emit([2], false, false, 0, request with { SoundName = "a.wav", WantDuration = false }, ref none));
        Assert.Equal(["emit 2/0 ent0 ch0 a.wav v1 l0 f0 p100 at"], engine.Calls);
        engine.Calls.Clear();
        Assert.Null(sounds.Emit([2], false, false, 0, request with { SoundName = "a.wav", ByHandle = true, WantDuration = false }, ref none));
        Assert.Equal(["paramsex a.wav h-1"], engine.Calls);
    }

    [Fact]
    public void TheRowsStagesAreTheSystemsAndUnboundTheInitIsRefused()
    {
        var (sounds, _, system, _, _) = Rig();
        Assert.True(sounds.SystemInit());
        sounds.LevelInitPreEntity();
        sounds.LevelShutdownPostEntity();
        sounds.SystemShutdown();
        Assert.Equal(["modinit", "exists maps/dm_test_level_sounds.txt", "clear", "modshutdown"], system.Calls);

        var (unbound, engine, _, _, _) = Rig(bind: false);
        Assert.False(unbound.SystemInit());
        unbound.EmitWorld("a.wav", 1f, 75, 100, 0, default);
        Assert.False(unbound.EmitImpactSound("Physics.Impact", default, 0, 1f));
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public void ANativeStopAndCaptionReachTheEmitter()
    {
        var (sounds, engine, _, _, _) = Rig();

        sounds.Stop(4, 6, "a.wav");
        sounds.Stop(4, 6, "Physics.Impact");
        sounds.EmitCloseCaption([2], true, false, 4, "Physics.Impact", 1f, false);

        Assert.Equal(["stop ent4 ch6 a.wav", "stop ent4 ch6 a.wav", "stop ent4 ch6 b.wav"], engine.Calls);
    }
}
