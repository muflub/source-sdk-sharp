using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;

using SourceSharp.Shared;
using SourceSharp.Tests;

using System.IO;

using Xunit;

using TF2.Client.Weapons;
using TF2.Shared;
using TF2.Shared.Player;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The flamethrower's client fire (<c>CTFFlameThrower</c>'s client half,
/// tf_weapon_flamethrower.cpp): the idle gun burns its pilot light, the
/// burning gun throws its flame from the muzzle over the start/loop
/// crossfade, the release takes the flame back to the pilot through the poof,
/// a gone or dry carrier goes quiet, and the particles and categories are
/// stock's.
/// </summary>
public class FlameThrowerFxTests
{
    private const int Owner = 3;

    private readonly RecordingEngine _engine = Fake();
    private readonly CarrierData _data = new();
    private readonly AliveStandalones _standalones = new();
    private readonly SilentController _sounds = new();
    private readonly FixedBearer _bearer = new();
    private readonly TFFlameThrowerFx _fx;

    private sealed class FixedBearer : IWeaponViewModelBearer
    {
        public EntityHandle? Bearer;

        public EntityHandle? ViewModelBearer() => Bearer;
    }

    private sealed class CarrierData : ITFPlayerData
    {
        public TFPlayerNetState? Carried;

        public TFPlayerNetState? Predicted(int playerIndex) => null;

        public TFPlayerNetState? Networked(int playerIndex) => Carried;
    }

    private sealed class AliveStandalones : IClientStandalones
    {
        public readonly ClientSsPlayer Player = new(Fake());

        public AliveStandalones() => Player.State.BindLocal();

        public IReadOnlyList<ClientStandaloneEntity> Live() => [Player];

        public ClientStandaloneEntity? GetByIndex(int entityIndex) => entityIndex == Owner ? Player : null;
    }

    private sealed class SilentController : IClientSoundController
    {
        public readonly List<(int Patch, string Call, float To, float Over)> Volume = [];
        public int Next = 100;

        public void Create(int patch) { }

        public void Destroy(int patch) { }

        public void Play(int patch, float volume, float pitch, float startTime) { }

        public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count) => 0f;

        public void ChangePitch(int patch, float target, float deltaTime) { }

        public void ChangeVolume(int patch, float target, float deltaTime) => Volume.Add((patch, "volume", target, deltaTime));

        public float GetPitch(int patch) => 0f;

        public float GetVolume(int patch) => 0f;

        public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout) { }

        public void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value) { }

        public void CommandClear(int patch) { }

        public void SystemReset() { }

        public void Shutdown(int patch) { }
    }

    public FlameThrowerFxTests()
    {
        _engine.Returns("Effects.ParticleSystemStart", true);
        _engine.Returns("Effects.ParticleSystemFollowEntity", true);
        _engine.Returns("EntityRender.LookupAttachment", 4);
        _engine.Returns("Hud.LocalPlayerIndex", Owner);
        // The only game file these frames read is the flamethrower's script,
        // plain text: its categories name every arm's sound.
        _engine.Returns("Files.ReadGameFileBytes",
            File.ReadAllBytes(RepoLayout.Repo("src", "tf2", "tests", "TestData", "weapons", "tf_weapon_flamethrower.txt")));
        _engine.ReturnsEachCall("Sound.SoundPatchCreate", () => ++_sounds.Next);
        _fx = new TFFlameThrowerFx(_engine, _engine, _data, _standalones,
            new TF2.Client.Player.TFClientPlayerResources(), _sounds, _engine, _bearer,
            new TFWeaponScripts(new RecordingLog(), _engine));
    }

    private ClientSsWeapon Weapon(TFFlamethrowerBehaviour.FlameState state, int reserve = 200, bool crit = false)
    {
        var weapon = new ClientSsWeapon(_engine);
        weapon.State.BindLocal();
        weapon.State.OwnerIndex = Owner;
        weapon.State.BehaviourMode = ((int)state << 8) | (crit ? TFFlamethrowerBehaviour.CritLatchMask : 0);
        weapon.State.Reserve = reserve;
        return weapon;
    }

    private void Think(TFFlamethrowerBehaviour.FlameState state, bool alive = true, int reserve = 200, bool carrier = true,
                       bool crit = false, bool firstPerson = false)
    {
        _data.Carried = carrier ? new TFPlayerNetState() : null;
        _standalones.Player.State.Alive = alive;
        _fx.Think(new WeaponDrawContext(Weapon(state, reserve, crit), null, firstPerson, Now: 10f));
    }

    [Fact]
    public void TheIdleGunBurnsItsPilotLight()
    {
        // StartPilotLight (tf_weapon_flamethrower.cpp:2489-2504): one loop
        // patch, played at once, and no flame.
        Think(TFFlamethrowerBehaviour.FlameState.Idle);

        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate"));
        Assert.DoesNotContain(_engine.Calls, c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal(WeaponScriptSound.Special2, TFFlameThrowerFx.CategoryOf(TFFlameThrowerFx.Arm.Pilot));

        // A second idle frame keeps the one patch (the guard :2491).
        Think(TFFlamethrowerBehaviour.FlameState.Idle);
        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate"));
    }

    [Fact]
    public void TheBurningGunThrowsItsFlameAndCrossfadesTheFire()
    {
        Think(TFFlamethrowerBehaviour.FlameState.Firing);

        // The flame from the muzzle (FlameEffect_t::StartEffects :2712), the
        // pilot out (StartFlame :2358), start and loop both up (:2374/:2388).
        EngineCall start = Assert.Single(_engine.Calls, c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal("new_flame", start.Args[1]);
        Assert.Contains(_engine.Calls, c => c.Member == "Effects.ParticleSystemFollowEntity");
        Assert.Equal(2, _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate"));

        // The crossfade (StartFlame :2376/:2392-2395): the start out, the loop
        // in, over 3.5 s.
        Assert.Equal(2, _sounds.Volume.Count);
        Assert.Equal((0f, 3.5f), (_sounds.Volume[0].To, _sounds.Volume[0].Over));
        Assert.Equal((1f, 3.5f), (_sounds.Volume[1].To, _sounds.Volume[1].Over));
        Assert.Equal(WeaponScriptSound.Single, TFFlameThrowerFx.CategoryOf(TFFlameThrowerFx.Arm.Start));
        Assert.Equal(WeaponScriptSound.Special1, TFFlameThrowerFx.CategoryOf(TFFlameThrowerFx.Arm.Loop, crit: false));
    }

    [Fact]
    public void TheFirstPersonFlameBurnsFromTheViewmodelsMuzzle()
    {
        // Stock's flame rides the weapon the viewer sees: the viewmodel
        // attachment's muzzle in the first person (the bearer the muzzle flash
        // rides), the carried gun's own in the world pass. The attachment here
        // is entry 0x21, no gun's handle.
        _bearer.Bearer = new EntityHandle(0x0100_0021u);

        Think(TFFlamethrowerBehaviour.FlameState.Firing, firstPerson: true);

        EngineCall follow = Assert.Single(_engine.Calls, c => c.Member == "Effects.ParticleSystemFollowEntity");
        Assert.Equal(_bearer.Bearer, follow.Args?[2]);
        EngineCall? lookup = _engine.Calls.FirstOrDefault(c => c.Member == "EntityRender.LookupAttachment");
        Assert.NotNull(lookup);
        Assert.Equal(_bearer.Bearer, lookup?.Args?[0]);
    }

    [Fact]
    public void TheFlameStopsWithThePoofAndThePilotReturns()
    {
        Think(TFFlamethrowerBehaviour.FlameState.Firing);
        int patches = _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate");

        // The release: the poof (StopFlame :2447-2453), the patches gone, the
        // particle stopped, the pilot back (:2493's StopFlame, then :2502).
        Think(TFFlamethrowerBehaviour.FlameState.Idle);

        Assert.Contains(_engine.Calls, c => c.Member == "Sound.EntityEmitSound");
        Assert.Equal(WeaponScriptSound.Special3, TFFlameThrowerFx.CategoryOf(TFFlameThrowerFx.Arm.Stop));
        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStop"));
        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate") - patches);
    }

    [Fact]
    public void TheGoneOrDryGunGoesQuiet()
    {
        Think(TFFlamethrowerBehaviour.FlameState.Firing);
        int starts = _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart");
        int stops = _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStop");
        int poofs = _engine.Calls.Count(c => c.Member == "Sound.EntityEmitSound");
        int patches = _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate");

        // OnDataChanged's else (tf_weapon_flamethrower.cpp:2212-2218): a dead
        // carrier — everything stops, poof included (stock's StopFlame():2447).
        Think(TFFlamethrowerBehaviour.FlameState.Firing, alive: false);

        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Sound.EntityEmitSound") - poofs);
        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStop") - stops);
        Assert.Equal(0, _engine.Calls.Count(c => c.Member == "Sound.SoundPatchCreate") - patches);
        Assert.Equal(starts, _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart"));

        // An empty pool is the same else (bLocalPlayerAmmo :2191-2196).
        int at = _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart");
        Think(TFFlamethrowerBehaviour.FlameState.Firing, reserve: 0);
        Assert.Equal(at, _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart"));
    }

    [Fact]
    public void TheFlameBurnsTheTeamsColour()
    {
        // GetNewFlameEffectInternal (tf_weapon_flamethrower.cpp:283-310), no
        // attribute overrides yet.
        Assert.Equal("new_flame", TFFlameThrowerFx.ParticleName((int)TFTeam.Red, crit: false));
        Assert.Equal("new_flame", TFFlameThrowerFx.ParticleName((int)TFTeam.Blue, crit: false));
        Assert.Equal("new_flame_crit_red", TFFlameThrowerFx.ParticleName((int)TFTeam.Red, crit: true));
        Assert.Equal("new_flame_crit_blue", TFFlameThrowerFx.ParticleName((int)TFTeam.Blue, crit: true));
    }

    [Fact]
    public void TheCritFlameBurnsTheCritParticleOnTheCritLoop()
    {
        // The crit flip's restart (StartFlame :2349-2356, PrimaryAttack
        // :850-855): the particle the crit's colour, the loop the BURST arm
        // (:2382-2383), no second start sound.
        Think(TFFlamethrowerBehaviour.FlameState.Firing, crit: true);

        EngineCall start = Assert.Single(_engine.Calls, c => c.Member == "Effects.ParticleSystemStart");
        Assert.Equal("new_flame_crit_blue", start.Args[1]);
        Assert.Equal(WeaponScriptSound.Burst, TFFlameThrowerFx.CategoryOf(TFFlameThrowerFx.Arm.Loop, crit: true));
    }

    [Fact]
    public void TheAirblastPuffsOnceAtTheState()
    {
        // The SECONDARY arm (StartFlame :2334-2342): the blast particle and
        // the double shot, once — the second frame keeps it (the state's edge,
        // where stock re-runs per network update).
        Think(TFFlamethrowerBehaviour.FlameState.Secondary);
        Think(TFFlamethrowerBehaviour.FlameState.Secondary);

        Assert.Equal(1, _engine.Calls.Count(c => c.Member == "Effects.ParticleSystemStart"));
        Assert.Contains(_engine.Calls, c => c.Member == "Sound.EntityEmitSound");
        Assert.Equal(WeaponScriptSound.Double, TFFlameThrowerFx.CategoryOf(TFFlameThrowerFx.Arm.Blast));
    }
}
