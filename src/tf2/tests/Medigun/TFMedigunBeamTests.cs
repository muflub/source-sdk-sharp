extern alias client;

using SourceSharp.Abi;

using TF2.Tests.Medigun;

using Xunit;

// The client's half, under the alias: the shared source compiles into both
// sides, so the mirrors and their netstates exist twice.
using CAbi = client::SourceSharp.Abi;
using CModule = client::SourceSharp.Client;
using CClient = client::SourceSharp.Client.Standalone;
using CInter = client::SourceSharp.Client.Interop;
using CEffects = client::SourceSharp.Client.WeaponEffects;
using CShared = client::SourceSharp.Shared;
using CPlayer = client::TF2.Shared.Player;
using CTeam = client::TF2.Shared;

using ClientBeam = client::TF2.Client.Weapons.TFMedigunBeam;
using ClientResources = client::TF2.Client.Player.TFClientPlayerResources;
using ClientResource = client::TF2.Client.Player.ClientTFPlayerResource;
using ClientViews = client::TF2.Shared.Player.TFPlayerNetStateViews;
using CondMask = client::TF2.Shared.Conditions.TFCondMask;
using TFCondId = client::TF2.Shared.TFCond;

namespace TF2.Tests.Medigun;

/// <summary>
/// The medigun a player sees: the beam from the muzzle to the patient
/// (<c>UpdateEffects</c>), the heal loop, the detach, the full-charge glow
/// and the no-target buzz (<c>ClientThink</c>, <c>StopHealSound</c>,
/// <c>ManageChargeEffect</c>).
/// </summary>
public sealed class TFMedigunBeamTests
{
    private const int Medic = 3, Patient = 4, Local = 1;

    [Fact]
    public void A_healing_medic_s_beam_runs_from_his_muzzle_to_his_patient()
    {
        var rig = new BeamRig(team: (int)CTeam.TFTeam.Red);
        rig.Healing(target: Patient);

        rig.Frame(10f);

        var start = rig.Engine.Last("Effects.ParticleSystemStart");
        Assert.NotNull(start);
        Assert.Equal("medicgun_beam_red", start!.Value.ArgAs<string>(1));
        Assert.Equal(rig.Weapon.Handle, start!.Value.ArgAs<EntityHandle>(0));

        // The near end rides the gun's muzzle, the far end the patient's
        // origin 50 up (UpdateEffects:2467-2468).
        var follows = rig.Engine.AllOf("Effects.ParticleSystemFollowEntity").ToArray();
        Assert.Equal(2, follows.Length);
        Assert.Equal(0, follows[0].ArgAs<int>(1));
        Assert.Equal(rig.Weapon.Handle, follows[0].ArgAs<EntityHandle>(2));
        Assert.Equal(5, follows[0].ArgAs<int>(3));
        Assert.Equal(1, follows[1].ArgAs<int>(1));
        Assert.Equal(rig.Patient.Handle, follows[1].ArgAs<EntityHandle>(2));
        Assert.Equal(0, follows[1].ArgAs<int>(3));
        Assert.Equal(new Vector(0f, 0f, 50f), follows[1].ArgAs<Vector>(4));
    }

    [Fact]
    public void The_beam_names_the_medic_s_team_s_colour()
    {
        var rig = new BeamRig(team: (int)CTeam.TFTeam.Blue);
        rig.Healing(target: Patient);

        rig.Frame(10f);

        Assert.Equal("medicgun_beam_blue", rig.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_charge_release_renames_the_beam()
    {
        var rig = new BeamRig(team: (int)CTeam.TFTeam.Red);
        rig.Healing(target: Patient);
        rig.Frame(10f);

        rig.Carrier.ChargeReleasing = true;
        rig.Frame(11f);

        // Stock re-runs UpdateEffects on the change and names the _invun arm.
        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStop"));
        Assert.Equal("medicgun_beam_red_invun", rig.Engine.Last("Effects.ParticleSystemStart")!.Value.ArgAs<string>(1));
    }

    [Fact]
    public void A_change_of_patient_restarts_the_beam()
    {
        var rig = new BeamRig();
        rig.Healing(target: Patient);
        rig.Frame(10f);

        rig.Carrier.HealTargetIndex = 5;
        rig.Frame(11f);

        var starts = rig.Engine.AllOf("Effects.ParticleSystemStart").ToArray();
        Assert.Equal(2, starts.Length);
        Assert.Equal(rig.Standalones.GetByIndex(5)!.Handle,
                     rig.Engine.Last("Effects.ParticleSystemFollowEntity")!.Value.ArgAs<EntityHandle>(2));
    }

    [Fact]
    public void A_steady_beam_never_restarts()
    {
        var rig = new BeamRig();
        rig.Healing(target: Patient);

        for (int i = 0; i < 500; i++)
            rig.Frame(10f + i * 0.01f);

        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStart"));
        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStop"));
    }

    [Fact]
    public void A_refused_start_leaves_no_beam()
    {
        var rig = new BeamRig();
        rig.Engine.Returns("Effects.ParticleSystemStart", false);
        rig.Healing(target: Patient);

        rig.Frame(10f);

        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemFollowEntity"));
        Assert.Null(rig.Beam.GunOf(rig.Weapon.Handle)!.Beam);
    }

    [Fact]
    public void A_non_medic_stops_everything()
    {
        var rig = new BeamRig();
        rig.Healing(target: Patient, charging: true);
        rig.Frame(10f);

        rig.Carrier.PlayerClass = (int)CTeam.TFClass.Scout;
        rig.Frame(11f);

        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStop"));
        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStopExtra"));
        Assert.Null(rig.Beam.GunOf(rig.Weapon.Handle)!.Beam);
    }

    [Fact]
    public void The_heal_loop_is_made_the_first_healing_frame_and_freed_with_the_detach_on_the_last()
    {
        var rig = new BeamRig();
        rig.Healing(target: Patient);
        rig.Frame(10f);

        var patch = rig.Engine.Last("Sound.SoundPatchCreate");
        Assert.NotNull(patch);
        Assert.Equal(rig.Weapon.Handle, patch!.Value.ArgAs<EntityHandle>(0));
        Assert.Equal("WeaponMedigun.HealingWorld", patch!.Value.ArgAs<string>(2));
        Assert.Contains(77, rig.Sounds.Created);
        Assert.Contains((77, 1f), rig.Sounds.Played);

        rig.Carrier.HealTargetIndex = -1;
        rig.Frame(11f);

        Assert.Contains(77, rig.Sounds.Destroyed);
        Assert.Equal("WeaponMedigun.HealingDetachWorld", rig.Engine.Last("Sound.SoundPatchCreate")!.Value.ArgAs<string>(2));

        // A thousand frames of nothing never grow it.
        for (int i = 0; i < 500; i++)
            rig.Frame(12f + i * 0.01f);

        Assert.Equal(2, rig.Engine.CountOf("Sound.SoundPatchCreate"));
    }

    [Fact]
    public void The_heal_loop_mixes_for_whosever_ears_are_local()
    {
        var rig = new BeamRig(local: Medic);
        rig.Healing(target: Patient);
        rig.Frame(10f);
        Assert.Equal("WeaponMedigun.HealingHealer", rig.Engine.Last("Sound.SoundPatchCreate")!.Value.ArgAs<string>(2));

        var patientRig = new BeamRig(local: Patient);
        patientRig.Healing(target: Patient);
        patientRig.Frame(10f);
        Assert.Equal("WeaponMedigun.HealingTarget", patientRig.Engine.Last("Sound.SoundPatchCreate")!.Value.ArgAs<string>(2));
    }

    [Fact]
    public void The_full_charge_glow_and_its_loop_come_up_and_go_down()
    {
        var rig = new BeamRig(team: (int)CTeam.TFTeam.Red);
        rig.Healing(target: Patient);
        rig.Weapon.State.BehaviourMeter = 1f;
        rig.Frame(10f);

        Assert.Equal("medicgun_invulnstatus_fullcharge_red",
                     rig.Engine.Last("Effects.ParticleSystemStartExtra")!.Value.ArgAs<string>(1));
        Assert.Equal("WeaponMedigun.Charged", rig.Engine.Last("Sound.SoundPatchCreate")!.Value.ArgAs<string>(2));

        rig.Weapon.State.BehaviourMeter = 0.5f;
        rig.Frame(11f);

        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStopExtra"));
    }

    [Fact]
    public void The_full_charge_glow_waits_out_the_medic_s_taunt_and_restarts_after_it()
    {
        // Stock's bOwnerTaunting guard (tf_weapon_medigun.cpp:2129-2138): the glow stays down
        // while the owner taunts, and the first/third-person switch a taunt causes restarts
        // the effect (:2141-2145). Our one gun handle restarts it as the guard lifts.
        var rig = new BeamRig(team: (int)CTeam.TFTeam.Red);
        rig.Healing(target: Patient, charging: true);
        ClientViews.SetConditions(rig.Carrier, CondMask.Of(TFCondId.Taunting));

        rig.Frame(10f);
        Assert.Equal(0, rig.Engine.CountOf("Effects.ParticleSystemStartExtra"));

        ClientViews.SetConditions(rig.Carrier, default);
        rig.Frame(11f);

        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStartExtra"));
    }

    [Fact]
    public void The_no_target_buzz_asks_every_half_second()
    {
        var rig = new BeamRig(local: Medic);
        rig.Weapon.State.LastButtons = (int)InputButtons.Attack;
        rig.Carrier.HealTargetIndex = -1;
        rig.Frame(10f);

        Assert.Equal("WeaponMedigun.NoTarget", rig.Engine.Last("Sound.EntityEmitSound")!.Value.ArgAs<string>(1));

        rig.Frame(10.2f);
        Assert.Equal(1, rig.Engine.CountOf("Sound.EntityEmitSound"));

        rig.Frame(10.6f);
        Assert.Equal(2, rig.Engine.CountOf("Sound.EntityEmitSound"));
    }

    [Fact]
    public void A_released_medic_lets_go_of_everything()
    {
        var rig = new BeamRig();
        rig.Healing(target: Patient, charging: true);
        rig.Frame(10f);

        rig.Beam.Forget(rig.Weapon.Handle);

        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStop"));
        Assert.Equal(1, rig.Engine.CountOf("Effects.ParticleSystemStopExtra"));
        Assert.Equal(2, rig.Sounds.Destroyed.Count);   // the heal loop and the charged loop
    }

    // =====================================================================
    // Rig.
    // =====================================================================

    private sealed class BeamRig
    {
        public readonly CAbi.RecordingEngine Engine = new();
        public readonly SoundSpy Sounds = new();
        public readonly ClientResources Resources = new();
        public readonly FakeData Data = new();
        public readonly FakeStandalones Standalones = new();
        public readonly ClientBeam Beam;
        public readonly CClient.ClientSsWeapon Weapon;
        public readonly CClient.ClientSsWeapon Patient;
        public readonly CPlayer.TFPlayerNetState Carrier = new();

        public BeamRig(int team = (int)CTeam.TFTeam.Red, int local = Local)
        {
            Carrier.BindLocal();
            Carrier.PlayerClass = (int)CTeam.TFClass.Medic;
            Data.States[Medic] = Carrier;

            Engine.Returns("Effects.ParticleSystemStart", true);
            Engine.Returns("Effects.ParticleSystemStartExtra", true);
            Engine.Returns("Effects.ParticleSystemFollowEntity", true);
            Engine.Returns("EntityRender.LookupAttachment", 5);
            Engine.Returns("Sound.SoundPatchCreate", 77);
            Engine.Returns("Hud.LocalPlayerIndex", local);

            var resource = new ClientResource(Engine, Resources);
            resource.State.BindLocal();
            Resources.Live = resource;
            for (int i = 0; i < CPlayer.TFPlayerResourceNetState.Slots; i++)
                resource.State.Team[i] = (byte)team;

            var medic = new CClient.ClientSsPlayer(Engine);
            medic.State.BindLocal();
            medic.State.Alive = true;
            medic.Handle = new EntityHandle(0x0001_0003u);
            Patient = new CClient.ClientSsWeapon(Engine) { Handle = new EntityHandle(0x0002_0004u) };
            var other = new CClient.ClientSsWeapon(Engine) { Handle = new EntityHandle(0x0002_0005u) };
            Standalones.Entities[Medic] = medic;
            Standalones.Entities[4] = Patient;
            Standalones.Entities[5] = other;

            Weapon = new CClient.ClientSsWeapon(Engine) { Handle = new EntityHandle(0x0003_0009u) };
            Weapon.State.BindLocal();
            Weapon.State.OwnerIndex = Medic;

            Beam = new ClientBeam(Engine, Engine, Data, Standalones, Resources, Sounds, Engine, Engine);
        }

        public void Healing(int target, bool charging = false)
        {
            Carrier.HealTargetIndex = target;
            if (charging)
                Weapon.State.BehaviourMeter = 1f;
        }

        public void Frame(float now) =>
            Beam.Think(new CEffects.WeaponDrawContext(Weapon, null, FirstPerson: false, Now: now));
    }

    /// <summary>The client's carrier answers, everyone else's does not.</summary>
    private sealed class FakeData : CPlayer.ITFPlayerData
    {
        public readonly Dictionary<int, CPlayer.TFPlayerNetState> States = [];

        public CPlayer.TFPlayerNetState? Predicted(int playerIndex) => null;

        public CPlayer.TFPlayerNetState? Networked(int playerIndex) => States.GetValueOrDefault(playerIndex);
    }

    private sealed class FakeStandalones : CModule.IClientStandalones
    {
        public readonly Dictionary<int, CClient.ClientStandaloneEntity> Entities = [];

        public IReadOnlyList<CClient.ClientStandaloneEntity> Live() => [.. Entities.Values];

        public CClient.ClientStandaloneEntity? GetByIndex(int entityIndex) => Entities.GetValueOrDefault(entityIndex);
    }

    /// <summary>The sound controller, recording.</summary>
    private sealed class SoundSpy : CInter.IClientSoundController
    {
        public readonly List<int> Created = [], Destroyed = [];
        public readonly List<(int patch, float volume)> Played = [];

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
