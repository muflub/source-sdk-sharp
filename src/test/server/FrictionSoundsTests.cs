using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// Physics stock parity D2 (9b): the friction scrape. Stock's server answers
/// CCollisionEvent::Friction with VPhysicsFriction -> PhysFrictionSound: no
/// sound under 75 energy or against an invisible ('X') material; volume is the
/// square of energy / 15500; the surface's rough scrape, or its smooth one when
/// the hit surface is smoother than this one's threshold; a new scrape starts
/// only above 0.1 of the script's volume, on CHAN_BODY at the entity; in
/// multiplayer an ongoing scrape is only touched every 0.5 s and otherwise just
/// kept alive; and a scrape not kept alive for 0.1 s is shut down
/// (UpdateFrictionSounds). Four at once, as stock's friction table.
/// </summary>
public class FrictionSoundsTests
{
    private sealed class Controller : IServerSoundController
    {
        public readonly List<string> Calls = new();
        public void Create(int patch) => Calls.Add($"Create {patch}");
        public void Destroy(int patch) => Calls.Add($"Destroy {patch}");
        public void Play(int patch, float volume, float pitch, float startTime) => Calls.Add($"Play {patch} {volume:0.###} {pitch:0.###}");
        public void ChangePitch(int patch, float target, float deltaTime) => Calls.Add($"Pitch {patch} {target:0.###} {deltaTime:0.###}");
        public void ChangeVolume(int patch, float target, float deltaTime) => Calls.Add($"Volume {patch} {target:0.###} {deltaTime:0.###}");
        public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout) => Calls.Add($"FadeOut {patch}");
        public void Shutdown(int patch) => Calls.Add($"Shutdown {patch}");
        public void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value) { }
        public void CommandClear(int patch) { }
        public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count) => 0f;
        public void SystemReset() { }
        public float GetPitch(int patch) => 0f;
        public float GetVolume(int patch) => 0f;
    }

    private sealed class Surfaces : IPhysicsSurfaceTable
    {
        // 1: a rough crate (threshold 0.5), 2: a smooth floor (roughness 0.1),
        // 3: a rough floor (roughness 0.9), 9: an invisible material.
        public MoveSurface Of(int surfaceProps) => surfaceProps switch
        {
            1 => new MoveSurface { Valid = true, Material = 'W', RoughnessFactor = 0.8f, RoughThreshold = 0.5f },
            2 => new MoveSurface { Valid = true, Material = 'C', RoughnessFactor = 0.1f, RoughThreshold = 0.5f },
            3 => new MoveSurface { Valid = true, Material = 'C', RoughnessFactor = 0.9f, RoughThreshold = 0.5f },
            9 => new MoveSurface { Valid = true, Material = 'X' },
            4 => new MoveSurface { Valid = true, Material = 'M', RoughnessFactor = 0.5f, RoughThreshold = 0.5f },
            _ => default,
        };
    }

    private const int Crate = 1, SmoothFloor = 2, RoughFloor = 3, Metal = 4, Sky = 9;
    private static readonly EntityHandle Box = new(41);

    // energy 7750 -> 0.5 -> volume 0.25.
    private const float Hard = 7750f;

    private static (FrictionSounds Sounds, Controller Controller, RecordingEngine Engine) World()
    {
        var engine = Fake();
        engine.ReturnsEachCall("Sound.SurfaceScrapeSound",
                  () => engine.Calls.Last(c => c.Member == "Sound.SurfaceScrapeSound").ArgAs<bool>(1)
                        ? "Crate.ScrapeSmooth" : "Crate.ScrapeRough")
              .Returns("Sound.SoundScriptParameters", (SoundScriptParams?)new SoundScriptParams(1f, 100, 90, 110, 75))
              .Returns("Sound.SoundPatchCreate", 7);
        var controller = new Controller();
        return (new FrictionSounds(engine, controller, new Surfaces(), new TempEntFrictionEffects(engine, Temps)), controller, engine);
    }

    private sealed class RecordingTemps : TempEntsDouble
    {
        public readonly List<(Vector Position, Vector Direction)> MetalSparksSent = new();
        public override void MetalSparks(TeFilter filter, float delay, Vector position, Vector direction)
            => MetalSparksSent.Add((position, direction));
    }

    [ThreadStatic] private static RecordingTemps? t_temps;
    private static RecordingTemps Temps => t_temps ??= new RecordingTemps();

    //-------------------------------------------------------------------------
    // The effect half (stock's PhysFrictionEffect after VPhysicsFriction, both
    // inside the 500 ms MP throttle; plan_physics_managed B2.2).
    //-------------------------------------------------------------------------

    // Concrete dusts from MASS10 speed 28: 28^2 x 10 = 7840, just over Hard.
    private const float Scraping = 8000f;

    [Fact]
    public void A_hard_scrape_on_concrete_dusts_at_the_contact_against_the_travel()
    {
        var (sounds, _, engine) = World();

        sounds.Friction(Box, Scraping, Crate, RoughFloor, now: 0f, point: new Vector(1f, 2f, 3f),
                        velocity: new Vector(10f, 0f, 0f));

        var dust = Assert.Single(engine.Calls, c => c.Member == "Effects.DustEffect");
        Assert.Equal(new Vector(1f, 2f, 3f), dust.ArgAs<Vector>(0));
        Assert.Equal(new Vector(-1f, 0f, 0f), dust.ArgAs<Vector>(1));
        Assert.Equal(1f, dust.ArgAs<float>(2));
        Assert.Equal(16f, dust.ArgAs<float>(3));
    }

    [Fact]
    public void A_scrape_under_the_threshold_throws_nothing()
    {
        var (sounds, _, engine) = World();

        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0f, point: Vector.Zero, velocity: new Vector(10f, 0f, 0f));

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Effects.DustEffect");
    }

    [Fact]
    public void Inside_the_throttle_no_second_effect_is_thrown()
    {
        var (sounds, _, engine) = World();
        sounds.Friction(Box, Scraping, Crate, RoughFloor, now: 0f, point: Vector.Zero, velocity: new Vector(10f, 0f, 0f));

        sounds.Friction(Box, Scraping, Crate, RoughFloor, now: 0.2f, point: Vector.Zero, velocity: new Vector(10f, 0f, 0f));

        Assert.Single(engine.Calls, c => c.Member == "Effects.DustEffect");
    }

    [Fact]
    public void Metal_scraping_concrete_hard_sends_metal_sparks_to_the_point()
    {
        t_temps = new RecordingTemps();
        var (sounds, _, _) = World();

        sounds.Friction(Box, PhysicsFrictionEffect.Mass10Energy(51f), Metal, RoughFloor, now: 0f,
                        point: new Vector(5f, 0f, 0f), velocity: new Vector(0f, 10f, 0f));

        Assert.Equal(new[] { (new Vector(5f, 0f, 0f), new Vector(0f, -1f, 0f)) }, t_temps.MetalSparksSent);
    }

    [Fact]
    public void The_effect_does_not_wait_on_the_sound()
    {
        // An invisible object makes no scrape sound; stock's effect is a
        // separate call after VPhysicsFriction, and the floor is concrete.
        var (sounds, _, engine) = World();

        sounds.Friction(Box, Scraping, Sky, RoughFloor, now: 0f, point: Vector.Zero, velocity: new Vector(10f, 0f, 0f));

        Assert.Single(engine.Calls, c => c.Member == "Effects.DustEffect");
    }

    [Fact]
    public void A_hard_scrape_starts_the_rough_sound_on_the_entity()
    {
        var (sounds, controller, engine) = World();

        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0f);

        var made = Assert.Single(engine.Calls, c => c.Member == "Sound.SoundPatchCreate");
        Assert.Equal(Box, made.Args[0]);
        Assert.Equal(4, made.Args[1]);                      // CHAN_BODY
        Assert.Equal("Crate.ScrapeRough", made.Args[2]);
        Assert.Equal(75, made.Args[3]);
        Assert.Equal(new[] { "Play 7 0.25 100" }, controller.Calls);
    }

    [Fact]
    public void A_smoother_floor_picks_the_smooth_scrape()
    {
        var (sounds, _, engine) = World();

        sounds.Friction(Box, Hard, Crate, SmoothFloor, now: 0f);

        Assert.Equal("Crate.ScrapeSmooth",
                     Assert.Single(engine.Calls, c => c.Member == "Sound.SoundPatchCreate").Args[2]);
    }

    [Fact]
    public void Under_75_energy_is_silent()
    {
        var (sounds, controller, engine) = World();

        sounds.Friction(Box, 74f, Crate, RoughFloor, now: 0f);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Sound.SoundPatchCreate");
        Assert.Empty(controller.Calls);
    }

    [Fact]
    public void An_invisible_material_never_scrapes()
    {
        var (sounds, _, engine) = World();

        sounds.Friction(Box, Hard, Crate, Sky, now: 0f);
        sounds.Friction(Box, Hard, Sky, RoughFloor, now: 0f);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Sound.SoundPatchCreate");
    }

    [Fact]
    public void A_quiet_scrape_never_starts()
    {
        // energy 4650 -> 0.3 -> volume 0.09: over 1/128, but 1 x 0.09 <= 0.1.
        var (sounds, _, engine) = World();

        sounds.Friction(Box, 4650f, Crate, RoughFloor, now: 0f);

        Assert.DoesNotContain(engine.Calls, c => c.Member == "Sound.SoundPatchCreate");
    }

    [Fact]
    public void Within_half_a_second_a_scrape_is_only_kept_alive()
    {
        var (sounds, controller, _) = World();

        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0f);
        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0.3f);
        sounds.Update(now: 0.35f);

        Assert.Equal(new[] { "Play 7 0.25 100" }, controller.Calls);
    }

    [Fact]
    public void After_half_a_second_it_ramps_the_volume_and_pitch()
    {
        // volume 0.25: pitch 0.25 x (110 - 90) + 90 = 95, both over 0.1 s.
        var (sounds, controller, _) = World();

        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0f);
        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0.6f);

        Assert.Equal(new[] { "Play 7 0.25 100", "Volume 7 0.25 0.1", "Pitch 7 95 0.1" }, controller.Calls);
    }

    [Fact]
    public void A_scrape_that_stops_is_shut_down_after_a_tenth_of_a_second()
    {
        var (sounds, controller, engine) = World();

        sounds.Friction(Box, Hard, Crate, RoughFloor, now: 0f);
        sounds.Update(now: 0.05f);
        Assert.DoesNotContain("Destroy 7", controller.Calls);

        sounds.Update(now: 0.2f);

        Assert.Contains("Destroy 7", controller.Calls);
        Assert.Equal(7, Assert.Single(engine.Calls, c => c.Member == "Sound.SoundPatchFree").Args[0]);
        Assert.Equal(0, sounds.Active);
    }

    [Fact]
    public void Four_scrapes_at_once_is_the_limit()
    {
        var (sounds, _, engine) = World();

        for (uint i = 0; i < 5; i++)
            sounds.Friction(new EntityHandle(50 + i), Hard, Crate, RoughFloor, now: 0f);

        Assert.Equal(4, engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate"));
        Assert.Equal(4, sounds.Active);
    }
}
