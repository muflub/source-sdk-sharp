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
using TF2.Shared.Player;

namespace TF2.Client.Weapons;

/// <summary>
/// The flamethrower's fire, heard and seen: <c>CTFFlameThrower</c>'s client
/// half (shared/tf/tf_weapon_flamethrower.cpp) — <c>OnDataChanged</c>'s gate
/// (:2183-2285) run every frame over the carrier the draw effects hand it, the
/// pilot light's loop while the gun idles (<c>StartPilotLight</c> :2489, the
/// sound stock leaves with the model's own flame), the flame particle on the
/// muzzle and the start/loop crossfade while it burns (<c>StartFlame</c>
/// :2332-2426), the poof when it stops (<c>StopFlame</c> :2445), and the
/// airblast's puff and boom in the SECONDARY state (:2334).
///
/// </summary>
// The plain mark: the draw effects are collected by ResolveAll (the medigun
// holds the IWeaponDrawEffect claim; a second claimant is a startup error).
[SideService(Side.Client)]
public sealed class TFFlameThrowerFx(IEngineEffects effects, IEngineEntityRender render, ITFPlayerData data,
                                    IClientStandalones standalones, TFClientPlayerResources resources,
                                    IClientSoundController sounds, IEngineSound engineSound,
                                    IWeaponViewModelBearer bearers, TFWeaponScripts scripts)
    : IWeaponDrawEffect
{
    public const int ChannelStatic = 6, SoundLevelNorm = 75;

    /// <summary>The start/loop crossfade (StartFlame :2347).</summary>
    public const float Crossfade = 3.5f;

    /// <summary>The flame particles, by team and crit (GetNewFlameEffectInternal :283-310).</summary>
    public const string FlameParticle = "new_flame",
                        FlameCritRed = "new_flame_crit_red",
                        FlameCritBlue = "new_flame_crit_blue";

    /// <summary>The airblast's puff (StartFlame :2336).</summary>
    public const string BlastParticle = "pyro_blast";

    /// <summary>The muzzle attachment (FlameEffect_t::StartEffects :2712).</summary>
    public const string MuzzleAttachment = "muzzle";

    /// <summary>Which of the gun's sounds an arm asks: the script categories of <c>StartFlame</c>/<c>StartPilotLight</c>/<c>StopFlame</c>.</summary>
    public enum Arm
    {
        Pilot,
        Start,
        Loop,
        Stop,
        Blast,
    }

    /// <summary>The arm's category: the pilot's loop (:2496), the start (:2370), the crit's burst over the plain loop (:2380-2388), the poof (:2451), the blast (:2338).</summary>
    internal static WeaponScriptSound? CategoryOf(Arm arm, bool crit = false) => arm switch
    {
        Arm.Pilot => WeaponScriptSound.Special2,
        Arm.Start => WeaponScriptSound.Single,
        Arm.Loop => crit ? WeaponScriptSound.Burst : WeaponScriptSound.Special1,
        Arm.Stop => WeaponScriptSound.Special3,
        Arm.Blast => WeaponScriptSound.Double,
        _ => null,
    };

    /// <summary>One gun's running effects, keyed by the weapon.</summary>
    public sealed class Gun
    {
        public string? Flame;
        public int StartPatch;
        public int LoopPatch;
        public bool LoopCrit;
        public int PilotPatch;
    }

    private readonly Dictionary<EntityHandle, Gun> _guns = [];

    public string WeaponName => "tf_weapon_flamethrower";

    public bool TwoPass => false;

    /// <summary>The flame is a particle system; nothing draws in the viewmodel pass.</summary>
    public void ViewModelDrawn(in WeaponDrawContext context) { }

    /// <summary>The flame is a particle system; the model draws.</summary>
    public int DrawModel(in WeaponDrawContext context, int flags) => -1;

    /// <summary>A gun's state, for the facts.</summary>
    internal Gun? GunOf(EntityHandle weapon) => _guns.GetValueOrDefault(weapon);

    /// <summary>
    /// The frame: OnDataChanged's gate, every frame. The carrier gone or dead,
    /// the pool empty — everything stops (:2212-2218); the idle gun burns its
    /// pilot light (:2207-2210); a burning state plays the flame (:2200-2205),
    /// the SECONDARY arm its blast (:2202, every carrier — the local gun's
    /// blast rides the same state edge rather than a network update).
    /// </summary>
    public void Think(in WeaponDrawContext context)
    {
        EntityHandle weapon = context.Weapon.Handle;
        Gun gun = GunFor(weapon);
        int owner = context.Owner;

        bool gate = data.Networked(owner) is not null && Alive(owner) && context.State.Reserve > 0;
        var state = TFFlamethrowerBehaviour.StateOf(context.State);
        bool crit = TFFlamethrowerBehaviour.CritLatched(context.State);

        if (!gate)
        {
            StopFlame(gun, weapon);
            StopPilot(gun, weapon);
            return;
        }

        if (state == TFFlamethrowerBehaviour.FlameState.Idle)
            StartPilot(gun, weapon);
        else
            StartFlame(gun, owner, weapon, state, crit, BearerOf(in context));
    }

    /// <summary>
    /// The entity this frame's flame hangs off: the viewmodel attachment's
    /// muzzle in the first person, the carried gun's own model in the world
    /// pass — stock's bearer is the weapon the viewer sees
    /// (<c>GetWeaponForEffect</c>, the two arms of
    /// <c>GetAppropriateWorldOrViewModel</c>).
    /// </summary>
    private EntityHandle BearerOf(in WeaponDrawContext context)
        => context.FirstPerson && bearers.ViewModelBearer() is { } viewmodel ? viewmodel : context.Weapon.Handle;

    /// <summary>
    /// StartFlame. The SECONDARY arm is the blast (:2334-2342), taken once per
    /// burn — the state's edge, where stock re-runs it at every network update.
    /// The burning arms: the crit flip rebuilds the loop with no crossfade
    /// (:2349-2356), the pilot goes (:2358), and a gun with no sound yet takes
    /// the particle, the start sound fading out and the loop fading in over
    /// <see cref="Crossfade"/> (:2360-2400).
    /// </summary>
    private void StartFlame(Gun gun, int owner, EntityHandle weapon, TFFlamethrowerBehaviour.FlameState state, bool crit,
                            EntityHandle bearer)
    {
        if (state == TFFlamethrowerBehaviour.FlameState.Secondary)
        {
            if (gun.Flame == BlastParticle)
                return;

            StopFlame(gun, weapon);

            if (effects.ParticleSystemStart(weapon, BlastParticle))
            {
                effects.ParticleSystemFollowEntity(weapon, 0, bearer, render.LookupAttachment(bearer, MuzzleAttachment));
                gun.Flame = BlastParticle;
            }

            Emit(weapon, CategoryOf(Arm.Blast));
            return;
        }

        bool abrupt = gun.LoopPatch != 0 && gun.LoopCrit != crit;
        if (abrupt)
        {
            Kill(ref gun.StartPatch);
            Kill(ref gun.LoopPatch);
        }

        StopPilot(gun, weapon);

        if (gun.StartPatch == 0 && gun.LoopPatch == 0)
        {
            StartFlameParticle(gun, owner, weapon, crit, bearer);

            if (!abrupt)
            {
                // The start sound, fading out (:2371-2377).
                gun.StartPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, SoundName(CategoryOf(Arm.Start)), SoundLevelNorm);
                sounds.Create(gun.StartPatch);
                sounds.Play(gun.StartPatch, 1f, 100f, 0f);
                sounds.ChangeVolume(gun.StartPatch, 0f, Crossfade);
            }

            // The loop, fading in — or full, after the flip (:2380-2400).
            gun.LoopPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic,
                SoundName(CategoryOf(Arm.Loop, crit)), SoundLevelNorm);
            sounds.Create(gun.LoopPatch);
            if (abrupt)
                sounds.Play(gun.LoopPatch, 1f, 100f, 0f);
            else
            {
                sounds.Play(gun.LoopPatch, 0f, 100f, 0f);
                sounds.ChangeVolume(gun.LoopPatch, 1f, Crossfade);
            }

            gun.LoopCrit = crit;
        }
    }

    /// <summary>RestartParticleEffect's particle (:2531-2545): the name the carrier's team and crit burn, the muzzle it rides.</summary>
    private void StartFlameParticle(Gun gun, int owner, EntityHandle weapon, bool crit, EntityHandle bearer)
    {
        string name = ParticleName(Team(owner), crit);

        // The particle restarts when the name changes — the crit flip
        // (PrimaryAttack :850-855) and the water crossings (:790-798) ride the
        // same compare; the underwater name waits for the client's water read.
        if (gun.Flame == name)
            return;

        if (gun.Flame is not null)
            effects.ParticleSystemStop(weapon);

        if (!effects.ParticleSystemStart(weapon, name))
        {
            gun.Flame = null;
            return;
        }

        effects.ParticleSystemFollowEntity(weapon, 0, bearer, render.LookupAttachment(bearer, MuzzleAttachment));
        gun.Flame = name;
    }

    /// <summary>StopFlame: the poof (:2447-2453), the patches, the particle (:2467).</summary>
    private void StopFlame(Gun gun, EntityHandle weapon)
    {
        if (gun.StartPatch != 0 || gun.LoopPatch != 0)
            engineSound.EntityEmitSound(weapon, SoundName(CategoryOf(Arm.Stop)));

        Kill(ref gun.StartPatch);
        Kill(ref gun.LoopPatch);

        if (gun.Flame is not null)
        {
            effects.ParticleSystemStop(weapon);
            gun.Flame = null;
        }
    }

    /// <summary>StartPilotLight (:2489-2504): the flame stops first (:2493), then the loop.</summary>
    private void StartPilot(Gun gun, EntityHandle weapon)
    {
        if (gun.PilotPatch != 0)
            return;

        StopFlame(gun, weapon);

        gun.PilotPatch = engineSound.SoundPatchCreate(weapon, ChannelStatic, SoundName(CategoryOf(Arm.Pilot)), SoundLevelNorm);
        sounds.Create(gun.PilotPatch);
        sounds.Play(gun.PilotPatch, 1f, 100f, 0f);
    }

    /// <summary>StopPilotLight (:2509-2516).</summary>
    private void StopPilot(Gun gun, EntityHandle weapon)
    {
        if (gun.PilotPatch == 0)
            return;

        sounds.Destroy(gun.PilotPatch);
        engineSound.SoundPatchFree(gun.PilotPatch);
        gun.PilotPatch = 0;
    }

    /// <summary>DestroyEffects and the dead arm: everything the gun held goes.</summary>
    public void Forget(EntityHandle weapon)
    {
        if (!_guns.Remove(weapon, out Gun? gun))
            return;

        StopFlame(gun, weapon);
        StopPilot(gun, weapon);
    }

    private Gun GunFor(EntityHandle weapon)
    {
        if (!_guns.TryGetValue(weapon, out Gun? gun))
            _guns[weapon] = gun = new Gun();
        return gun;
    }

    private void Kill(ref int patch)
    {
        if (patch == 0)
            return;

        sounds.Destroy(patch);
        engineSound.SoundPatchFree(patch);
        patch = 0;
    }

    private void Emit(EntityHandle weapon, WeaponScriptSound? category) =>
        engineSound.EntityEmitSound(weapon, SoundName(category));

    /// <summary>The script sound a category names; null is stock's silence.</summary>
    private string SoundName(WeaponScriptSound? category) => category is { } kind ? scripts.Get(WeaponName)?.Sound(kind) ?? "" : "";

    /// <summary>UpdateEffects' owner gate: the carrier is there and alive.</summary>
    private bool Alive(int playerIndex)
        => standalones.GetByIndex(playerIndex) is ClientSsPlayer player && player.State.Alive;

    /// <summary>The carrier's team from the scoreboard.</summary>
    private int Team(int playerIndex)
        => playerIndex > 0 && playerIndex < TFPlayerResourceNetState.Slots
            ? resources.Live?.State.Team[playerIndex] ?? 0
            : 0;

    /// <summary>GetNewFlameEffectInternal's stock names, no attribute overrides yet (:283-310).</summary>
    internal static string ParticleName(int team, bool crit)
        => team == (int)TFTeam.Red
            ? crit ? FlameCritRed : FlameParticle
            : crit ? FlameCritBlue : FlameParticle;
}
