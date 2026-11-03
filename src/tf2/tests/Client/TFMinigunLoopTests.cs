extern alias client;

using SourceSharp.Abi;

using TF2.Tests.Weapons;

using Xunit;

using ClientEngine = client::SourceSharp.Abi.RecordingEngine;
using ClientLog = client::SourceSharp.Abi.RecordingLog;
using ClientSound = client::SourceSharp.Client.Interop.IClientSoundController;
using ClientLoop = client::TF2.Client.Weapons.TFMinigunLoop;
using ClientWeapon = client::SourceSharp.Client.Standalone.ClientSsWeapon;
using CAbi = client::SourceSharp.Abi;
using CEffects = client::SourceSharp.Client.WeaponEffects;
using CTF = client::TF2.Shared;

namespace TF2.Tests.Client;

/// <summary>
/// The minigun's loop sounds on the client (<c>WeaponSoundUpdate</c>,
/// <c>tf_weapon_minigun.cpp:1381-1496</c>): one sound per weapon state,
/// named by the script's category — wind-up (<c>special1</c>), the firing
/// loop (<c>double_shot</c>, <c>burst</c> on a crit shot), the spin
/// (<c>special3</c>) and the empty click — stopped when the gun goes idle.
/// </summary>
public sealed class TFMinigunLoopTests
{
    private const int WeaponEntity = 9;

    [Fact]
    public void The_wind_up_loops_the_script_s_wind_sound()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.StartFiring);

        rig.Frame(10f);

        Assert.Equal("Weapon_Minigun.WindUp", rig.Created.Name);
        Assert.Equal(rig.Weapon.Handle, rig.Created.Owner);
        Assert.Contains(77, rig.Sounds.Created);
        Assert.Contains((77, 1f), rig.Sounds.Played);
    }

    /// <summary>
    /// The firing arm's two halves (:1411-1421): the plain loop is
    /// <c>double_shot</c>, a crit shot's loop is <c>burst</c>; the change of
    /// name stops the old loop before the new one is made (:1469-1487).
    /// </summary>
    [Fact]
    public void The_firing_loop_is_the_plain_shot_until_the_crit_latch_lifts_it()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);
        rig.Frame(10f);
        Assert.Equal("Weapon_Minigun.Fire", rig.Created.Name);

        rig.Into(CTF.TFMinigunBehaviour.State.Firing, crit: true);
        rig.Frame(10.1f);

        Assert.Equal("Weapon_Minigun.FireCrit", rig.Engine.Last("Sound.SoundPatchCreate")!.Value.ArgAs<string>(2));
        Assert.Equal(2, rig.Engine.CountOf("Sound.SoundPatchCreate"));
        Assert.Equal(1, rig.Engine.CountOf("Sound.SoundPatchFree"));
        Assert.Single(rig.Sounds.Destroyed);
    }

    [Fact]
    public void A_spun_gun_loops_the_spin_sound()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Spinning);

        rig.Frame(10f);

        Assert.Equal("Weapon_Minigun.Spin", rig.Created.Name);
    }

    /// <summary>The dry-fire arm (:1429-1431): the empty click, looped like the rest.</summary>
    [Fact]
    public void A_dry_gun_loops_the_empty_click()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.DryFire);

        rig.Frame(10f);

        Assert.Equal("Weapon_Minigun.ClipEmpty", rig.Created.Name);
    }

    /// <summary>
    /// The idle arm's two halves (:1387-1406): the wind-down plays while the
    /// barrel still turns, and the loop stops the frame the velocity lands on
    /// exactly zero — nothing takes its place after that.
    /// </summary>
    [Fact]
    public void An_idle_gun_winds_the_still_barrel_down_to_silence()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);
        rig.Frame(10f);

        rig.Into(CTF.TFMinigunBehaviour.State.Idle);
        rig.Frame(10.1f);

        Assert.Equal("Weapon_Minigun.WindDown", rig.Created.Name);
        Assert.Single(rig.Sounds.Destroyed);
        Assert.Equal(2, rig.Engine.CountOf("Sound.SoundPatchCreate"));
        Assert.Equal(0f, rig.Loop.BarrelVelocity(rig.Weapon.Handle));

        rig.Frame(10.2f);

        Assert.Equal(2, rig.Sounds.Destroyed.Count);
        Assert.Equal(2, rig.Engine.CountOf("Sound.SoundPatchCreate"));
        Assert.Equal(2, rig.Engine.CountOf("Sound.SoundPatchFree"));
    }

    /// <summary>A press during the wind-down hands the gun back to its wind-up.</summary>
    [Fact]
    public void A_press_during_the_wind_down_winds_up_again()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.StartFiring);
        rig.Frame(10f);

        rig.Into(CTF.TFMinigunBehaviour.State.Idle);
        rig.Frame(10.1f);
        Assert.Equal("Weapon_Minigun.WindDown", rig.Created.Name);

        rig.Into(CTF.TFMinigunBehaviour.State.StartFiring);
        rig.Frame(10.2f);

        Assert.Equal("Weapon_Minigun.WindUp", rig.Created.Name);
        Assert.Equal(3, rig.Engine.CountOf("Sound.SoundPatchCreate"));
        Assert.Equal(2, rig.Engine.CountOf("Sound.SoundPatchFree"));
    }

    /// <summary>A steady state never restarts its loop (:1453-1467).</summary>
    [Fact]
    public void A_steady_loop_never_restarts()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);

        for (int i = 0; i < 100; i++)
            rig.Frame(10f + i * 0.01f);

        Assert.Equal(1, rig.Engine.CountOf("Sound.SoundPatchCreate"));
        Assert.Empty(rig.Sounds.Destroyed);
    }

    /// <summary>UpdateOnRemove's arm (:1150-1156): the gun going takes its loop with it.</summary>
    [Fact]
    public void A_forgotten_gun_frees_its_loop()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Spinning);
        rig.Frame(10f);

        rig.Loop.Forget(rig.Weapon.Handle);

        Assert.Single(rig.Sounds.Destroyed);
        Assert.Equal(1, rig.Engine.CountOf("Sound.SoundPatchFree"));
    }

    // ---------------------------------------------------------------------

    private sealed class LoopRig
    {
        public readonly ClientEngine Engine = new();
        public readonly SoundSpy Sounds = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientLoop Loop;

        public LoopRig()
        {
            Engine.Returns("Sound.SoundPatchCreate", 77);

            Weapon = new ClientWeapon(Engine) { Handle = new EntityHandle(0x0002_0000u | WeaponEntity) };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = 3;

            Loop = new ClientLoop(
                new CTF.TFWeaponScripts(new ClientLog(), new FixtureFiles(WeaponFixtures.ShippedWithClasses())),
                Sounds, Engine);
        }

        /// <summary>The gun in this state, with the crit latch beside it.</summary>
        public void Into(CTF.TFMinigunBehaviour.State state, bool crit = false) =>
            Weapon.State.BehaviourMode = ((int)state << 8) | (crit ? 1 << 11 : 0);

        public void Frame(float now) =>
            Loop.Think(new CEffects.WeaponDrawContext(Weapon, null, FirstPerson: true, Now: now));

        /// <summary>The last loop the engine was asked to create: its name and its entity.</summary>
        public (string Name, EntityHandle Owner) Created
        {
            get
            {
                var call = Engine.Last("Sound.SoundPatchCreate")!.Value;
                return (call.ArgAs<string>(2), call.ArgAs<EntityHandle>(0));
            }
        }
    }

    /// <summary>The shipped script fixture, over the client's file seam.</summary>
    private sealed class FixtureFiles(TFClassTableTests.Files files) : CAbi.IEngineFiles
    {
        public int GameFileSize(string path) => files.GameFileSize(path);
        public string? ReadGameFile(string path) => files.ReadGameFile(path);
        public byte[]? ReadGameFileBytes(string path) => files.ReadGameFileBytes(path);
        public byte[]? ReadFileOnPath(string path, string pathId) => files.ReadFileOnPath(path, pathId);

        public Dictionary<string, byte[]> Bytes => files.Bytes;
    }

    /// <summary>The envelope controller, recording what the loop asked of it.</summary>
    private sealed class SoundSpy : ClientSound
    {
        public readonly List<int> Created = [];
        public readonly List<int> Destroyed = [];
        public readonly List<(int Patch, float Volume)> Played = [];

        public void Create(int patch) => Created.Add(patch);
        public void Destroy(int patch) => Destroyed.Add(patch);
        public void Play(int patch, float volume, float pitch, float startTime) => Played.Add((patch, volume));
        public float PlayEnvelope(int patch, int command, ReadOnlySpan<float> points, int count) => 0f;
        public void ChangePitch(int patch, float target, float deltaTime) { }
        public void ChangeVolume(int patch, float target, float deltaTime) { }
        public float GetPitch(int patch) => 0f;
        public float GetVolume(int patch) => 0f;
        public void FadeOut(int patch, float deltaTime, bool destroyOnFadeout) { }
        public void CommandAdd(int patch, float executeDeltaTime, int command, float commandTime, float value) { }
        public void CommandClear(int patch) { }
        public void SystemReset() { }
        public void Shutdown(int patch) { }
    }
}
