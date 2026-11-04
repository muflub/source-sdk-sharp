//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

using SourceSharp.Shared;

using TF2.Shared;
using TF2.Shared.Combat;
using TF2.Shared.Econ;
using TF2.Server.Combat;
using TF2.Server.Weapons;

namespace TF2.Server.Projectiles;

/// <summary>
/// The projectile arm of the weapon effects (<c>ITFWeaponEffects.Projectile</c>
/// and <c>.Detonate</c>, Stage 2.8): what stock's launcher
/// <c>PrimaryAttack</c>s do after the shell —
/// <c>CTFWeaponBaseGun::FireProjectile</c> and the rocket/pipebomb/syringe
/// <c>Create</c>s, and <c>CTFPipebombLauncher::SecondaryAttack</c>'s
/// <c>DetonateStickybombs</c> (<c>tf_weapon_pipebomblauncher.cpp:428</c>).
///
/// <para>
/// Which projectile a mode fires is the script's
/// <see cref="TFWeaponModeData.Projectile"/>; the speed is the mode's
/// <see cref="TFWeaponModeData.ProjectileSpeed"/> except the sticky's, which
/// is the charge remapped over <see cref="TFProjectileDefs.StickySpeed"/>.
/// </para>
///
/// <para>
/// Each launcher fires from its own muzzle: the offsets below follow stock's
/// <c>FireRocket</c>/<c>FireNail</c>/<c>FirePipeBomb</c> (the pipes' shared
/// launch also carries stock's <c>up * 200</c> tilt and ±10 jitter,
/// <c>tf_weaponbase_gun.cpp:687-688</c>), and the launch takes its firing
/// item's attributes as stock's <c>FireNail</c>/<c>FirePipeBomb</c> do: the
/// syringe's ±1.5° cone, the pipes' <c>projectile_spread_angle</c> cone,
/// their <c>mult_projectile_range</c> speed and <c>grenade_no_spin</c>. Not
/// here (docs/divergences.md): stock's eye→muzzle trace that pulls the born
/// projectile out of a wall and the <c>startsolid</c> refuse-to-fire that
/// guards it — the seam's <see cref="TraceResult"/> carries no
/// <c>StartSolid</c>, the same gap as the pipe's own physics arm (deferred
/// 2.8); and the <c>IsViewModelFlipped</c> mirror of the pipes' right offset,
/// a per-view client choice.
/// </para>
/// </summary>
[SideService(Side.Server, As = [typeof(ITFProjectileEffects)])]
public sealed class TFProjectileEffects(IEngineEntities entities, IEntityDirectory directory,
                                        IEnginePlayers players, TFStickyTray tray, ILog log,
                                        ITFWeaponOwners owners, IUniformRandom random,
                                        AttributeHooks attributes, ITempEnts temps,
                                        IEnginePrecache precache, IEngineEntityRender render,
                                        TFStickybombConVars stickyConVars)
    : ITFProjectileEffects, IOrderedHook
{
    /// <summary>Stage 2.8's band (+800..+899 of TF2's thousand).</summary>
    public const int Order = 1_000_000 - 1_000 + 800;

    /// <summary>The rocket specialist's launch-speed cap (<c>tf_weaponbase_rocket.cpp:296</c>).</summary>
    public const float SpecialistSpeedCap = 3000f;

    /// <summary>The needle's client lifetime, in seconds (<c>tf_projectile_base.cpp:220</c>'s <c>// Lifetime</c>).</summary>
    private const int SyringeLifetimeSeconds = 6;

    /// <summary>Stock's named syringe effect (<c>tf_projectile_nail.cpp:29</c>).</summary>
    private const string SyringeDispatchEffect = "ClientProjectile_Syringe";

    public int HookOrder => Order;

    public void Projectile(in WeaponAttackContext attack, TFWeaponScript script, ShotDamage shot, float charge)
    {
        var mode = script.Primary;
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        Vector eyes = entities.GetEyeAngles(owner);
        Vector forward = Angles.Forward(eyes);
        (Vector right, Vector up) = Angles.RightUp(forward);

        // The script may carry a speed; stock's launchers keep theirs as C++
        // constants (1100 the rocket, 1200 the pipe, 1000 the syringe), which
        // is the fallback when the script says none.
        float scriptSpeed = mode.ProjectileSpeed;

        switch (mode.Projectile)
        {
            case TFProjectile.Rocket:
                {
                    // FireRocket's vecOffset, folded to the ducked height when
                    // the shooter is crouched (tf_weaponbase_gun.cpp:521-525).
                    float muzzleUp = owners.DuckingOf(attack.Command.OwnerIndex)
                        ? TFProjectileDefs.RocketMuzzleUpDucked
                        : TFProjectileDefs.RocketMuzzleUp;
                    if (Born(TFProjectileEntityNames.Rocket, owner, forward, right, up,
                             TFProjectileDefs.RocketMuzzleForward, TFProjectileDefs.RocketMuzzleRight,
                             muzzleUp) is not TFRocket rocket)
                        return;

                    // Rocket Specialist (tf_weaponbase_rocket.cpp:279-298): the
                    // owner's level, remapped 1..4 -> 1.15..1.6 onto the launch
                    // speed and capped at 3000. Stock reads the attribute on
                    // the owner entity, a sentry's rocket asking its builder —
                    // the rockets this fires are always a player's.
                    float speed = Speed(scriptSpeed, TFProjectileDefs.RocketSpeed);
                    int specialist = attributes.Int(0, "rocket_specialist",
                                                    owners.ItemOf(attack.Command.OwnerIndex, script.Name));
                    if (specialist != 0)
                        speed = MathF.Min(speed * TFDamageModifyRules.RemapValClamped(specialist, 1f, 4f, 1.15f, 1.6f),
                                          SpecialistSpeedCap);

                    rocket.ArmedBy(mode.Damage, script.Id, script.Name, shot.Critical, attack.Command.OwnerIndex);
                    rocket.FiredBy(owner, forward * speed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Rocket} created by {owner}"
                          + $" at {Speed(scriptSpeed, TFProjectileDefs.RocketSpeed):F0} u/s\n");
                    break;
                }

            case TFProjectile.Pipebomb:
                {
                    // FirePipeBomb's arms, read off the firing item: the cone
                    // drawn onto the eye angles, and the speed through the
                    // mult_projectile_range hook (tf_weaponbase_gun.cpp:652-686).
                    IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    (forward, right, up) = SpreadBasis(eyes, item);
                    float speed = attributes.Float(Speed(scriptSpeed, TFProjectileDefs.PipeSpeed),
                                                   "mult_projectile_range", item);
                    if (Born(TFProjectileEntityNames.Pipebomb, owner, forward, right, up,
                             TFProjectileDefs.PipeMuzzleForward, TFProjectileDefs.PipeMuzzleRight,
                             TFProjectileDefs.PipeMuzzleUp) is not TFPipebomb pipe)
                        return;
                    pipe.ThrownBy(owner, Lob(forward, speed, right, up),
                                  mode.Damage, script.DamageRadius, script.Id, script.Name, shot.Critical);
                    Spin(pipe, item);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} created by {owner}\n");
                    break;
                }

            case TFProjectile.PipebombRemote:
                {
                    IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    (forward, right, up) = SpreadBasis(eyes, item);
                    float speed = attributes.Float(TFProjectileDefs.StickySpeed(charge),
                                                   "mult_projectile_range", item);
                    if (Born(TFProjectileEntityNames.Stickybomb, owner, forward, right, up,
                             TFProjectileDefs.PipeMuzzleForward, TFProjectileDefs.PipeMuzzleRight,
                             TFProjectileDefs.PipeMuzzleUp) is not TFStickybomb sticky)
                        return;

                    // GetLiveTime (tf_weapon_grenade_pipebomb.cpp:1186-1190): the arm
                    // time is tf_grenadelauncher_livetime through the launcher's
                    // sticky_arm_time hook; the arm think books at the total (:1144).
                    sticky.SetArmTime(attributes.Float(stickyConVars.LiveTime.Value,
                                                       "sticky_arm_time", item));

                    sticky.ThrownBy(owner, Lob(forward, speed, right, up),
                                    mode.Damage, script.DamageRadius, script.Id, script.Name, shot.Critical);
                    Spin(sticky, item);

                    // FireProjectile's cap (tf_weapon_pipebomblauncher.cpp:381-392):
                    // TF_WEAPON_PIPEBOMB_COUNT (tf_shareddefs.h:391) through the
                    // launcher's add_max_pipebombs hook; past the cap the oldest goes.
                    tray.Register(attack.Command.OwnerIndex, sticky,
                                  attributes.Int(TFProjectileDefs.MaxStickies, "add_max_pipebombs", item));
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Stickybomb} created by {owner}"
                          + $" ({tray.Count(attack.Command.OwnerIndex)} out)\n");
                    break;
                }

            case TFProjectile.Syringe:
                {
                    if (Born(TFProjectileEntityNames.Syringe, owner, forward, right, up,
                             TFProjectileDefs.SyringeMuzzleForward, TFProjectileDefs.SyringeMuzzleRight,
                             TFProjectileDefs.SyringeMuzzleUp) is not TFSyringe syringe)
                        return;
                    syringe.ArmedBy(script.Id, script.Name, shot.Critical);
                    // FireNail throws each syringe inside a ±1.5° cone drawn on
                    // the eye angles' pitch and yaw after the muzzle placement
                    // (tf_weaponbase_gun.cpp:611-623); GetProjectileSpread
                    // stands at 0 for the syringe gun.
                    Vector cone = new(
                        eyes.X + random.RandomFloat(-TFProjectileDefs.SyringeSpread, TFProjectileDefs.SyringeSpread),
                        eyes.Y + random.RandomFloat(-TFProjectileDefs.SyringeSpread, TFProjectileDefs.SyringeSpread),
                        eyes.Z);
                    Vector launch = Angles.Forward(cone) * Speed(scriptSpeed, TFProjectileDefs.SyringeSpeed);
                    syringe.FiredBy(owner, launch);

                    // Stock hides the server entity (tf_projectile_base.cpp:172-173's GAME_DLL arm,
                    // AddEffects(EF_NODRAW)) and hands the needle to the named client effect
                    // (:217-235): the CLIENT tempent is what flies under gravity and sticks where
                    // it hits for its lifetime (ClientsideProjectileCallback :304-370, the
                    // ATTACHTOTARGET stop arm of the legacy tempents' collision block).
                    // render.SetVisible writes EF_NODRAW into the effects word, the observer's own door.
                    render.SetVisible(syringe.Handle, false);
                    Vector at = entities.GetOrigin(syringe.Handle);
                    var needle = new TeEffectData
                    {
                        Origin = at,
                        Start = launch,
                        Flags = SyringeLifetimeSeconds,   // stock's :220 "// Lifetime"
                        // :229 reads the born entity's model index; this seam's ask of the
                        // precached syringe model answers the same number.
                        Material = precache.PrecacheModel(TFProjectileDefs.SyringeModel),
                        EntIndex = entities.GetIndex(owner), // :230 m_nEntIndex = pOwner->entindex()
                    };
                    if (shot.Critical)
                        needle.DamageType = (int)TFDamageBits.Critical; // :222-225, the crit trail's bit
                    // Stock's filter is PAS of the origin and the trace's endpos, broadcast across a water crossing
                    // (:183-215); the seam answers the PVS of the spawn point — the stun's shockwave's door.
                    // The cull bit stays off this dispatch: stock shows the shooter its own needle through the
                    // predicted local dispatch (tf_projectile_base.cpp:231-233, the client arm), and this client
                    // has only the networked one, so the shooter must survive SuppressTE to see the needle at all
                    // (EffectsServer.cpp:62-65 keeps the host in a filter that ignores the prediction cull).
                    temps.DispatchEffect(TeFilters.Pvs(at) with { IgnorePredictionCull = true },
                                         0f, SyringeDispatchEffect, in needle);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Syringe} created by {owner}\n");
                    break;
                }

            case TFProjectile.Arrow:
                {
                    if (Born(TFProjectileEntityNames.Arrow, owner, forward, right, up,
                             TFProjectileDefs.SyringeMuzzleForward, TFProjectileDefs.SyringeMuzzleRight,
                             TFProjectileDefs.SyringeMuzzleUp) is not TFArrow arrow)
                        return;

                    // The bow hands its alight flag to the arrow and loses it
                    // (LaunchGrenade's copy and clear, tf_weapon_compound_bow.cpp:125, :169).
                    bool lit = attack.Weapon.ArrowAlight;
                    attack.Weapon.ArrowAlight = false;

                    arrow.ArmedBy(script.Id, script.Name, shot.Critical, lit,
                                  TFProjectileDefs.ArrowDamage(mode.Damage, charge),
                                  TFProjectileDefs.ArrowGravity(charge));
                    arrow.FiredBy(owner, forward * TFProjectileDefs.ArrowSpeed(charge));
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Arrow} created by {owner}\n");
                    break;
                }

            default:
                return;
        }
    }

    /// <summary>
    /// The pipe's and the sticky's shared launch vector
    /// (<c>FirePipeBomb</c>'s <c>vecVelocity</c>,
    /// <c>tf_weaponbase_gun.cpp:687-688</c>): forward at the launched speed,
    /// <see cref="TFProjectileDefs.PipeLaunchUp"/> up, and two draws from the
    /// engine stream jittering the right and up axes.
    /// </summary>
    private Vector Lob(Vector forward, float speed, Vector right, Vector up)
        => forward * speed
         + up * TFProjectileDefs.PipeLaunchUp
         + right * random.RandomFloat(-TFProjectileDefs.PipeLaunchJitter, TFProjectileDefs.PipeLaunchJitter)
         + up * random.RandomFloat(-TFProjectileDefs.PipeLaunchJitter, TFProjectileDefs.PipeLaunchJitter);

    /// <summary>
    /// The pipes' firing basis: <c>FirePipeBomb</c> adds a
    /// <c>RandomAngle</c> over the item's <c>projectile_spread_angle</c> to
    /// the eye angles — all three axes drawn, the roll discarded — and takes
    /// the basis from those angles (<c>tf_weaponbase_gun.cpp:652-665</c>).
    /// With no attribute the eye basis stands, undrawn.
    /// </summary>
    private (Vector forward, Vector right, Vector up) SpreadBasis(Vector eyes, IAttributeProvider? item)
    {
        float spreadAngle = attributes.Float(0f, "projectile_spread_angle", item);
        if (spreadAngle > 0f)
        {
            eyes = new Vector(eyes.X + random.RandomFloat(-spreadAngle, spreadAngle),
                              eyes.Y + random.RandomFloat(-spreadAngle, spreadAngle),
                              eyes.Z);
            random.RandomFloat(-spreadAngle, spreadAngle); // RandomAngle's third draw, zeroed at :659
        }

        Vector launch = Angles.Forward(eyes);
        (Vector right, Vector up) = Angles.RightUp(launch);
        return (launch, right, up);
    }

    /// <summary>
    /// The pipes' shared spin: <c>Create</c>'s
    /// <c>ApplyLocalAngularVelocityImpulse</c>
    /// (<c>baseentity_shared.cpp:2555</c>) of <c>FirePipeBomb</c>'s
    /// <c>angImpulse</c> — the impulse swizzles into an angular velocity
    /// (<c>vector.h:2198</c>), so the bomb rolls at 600°/s and tumbles in
    /// pitch, added to what it already spins at (none at birth). Loch-n-load's
    /// <c>grenade_no_spin</c> (<c>tf_weaponbase_gun.cpp:694-699</c>) zeroes it.
    /// Stock's vphysics arm (<c>AddVelocity(NULL, &amp;impulse)</c>) waits on
    /// the pipe's physics (deferred 2.8); the bounce already spins on this
    /// seam (<c>TFStickybomb.OnPhysicsUpdate</c>).
    /// </summary>
    private void Spin(ScriptedEntity bomb, IAttributeProvider? item)
    {
        var impulse = new Vector(random.RandomInt(-TFProjectileDefs.PipeSpinPitchMax, TFProjectileDefs.PipeSpinPitchMax),
                                 0f, TFProjectileDefs.PipeSpinRoll);
        if (attributes.Int(0, "grenade_no_spin", item) != 0)
            impulse = default;
        entities.SetAngularVelocity(bomb.Handle, entities.GetAngularVelocity(bomb.Handle) + impulse);
    }

    /// <summary>
    /// A projectile is born at its launcher's own muzzle — the offset along
    /// the firing basis that stock's <c>FireRocket</c>, <c>FireNail</c> and
    /// <c>FirePipeBomb</c> carry — and is brought to life before its managed
    /// half is asked for: the registry binds the instance when the engine
    /// spawns the shell (the <c>env_fire</c> flame's order), so an entity that
    /// was created but never spawned has no managed half to arm.
    ///
    /// <para>
    /// The offset is measured from the eye. Stock measures it from
    /// <c>Weapon_ShootPosition</c> (<c>GetAbsOrigin + forward * 32</c>,
    /// <c>player.cpp:5129</c>, <c>basecombatcharacter.cpp:2793</c>) and births
    /// the projectile at the end of a trace from the eye to that point — the
    /// trace is the decision about which of the two the projectile really
    /// stands at, and until its seam exists (the gap named in the class
    /// comment) the eye-anchored placement stands, as it did under the one
    /// fixed offset before.
    /// </para>
    /// </summary>
    private ScriptedEntity? Born(string className, EntityHandle owner, Vector forward,
                                 Vector right, Vector up, float forwardOffset, float rightOffset,
                                 float upOffset)
    {
        EntityHandle handle = entities.Create(className);
        if (!entities.IsValid(handle))
            return null;

        entities.SetOrigin(handle, entities.GetEyePosition(owner)
            + forward * forwardOffset + right * rightOffset + up * upOffset);
        entities.Spawn(handle);
        return directory.Get(handle);
    }

    /// <summary>The script's speed, or stock's when the script names none.</summary>
    private static float Speed(float scriptSpeed, float stock) => TFProjectileDefs.Speed(scriptSpeed, stock);

    /// <summary><c>TF_DETONATE_MODE_DOT</c> (<c>tf_weapon_pipebomblauncher.h:24</c>).</summary>
    private const int DetonateModeDot = 1;

    /// <summary>The crosshair cone (<c>tf_weapon_pipebomblauncher.cpp:605</c>).</summary>
    private const float AimedDot = 0.975f;

    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script)
    {
        // CanDestroyStickies (tf_weapon_pipebomblauncher.h:80): the launcher's
        // stickies_detonate_stickies arms each bomb's sphere sweep, before
        // its own Detonate (tf_weapon_pipebomblauncher.cpp:561-567).
        IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
        bool destroyStickies = attributes.Int(0, "stickies_detonate_stickies", item) == 1;

        // GetDetonateMode (tf_weapon_grenadelauncher.cpp:481-486): the set_detonate_mode
        // attribute picks the arm of DetonateRemotePipebombs (:523-572). At mode 1
        // (TF_DETONATE_MODE_DOT) only the stickies in view go — ModifyPipebombsInView
        // (:577-638); any other mode (TF_DETONATE_MODE_STANDARD 0, stock's unset
        // default) fires the whole tray (:530-571).
        int mode = attributes.Int(0, "set_detonate_mode", item);
        if (mode == DetonateModeDot)
        {
            EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
            Vector eyePosition = entities.GetEyePosition(owner);
            Vector forward = Angles.Forward(entities.GetEyeAngles(owner));
            Vector playerOrigin = entities.GetOrigin(owner);
            tray.DetonateAll(attack.Command.OwnerIndex, destroyStickies,
                             sticky => InView(entities, eyePosition, forward, playerOrigin, sticky));
            return;
        }

        tray.DetonateAll(attack.Command.OwnerIndex, destroyStickies);
    }

    /// <summary>
    /// ModifyPipebombsInView's in-view test (<c>tf_weapon_pipebomblauncher.cpp:592-605</c>):
    /// the direction from the eyes to the bomb's centre dotting over <see cref="AimedDot"/>
    /// with the eyes' forward, or the bomb standing within its damage radius of the
    /// player — stock's sticky-jumping arm. The distance is the player's origin to the
    /// bomb's origin; only armed bombs pass the tray's own gate beside this test.
    /// </summary>
    private static bool InView(IEngineEntities entities, Vector eyePosition, Vector forward,
                               Vector playerOrigin, TFStickybomb sticky)
    {
        Vector towards = entities.GetWorldSpaceCenter(sticky.Handle) - eyePosition;
        float dot = towards.Normalized.Dot(forward);
        float distance = (entities.GetOrigin(sticky.Handle) - playerOrigin).Length;
        return dot > AimedDot || distance < sticky.GetDamageRadius();
    }
}
