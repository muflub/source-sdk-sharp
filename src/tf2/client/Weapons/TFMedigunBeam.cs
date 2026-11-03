//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Client.WeaponEffects;
using SourceSharp.Shared;

using TF2.Client.Player;
using TF2.Shared;
using TF2.Shared.Conditions;
using TF2.Shared.Player;

namespace TF2.Client.Weapons;

/// <summary>
/// The medigun beam a player sees: <c>CWeaponMedigun</c>'s client half
/// (shared/tf/tf_weapon_medigun.cpp) — <c>UpdateEffects</c>' beam from the
/// gun's muzzle to the patient's origin 50 units up, <c>ClientThink</c>'s
/// heal loop and no-target buzz, <c>StopHealSound</c>'s detach, and
/// <c>ManageChargeEffect</c>'s full-charge glow and sound.
///
/// <para>
/// THE BEAM. Stock re-runs <c>UpdateEffects</c> on every wire change and
/// names the system there — by the MEDIC's team, <c>_invun</c> while the
/// charge releases — so this frames the medigun once a frame (the draw
/// effects' <c>Think</c>) and restarts the system when the name or the
/// patient changed. Point 0 rides the weapon's <c>muzzle</c> attachment
/// (stock's <c>PATTACH_POINT_FOLLOW "muzzle"</c>:2467), point 1 the patient
/// (<c>PATTACH_ABSORIGIN_FOLLOW</c> + (0,0,50):2468).
/// </para>
///
/// <para>
/// THE SOUNDS. Stock's <c>ClientThink</c> makes the heal loop the frame
/// <c>m_bHealing</c> first reads true (:2317) and <c>StopHealSound</c> plays
/// the detach as it destroys the loop (:2042); the charged glow's loop is
/// <c>ManageChargeEffect</c>'s (:2174). The no-target buzz is <c>ClientThink</c>'s
/// every-half-second arm (:2237), which stock gates on <c>CanAttack()</c> —
/// a gate the client's shell has no answer for, so the buzz asks only the
/// button.
/// </para>
///
/// <para>
/// Divergences recorded: the local player's beam rides the carried world
/// model (stock rides the rendered weapon model, the viewmodel); the
/// charged glow rides the weapon's origin rather than its muzzle (the extra
/// system takes no attachment); the <c>_targeted</c> beam variant (behind
/// <c>hud_medichealtargetmarker</c>, default 0) and <c>_machinery</c>
/// (buildings as patients) have no arm; the fully-invisible refusal
/// (IsFullyInvisible) has no arm.
/// </para>
/// </summary>
[SideService(Side.Client, As = [typeof(IWeaponDrawEffect)])]
public sealed class TFMedigunBeam(IEngineEffects effects, IEngineEntityRender render, ITFPlayerData data,
                                  IClientStandalones standalones, TFClientPlayerResources resources,
                                  IClientSoundController sounds, IEngineSound engineSound, IEngineHud screen)
    : IWeaponDrawEffect
{
    /// <summary>CHAN_STATIC, and ATTN_NORM as a sound level (ATTN_TO_SNDLVL).</summary>
    public const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>The beam systems, by the medic's team (UpdateEffects:2425-2459).</summary>
    public const string BeamRed = "medicgun_beam_red", BeamBlue = "medicgun_beam_blue";

    /// <summary>The release variants: stock's suffix on the same name.</summary>
    public const string BeamRedInvun = "medicgun_beam_red_invun", BeamBlueInvun = "medicgun_beam_blue_invun";

    /// <summary>The full-charge glows (ManageChargeEffect:2159-2167).</summary>
    public const string FullChargeRed = "medicgun_invulnstatus_fullcharge_red",
                        FullChargeBlue = "medicgun_invulnstatus_fullcharge_blue";

    /// <summary>g_pszMedigunHealSounds[0] and GetHealSound's local-player arms (:1113).</summary>
    public const string HealSoundWorld = "WeaponMedigun.HealingWorld", HealSoundHealer = "WeaponMedigun.HealingHealer",
                        HealSoundTarget = "WeaponMedigun.HealingTarget";

    /// <summary>GetDetachSound's three (:1143).</summary>
    public const string DetachSoundWorld = "WeaponMedigun.HealingDetachWorld",
                        DetachSoundHealer = "WeaponMedigun.HealingDetachHealer",
                        DetachSoundTarget = "WeaponMedigun.HealingDetachTarget";

    /// <summary>The no-target buzz (:2243) and the charged loop (:2180).</summary>
    public const string NoTargetSound = "WeaponMedigun.NoTarget", ChargedSound = "WeaponMedigun.Charged";

    /// <summary>The muzzle attachment name (UpdateEffects:2467).</summary>
    public const string MuzzleAttachment = "muzzle";

    /// <summary>The patient's height offset (UpdateEffects:2468).</summary>
    public static Vector TargetOffset => new(0f, 0f, 50f);

    /// <summary>The buzz's spacing: stock's m_flNextBuzzTime step (:2244).</summary>
    public const float BuzzInterval = 0.5f;

    /// <summary>GetMinChargeAmount for the standard medigun (:809-819).</summary>
    public const float MinChargeToDeploy = 1f;

    /// <summary>One gun's running effects, keyed by the weapon.</summary>
    public sealed class Gun
    {
        public int Owner;
        public string? Beam;
        public int Patient = -1;
        public int LastPatient = -1;
        public int HealPatch;
        public int DetachPatch;
        public bool ChargedGlow;
        public int ChargedPatch;
        public float NextBuzz;
    }

    private readonly Dictionary<EntityHandle, Gun> _guns = [];

    public string WeaponName => "tf_weapon_medigun";

    public bool TwoPass => false;

    /// <summary>The beam is a particle system; nothing draws in the viewmodel pass.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context) { }

    /// <summary>The beam is a particle system; the model draws.</summary>
    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    /// <summary>A gun's state, for the facts.</summary>
    internal Gun? GunOf(EntityHandle weapon) => _guns.GetValueOrDefault(weapon);

    /// <summary>
    /// The frame: the beam, the loops, the buzz. Stock's <c>ClientThink</c>
    /// and <c>UpdateEffects</c>, run once a frame per medigun.
    /// </summary>
    public void Think(in WeaponDrawContext context)
    {
        int owner = context.Owner;
        Gun gun = GunFor(context.Weapon.Handle);
        gun.Owner = owner;
        TFPlayerNetState? carrier = data.Networked(owner);
        EntityHandle weapon = context.Weapon.Handle;

        // ClientThink's refusal (2290-2297) and UpdateEffects' (2406): the
        // medic is gone, dead, not a medic, or names no patient — everything
        // stops. Stock also refuses the fully invisible.
        if (carrier is null || !Alive(owner) || carrier.Class() != TFClass.Medic)
        {
            StopBeam(gun, weapon);
            StopHeal(gun, weapon, detach: false);
            StopCharged(gun, weapon);
            return;
        }

        int patient = carrier.HealTargetIndex;
        bool healing = patient >= 0;
        bool releasing = carrier.ChargeReleasing;
        int team = Team(owner);

        // The beam: re-created whenever its name or its patient changed.
        string name = BeamName(team, releasing);
        if (healing && (gun.Beam != name || gun.Patient != patient))
        {
            if (gun.Beam is not null)
                effects.ParticleSystemStop(weapon);
            if (effects.ParticleSystemStart(weapon, name))
            {
                gun.Beam = name;
                gun.Patient = patient;

                // The muzzle end and the patient's origin 50 up.
                effects.ParticleSystemFollowEntity(weapon, 0, weapon, render.LookupAttachment(weapon, MuzzleAttachment));
                effects.ParticleSystemFollowEntity(weapon, 1, PatientHandle(patient), 0, TargetOffset);
            }
            else
            {
                gun.Beam = null;
                gun.Patient = -1;
            }
        }
        else if (!healing && gun.Beam is not null)
        {
            StopBeam(gun, weapon);
        }

        // The heal loop: created the frame healing starts (ClientThink:2317),
        // destroyed on its end with the detach in its place (StopHealSound).
        if (healing)
        {
            gun.LastPatient = patient;
            // The next heal destroys the last detach (StopHealSound's
            // bStopDetachSound arm, called from the new loop's edge:2331).
            if (gun.DetachPatch != 0)
            {
                sounds.Destroy(gun.DetachPatch);
                engineSound.SoundPatchFree(gun.DetachPatch);
                gun.DetachPatch = 0;
            }
            if (gun.HealPatch == 0)
            {
                gun.HealPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, HealSound(owner, patient), SoundLevelNorm);
                sounds.Create(gun.HealPatch);
                sounds.Play(gun.HealPatch, 1f, 100f, 0f);
            }
        }
        else if (gun.HealPatch != 0)
        {
            StopHeal(gun, weapon, detach: true);
        }

        // The full-charge glow and its loop (ManageChargeEffect:2144-2187).
        // The meter is the weapon's own state (stock's m_flChargeLevel on
        // DT_WeaponMedigun), the replay's while it is fresh.
        bool charged = (context.State.BehaviourMeter >= MinChargeToDeploy || releasing)
                       && !carrier.InCond(TFCond.Taunting);
        if (charged && !gun.ChargedGlow)
        {
            gun.ChargedGlow = effects.ParticleSystemStartExtra(weapon, team == (int)TFTeam.Red ? FullChargeRed : FullChargeBlue);
            if (gun.ChargedGlow)
            {
                gun.ChargedPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, ChargedSound, SoundLevelNorm);
                sounds.Create(gun.ChargedPatch);
                sounds.Play(gun.ChargedPatch, 1f, 100f, 0f);
            }
        }
        else if (!charged && gun.ChargedGlow)
        {
            StopCharged(gun, weapon);
        }

        // The no-target buzz (ClientThink:2237-2246): the local player's gun,
        // trigger held, healing nobody, at most every half second.
        if (!healing && owner == screen.LocalPlayerIndex
                     && (context.State.LastButtons & (int)InputButtons.Attack) != 0
                     && context.Now >= gun.NextBuzz)
        {
            engineSound.EntityEmitSound(weapon, NoTargetSound);
            gun.NextBuzz = context.Now + BuzzInterval;
        }
    }

    /// <summary>StopHealSound and the dead arm: the loop goes, the detach takes its place.</summary>
    private void StopHeal(Gun gun, EntityHandle weapon, bool detach)
    {
        if (gun.HealPatch != 0)
        {
            sounds.Destroy(gun.HealPatch);
            engineSound.SoundPatchFree(gun.HealPatch);
            gun.HealPatch = 0;

            if (detach)
            {
                gun.DetachPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, DetachSound(gun.Owner, gun.LastPatient), SoundLevelNorm);
                sounds.Create(gun.DetachPatch);
                sounds.Play(gun.DetachPatch, 1f, 100f, 0f);
            }
        }
    }

    private void StopBeam(Gun gun, EntityHandle weapon)
    {
        if (gun.Beam is null)
            return;

        effects.ParticleSystemStop(weapon);
        gun.Beam = null;
        gun.Patient = -1;
    }

    private void StopCharged(Gun gun, EntityHandle weapon)
    {
        if (!gun.ChargedGlow)
            return;

        effects.ParticleSystemStopExtra(weapon);
        gun.ChargedGlow = false;
        if (gun.ChargedPatch != 0)
        {
            sounds.Destroy(gun.ChargedPatch);
            engineSound.SoundPatchFree(gun.ChargedPatch);
            gun.ChargedPatch = 0;
        }
    }

    /// <summary>UpdateEffects' owner gates: the entity is there and alive.</summary>
    private bool Alive(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player && player.State.Alive;

    /// <summary>The medic's team from the scoreboard (UpdateEffects:2425).</summary>
    private int Team(int playerIndex)
        => playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? resources.Live?.State.Team[playerIndex] ?? 0
            : 0;

    /// <summary>The patient's handle, 0 when its entity has not arrived (the point then rides the owner feed).</summary>
    private EntityHandle PatientHandle(int patientIndex)
        => standalones.GetByIndex(patientIndex)?.Handle ?? default;

    /// <summary>DestroyEffects and the dead arm: everything the gun held goes.</summary>
    public void Forget(EntityHandle weapon)
    {
        if (!_guns.Remove(weapon, out Gun? gun))
            return;

        StopBeam(gun, weapon);
        StopHeal(gun, weapon, detach: false);
        StopCharged(gun, weapon);
        if (gun.DetachPatch != 0)
        {
            sounds.Destroy(gun.DetachPatch);
            engineSound.SoundPatchFree(gun.DetachPatch);
        }
    }

    private Gun GunFor(EntityHandle weapon)
    {
        if (!_guns.TryGetValue(weapon, out Gun? gun))
            _guns[weapon] = gun = new Gun();
        return gun;
    }

    /// <summary>UpdateEffects' name arms: the medic's team, _invun while releasing.</summary>
    internal static string BeamName(int team, bool releasing)
        => team == (int)TFTeam.Red
            ? (releasing ? BeamRedInvun : BeamRed)
            : (releasing ? BeamBlueInvun : BeamBlue);

    /// <summary>GetHealSound: whose ears the loop is mixed for (:1113-1125).</summary>
    private string HealSound(int healer, int patient)
        => healer == screen.LocalPlayerIndex ? HealSoundHealer
         : patient == screen.LocalPlayerIndex ? HealSoundTarget
         : HealSoundWorld;

    /// <summary>GetDetachSound: the same arms, over the last patient (:1143-1163).</summary>
    internal string DetachSound(int healer, int lastPatient)
        => healer == screen.LocalPlayerIndex ? DetachSoundHealer
         : lastPatient >= 0 && lastPatient == screen.LocalPlayerIndex ? DetachSoundTarget
         : DetachSoundWorld;
}
