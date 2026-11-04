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

    /// <summary>
    /// The rotation arm (:1118): the angle rides the stepped velocity times
    /// the frame time, so the first frame at frame time 2 turns the barrel by
    /// 0.1 × 2 and the second adds 0.2 × 2 to it.
    /// </summary>
    [Fact]
    public void The_barrel_angle_rises_by_the_velocity_times_the_frame_time()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.StartFiring);

        rig.Frame(10f, frameTime: 2f);
        Assert.Equal(0.2f, rig.Loop.BarrelAngle(rig.Weapon.Handle), 4);

        rig.Frame(10.1f, frameTime: 2f);
        Assert.Equal(0.6f, rig.Loop.BarrelAngle(rig.Weapon.Handle), 4);
    }

    /// <summary>
    /// The wind-down's tail (:1110, :1118): while the velocity decays the
    /// angle keeps rising, and the frame it lands on zero the angle stops —
    /// a long frame past it moves nothing.
    /// </summary>
    [Fact]
    public void The_barrel_angle_holds_the_frame_the_velocity_lands_on_zero()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.StartFiring);

        for (int i = 0; i < 200; i++)
            rig.Frame(10f + i * 0.01f, frameTime: 0.01f);

        rig.Into(CTF.TFMinigunBehaviour.State.Idle);

        for (int i = 0; i < 201; i++)
            rig.Frame(20f + i * 0.01f, frameTime: 0.01f);

        Assert.Equal(0f, rig.Loop.BarrelVelocity(rig.Weapon.Handle));
        float held = rig.Loop.BarrelAngle(rig.Weapon.Handle);

        rig.Frame(25f, frameTime: 0.5f);

        Assert.Equal(held, rig.Loop.BarrelAngle(rig.Weapon.Handle), 4);
    }

    /// <summary>The bone read (stock's StandardBlendingRules arm, :1072): the held
    /// angle answers for the bone named barrel; an unknown gun answers nothing.</summary>
    [Fact]
    public void The_bone_spin_answers_the_held_angle_for_the_gun_it_stepped()
    {
        var rig = new LoopRig();
        Assert.Null(rig.Loop.TryGetBoneSpin(rig.Weapon.Handle));

        rig.Into(CTF.TFMinigunBehaviour.State.StartFiring);
        rig.Frame(10f, frameTime: 2f);

        var spin = rig.Loop.TryGetBoneSpin(rig.Weapon.Handle)!.Value;
        Assert.Equal("barrel", spin.BoneName);
        Assert.Equal(0.2f, spin.AngleRadians, 4);
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

    /// <summary>
    /// Stock's firing arm (<c>StartBrassEffect</c>/<c>StartMuzzleEffect</c>,
    /// :1206-1265): the firing state gets its two looped systems on the gun —
    /// the flash <c>muzzle_minigun_constant</c> on the <c>muzzle</c> attachment
    /// and the brass <c>eject_minigunbrass</c> on <c>eject_brass</c>, each
    /// follow-pointed at control point zero (:1229-1231, :1261-1263).
    /// </summary>
    [Fact]
    public void Firing_starts_the_flash_and_the_brass_on_their_attachments()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);

        rig.Frame(10f);

        Assert.Equal(2, rig.Engine.CountOf("Effects.ParticleSystemStartSlot"));
        // Stock's OnDataChanged runs brass then muzzle (:1127-1128), so the
        // brass's slot leads the log.
        var started = rig.Engine.AllOf("Effects.ParticleSystemStartSlot").ToList();
        Assert.Equal(rig.Weapon.Handle, started[0].ArgAs<EntityHandle>(0));
        Assert.Equal("eject_minigunbrass", started[0].ArgAs<string>(1));
        Assert.Equal(1, started[0].ArgAs<int>(2));
        Assert.Equal("muzzle_minigun_constant", started[1].ArgAs<string>(1));
        Assert.Equal(0, started[1].ArgAs<int>(2));

        Assert.Equal(2, rig.Engine.CountOf("Effects.ParticleSystemFollowEntitySlot"));
        var followed = rig.Engine.AllOf("Effects.ParticleSystemFollowEntitySlot").ToList();
        Assert.Equal(0, followed[0].ArgAs<int>(1));                     // control point zero
        Assert.Equal(rig.Weapon.Handle, followed[0].ArgAs<EntityHandle>(2));
        Assert.Equal(4, followed[0].ArgAs<int>(3));                     // the looked-up attachment
        Assert.Equal(1, followed[0].ArgAs<int>(4));                     // the brass's own slot

        var looked = rig.Engine.AllOf("EntityRender.LookupAttachment").ToList();
        Assert.Equal("eject_brass", looked[0].ArgAs<string>(1));
        Assert.Equal("muzzle", looked[1].ArgAs<string>(1));
    }

    /// <summary>
    /// GetWeaponForEffect (:4564-4567): in the first person the flash and the
    /// brass ride the viewmodel's attachments. The carried gun is not drawn
    /// there, so they burned where nobody looks.
    /// </summary>
    [Fact]
    public void In_the_first_person_the_flash_and_brass_follow_the_viewmodel()
    {
        var rig = new LoopRig();
        var viewmodel = new EntityHandle(0x0003_0000u | 77);
        rig.Bearer.Held = viewmodel;
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);

        rig.Frame(10f);

        var followed = rig.Engine.AllOf("Effects.ParticleSystemFollowEntitySlot").ToList();
        Assert.Equal(2, followed.Count);
        Assert.All(followed, f => Assert.Equal(viewmodel, f.ArgAs<EntityHandle>(2)));
        // The systems stay keyed by the gun; the attachments are the bearer's.
        Assert.All(followed, f => Assert.Equal(rig.Weapon.Handle, f.ArgAs<EntityHandle>(0)));
        Assert.All(rig.Engine.AllOf("EntityRender.LookupAttachment"),
                   l => Assert.Equal(viewmodel, l.ArgAs<EntityHandle>(0)));
    }

    /// <summary>
    /// The handle arms (:1304-1329): leaving the firing state stops the two
    /// systems once, the stop holds while the gun stays off, and coming back
    /// starts them again — the looked-up attachments cached across the stretch
    /// (:1223, :1255), so the second start asks the model for nothing new.
    /// </summary>
    [Fact]
    public void Leaving_firing_stops_the_systems_and_a_refire_starts_them_again()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);
        rig.Frame(10f);

        rig.Into(CTF.TFMinigunBehaviour.State.Idle);
        rig.Frame(10.1f);

        Assert.Equal(2, rig.Engine.CountOf("Effects.ParticleSystemStopSlot"));

        rig.Frame(10.2f);
        Assert.Equal(2, rig.Engine.CountOf("Effects.ParticleSystemStopSlot"));

        rig.Into(CTF.TFMinigunBehaviour.State.Firing);
        rig.Frame(10.3f);

        Assert.Equal(4, rig.Engine.CountOf("Effects.ParticleSystemStartSlot"));
        Assert.Equal(2, rig.Engine.CountOf("Effects.ParticleSystemStopSlot"));
        Assert.Equal(2, rig.Engine.CountOf("EntityRender.LookupAttachment"));
    }

    /// <summary>UpdateOnRemove's particle arms (:1158-1160): the gun going takes its two systems with it.</summary>
    [Fact]
    public void A_forgotten_gun_stops_its_running_systems()
    {
        var rig = new LoopRig();
        rig.Into(CTF.TFMinigunBehaviour.State.Firing);
        rig.Frame(10f);

        rig.Loop.Forget(rig.Weapon.Handle);

        Assert.Equal(2, rig.Engine.CountOf("Effects.ParticleSystemStopSlot"));
    }

    // ---------------------------------------------------------------------

    private sealed class FixedBearer : CEffects.IWeaponViewModelBearer
    {
        public EntityHandle? Held;

        public EntityHandle? ViewModelBearer() => Held;
    }

    private sealed class LoopRig
    {
        public readonly ClientEngine Engine = new();
        public readonly SoundSpy Sounds = new();
        public readonly FixedBearer Bearer = new();
        public readonly ClientWeapon Weapon;
        public readonly ClientLoop Loop;

        public LoopRig()
        {
            Engine.Returns("Sound.SoundPatchCreate", 77);
            Engine.Returns("Effects.ParticleSystemStartSlot", true);
            Engine.Returns("Effects.ParticleSystemFollowEntitySlot", true);
            Engine.Returns("EntityRender.LookupAttachment", 4);

            Weapon = new ClientWeapon(Engine) { Handle = new EntityHandle(0x0002_0000u | WeaponEntity) };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = 3;

            Loop = new ClientLoop(
                new CTF.TFWeaponScripts(new ClientLog(), new FixtureFiles(WeaponFixtures.ShippedWithClasses())),
                Sounds, Engine, Engine, Engine, Bearer);
        }

        /// <summary>The gun in this state, with the crit latch beside it.</summary>
        public void Into(CTF.TFMinigunBehaviour.State state, bool crit = false) =>
            Weapon.State.BehaviourMode = ((int)state << 8) | (crit ? 1 << 11 : 0);

        public void Frame(float now) =>
            Loop.Think(new CEffects.WeaponDrawContext(Weapon, null, FirstPerson: true, Now: now));

        public void Frame(float now, float frameTime) =>
            Loop.Think(new CEffects.WeaponDrawContext(Weapon, null, FirstPerson: true, Now: now, FrameTime: frameTime));

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
