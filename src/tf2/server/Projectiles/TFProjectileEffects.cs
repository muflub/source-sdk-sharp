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
public sealed class TFProjectileEffects(IEngineEntities entities, ITFProjectileSpawner spawner,
                                        IEnginePlayers players, TFStickyTray tray, TFFlareTray flareTray,
                                        ILog log, ITFWeaponOwners owners, IUniformRandom random,
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

    /// <summary>
    /// FireProjectile's override (<c>tf_weaponbase_gun.cpp:261</c>): the
    /// item's <c>override_projectile_type</c>, the projectile the launch
    /// fires in place of the weapon type's.
    /// </summary>
    public const string OverrideProjectileAttribute = "override_projectile_type";

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

        // FireProjectile's override (tf_weaponbase_gun.cpp:258-268): the item's
        // override_projectile_type fires in place of the weapon type's — the
        // Stickybomb Jumper's practice bomb where the launcher says remote.
        int overrideProjectile = attributes.Int(0, OverrideProjectileAttribute,
                                                owners.ItemOf(attack.Command.OwnerIndex, script.Name));
        if (overrideProjectile != 0)
            mode = mode with { Projectile = (TFProjectile)overrideProjectile };

        // The Dragon's Fury's ball, keyed by the weapon id: stock's
        // FireProjectile spawns its entity directly, not by the script's
        // ProjectileType (<c>tf_weapon_dragons_fury.cpp:161</c>), so the
        // shipped script's spelling cannot break the launch here.
        if (script.Id == TFWeaponId.FlameBall)
        {
            // FireProjectile's muzzle (:152-159): forward 3, right 7, up −9,
            // the right flipped for the mirrored view — that mirror waits
            // with the view-flip seam, as the pipes'.
            if (Born(TFProjectileEntityNames.BallOfFire, owner, forward, right, up,
                     TFProjectileDefs.BallMuzzleForward, TFProjectileDefs.BallMuzzleRight,
                     TFProjectileDefs.BallMuzzleUp) is not TFBallOfFire ball)
                return;

            // The ball's worth is tf_fireball_damage through the launcher's mult_dmg
            // (tf_projectile_dragons_fury.cpp:86-88), not the script's number.
            float ballDamage = attributes.Float(TFProjectileDefs.BallBaseDamage, "mult_dmg",
                                                owners.ItemOf(attack.Command.OwnerIndex, script.Name));
            ball.ArmedBy(ballDamage, script.Id, script.Name, shot.Critical, attack.Command.OwnerIndex);
            ball.FiredBy(owner, forward * TFProjectileDefs.BallSpeed);
            log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.BallOfFire} created by {owner}\n");
            return;
        }

        // The Wrap Assassin's ornament, keyed by the weapon id for the same
        // reason: CTFBat_Giftwrap::CreateBall spawns its entity directly
        // (tf_weapon_bat.cpp:1084). The lob is GetBallDynamics' own
        // (:240-244): ten forward parts to one up of the sight basis,
        // normalized, at the convar's speed — and the shot is the bat's
        // damage under its mult_dmg, baked at the throw as the Fury's is.
        if (script.Id == TFWeaponId.BatGiftwrap)
        {
            if (Born(TFProjectileEntityNames.BallOrnament, owner, forward, right, up,
                     TFProjectileDefs.OrnamentMuzzleForward, 0f,
                     TFProjectileDefs.OrnamentMuzzleUp) is not TFBallOrnament ball)
                return;

            float ballDamage = attributes.Float(script.Primary.Damage, "mult_dmg",
                                                owners.ItemOf(attack.Command.OwnerIndex, script.Name));
            ball.ArmedBy(ballDamage, script.Id, script.Name, shot.Critical);
            ball.FiredBy(owner, (forward * TFProjectileDefs.OrnamentAimForward
                                 + up * TFProjectileDefs.OrnamentAimUp).Normalized
                               * TFProjectileDefs.OrnamentSpeed);
            log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.BallOrnament} created by {owner}\n");
            return;
        }

        switch (mode.Projectile)
        {
            case TFProjectile.Rocket:
                {
                    // FireRocket's vecOffset, folded to the ducked height when
                    // the shooter is crouched (tf_weaponbase_gun.cpp:521-525).
                    float muzzleUp = owners.DuckingOf(attack.Command.OwnerIndex)
                        ? TFProjectileDefs.RocketMuzzleUpDucked
                        : TFProjectileDefs.RocketMuzzleUp;

                    // GetProjectileFireSetup's centre-fire arm
                    // (tf_weaponbase.cpp:5755-5760): the launcher's item
                    // answering exactly 1 on centerfire_projectile drops the
                    // lateral offset of the shot position; any other answer
                    // keeps it. The item rides the launch itself, so it is
                    // read before the rocket is born; the specialist's remap
                    // below asks the same item.
                    IAttributeProvider? centerItem = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    float muzzleRight = attributes.Int(0, "centerfire_projectile", centerItem) == 1
                        ? 0f
                        : TFProjectileDefs.RocketMuzzleRight;

                    if (Born(TFProjectileEntityNames.Rocket, owner, forward, right, up,
                             TFProjectileDefs.RocketMuzzleForward, muzzleRight,
                             muzzleUp) is not TFRocket rocket)
                        return;

                    // Rocket Specialist (tf_weaponbase_rocket.cpp:279-298): the
                    // owner's level, remapped 1..4 -> 1.15..1.6 onto the launch
                    // speed and capped at 3000. Stock reads the attribute on
                    // the owner entity, a sentry's rocket asking its builder —
                    // the rockets this fires are always a player's.
                    // mult_projectile_speed rides the launcher's item first,
                    // before the specialist's remap (:274-275).
                    IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    float speed = attributes.Float(Speed(scriptSpeed, TFProjectileDefs.RocketSpeed),
                                                   "mult_projectile_speed", item);
                    int specialist = attributes.Int(0, "rocket_specialist", item);
                    if (specialist != 0)
                        speed = MathF.Min(speed * TFDamageModifyRules.RemapValClamped(specialist, 1f, 4f, 1.15f, 1.6f),
                                          SpecialistSpeedCap);

                    rocket.ArmedBy(shot.Damage, script.Id, script.Name, shot.Critical, attack.Command.OwnerIndex);
                    rocket.FiredBy(owner, forward * speed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Rocket} created by {owner}"
                          + $" at {Speed(scriptSpeed, TFProjectileDefs.RocketSpeed):F0} u/s\n");
                    break;
                }

            case TFProjectile.Pipebomb:
            case TFProjectile.Cannonball:
                {
                    // FirePipeBomb's arms, read off the firing item: the cone
                    // drawn onto the eye angles, and the speed through the
                    // mult_projectile_range hook (tf_weaponbase_gun.cpp:652-686).
                    // The launch speed itself is GetProjectileSpeed's
                    // (tf_weapon_grenadelauncher.cpp:469-479): the stock 1200
                    // under the launcher's mult_projectile_speed, the
                    // Loch-n-Load's 1.25, before that range hook. The cannon
                    // rides the same arm: stock's Create translates its
                    // projectile type into the pipe entity with the mode
                    // stamped (tf_weapon_grenade_pipebomb.cpp:437-458), the
                    // Loose Cannon's ball a TF_GL_MODE_CANNONBALL pipe.
                    int grenadeMode = TFProjectileDefs.PipebombMode(mode.Projectile);
                    IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    (forward, right, up) = SpreadBasis(eyes, item);
                    float speed = attributes.Float(Speed(scriptSpeed, TFProjectileDefs.PipeSpeed),
                                                   "mult_projectile_speed", item);
                    speed = attributes.Float(speed, "mult_projectile_range", item);
                    if (Born(TFProjectileEntityNames.Pipebomb, owner, forward, right, up,
                             TFProjectileDefs.PipeMuzzleForward, TFProjectileDefs.PipeMuzzleRight,
                             TFProjectileDefs.PipeMuzzleUp) is not TFPipebomb pipe)
                        return;
                    pipe.ThrownBy(owner, Lob(forward, speed, right, up),
                                  shot.Damage, script.DamageRadius, script.Id, script.Name, shot.Critical,
                                  grenadeMode);
                    Spin(pipe, item);

                    // FireProjectileInternal's charged fuse
                    // (tf_weapon_grenadelauncher.cpp:277-287): the held shot's
                    // window less the hold, through the same
                    // SetDetonateTimerLength the spawn booked — at none, the
                    // muzzle arm's trace-and-explode where the ball stands.
                    if (grenadeMode == TFProjectileDefs.GrenadeModeCannonball)
                    {
                        float mortar = attributes.Float(0f, TFProjectileBehaviour.MortarModeAttribute, item);
                        if (mortar > 0f)
                            pipe.ChargedFuse(mortar - charge);
                    }

                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Pipebomb} created by {owner}\n");
                    break;
                }

            case TFProjectile.PipebombRemote:
            case TFProjectile.PipebombPractice:
                {
                    IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    (forward, right, up) = SpreadBasis(eyes, item);

                    // GetChargeMaxTime again (tf_weapon_pipebomblauncher.h:75): the
                    // speed remap runs to the same window the charge clamped to, the
                    // launcher's stickybomb_charge_rate hook bends it.
                    float chargeMax = attributes.Float(TFProjectileBehaviour.MaxChargeTime,
                                                       TFProjectileBehaviour.ChargeRateAttribute, item);
                    float speed = attributes.Float(TFProjectileDefs.StickySpeed(charge, chargeMax),
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

                    // Create's translation (tf_weapon_grenade_pipebomb.cpp:437-458):
                    // the mode stamped before the spawn, the model and the
                    // damage-type arms reading it.
                    sticky.ThrownBy(owner, Lob(forward, speed, right, up),
                                    shot.Damage, script.DamageRadius, script.Id, script.Name, shot.Critical,
                                    TFProjectileDefs.PipebombMode(mode.Projectile));
                    Spin(sticky, item);

                    // FireProjectile's cap (tf_weapon_pipebomblauncher.cpp:381-392):
                    // TF_WEAPON_PIPEBOMB_COUNT (tf_shareddefs.h:391) through the
                    // launcher's add_max_pipebombs hook; past the cap the oldest goes.
                    tray.Register(attack.Command.OwnerIndex, sticky,
                                  attributes.Int(TFProjectileDefs.MaxStickies, "add_max_pipebombs", item));

                    // LaunchGrenade's DOT arm (tf_weapon_pipebomblauncher.cpp:305-309):
                    // the Scottish Resistance's set_detonate_mode 1 dresses its
                    // bomb in the defensive shell at the launch.
                    if (attributes.Int(TFProjectileDefs.DetonateModeStandard, "set_detonate_mode", item)
                        == TFProjectileDefs.DetonateModeDot)
                        sticky.DefensiveBomb();
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
            case TFProjectile.FestiveArrow:
                {
                    // The tired draw's wild shot (GetProjectileFireSetup's tail,
                    // tf_weapon_compound_bow.cpp:490-503): TF_ARROW_MAX_CHARGE_TIME
                    // 5 s after the draw began, the launch marked it
                    // (TFBowBehaviour.ModeTired) and the aim is re-drawn ±6° in
                    // pitch and yaw — stock's rand() pair, seeded as the pipes'
                    // cone is.
                    if (TFBowBehaviour.Tired(attack.Weapon))
                    {
                        eyes.X += random.RandomFloat(-TFBowBehaviour.TiredAim, TFBowBehaviour.TiredAim);
                        eyes.Y += random.RandomFloat(-TFBowBehaviour.TiredAim, TFBowBehaviour.TiredAim);
                        forward = Angles.Forward(eyes);
                        (right, up) = Angles.RightUp(forward);
                    }

                    if (Born(TFProjectileEntityNames.Arrow, owner, forward, right, up,
                             TFProjectileDefs.ArrowMuzzleForward, TFProjectileDefs.ArrowMuzzleRight,
                             TFProjectileDefs.ArrowMuzzleUp) is not TFArrow arrow)
                        return;

                    // The bow hands its alight flag to the arrow and loses it
                    // (LaunchGrenade's copy and clear, tf_weapon_compound_bow.cpp:125, :169).
                    bool lit = attack.Weapon.ArrowAlight;
                    attack.Weapon.ArrowAlight = false;

                    arrow.ArmedBy(script.Id, script.Name, shot.Critical, lit,
                                  TFProjectileDefs.ArrowDamage(shot.Damage, charge),
                                  TFProjectileDefs.ArrowGravity(charge));
                    arrow.FiredBy(owner, forward * TFProjectileDefs.ArrowSpeed(charge));
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Arrow} created by {owner}\n");
                    break;
                }

            case TFProjectile.BuildingRepairBolt:
                {
                    // The Rescue Ranger's bolt: the arrow's own fire (FireArrow's
                    // TF_PROJECTILE_BUILDING_REPAIR_BOLT arm,
                    // tf_weaponbase_gun.cpp:314-322) — same muzzle, same entity
                    // name, the claw the spawn dresses
                    // (tf_projectile_arrow.cpp:204-207) — at the rescue's fixed
                    // charge (tf_weapon_shotgun.cpp:507-516). The charge ramp is
                    // the bow's alone, and no lit flag rides a shotgun's shot.
                    if (Born(TFProjectileEntityNames.Arrow, owner, forward, right, up,
                             TFProjectileDefs.ArrowMuzzleForward, TFProjectileDefs.ArrowMuzzleRight,
                             TFProjectileDefs.ArrowMuzzleUp) is not TFArrow arrow)
                        return;

                    arrow.ArmedBy(script.Id, script.Name, shot.Critical, false,
                                  shot.Damage, TFProjectileDefs.RescueBoltGravity,
                                  TFProjectile.BuildingRepairBolt);
                    arrow.FiredBy(owner, forward * TFProjectileDefs.RescueBoltSpeed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Arrow} created by {owner}\n");
                    break;
                }

            case TFProjectile.HealingBolt:
            case TFProjectile.FestiveHealingBolt:
                {
                    // FireArrow's vecOffset (tf_weaponbase_gun.cpp:784): the
                    // arrow's muzzle, no ducked height to fold.
                    if (Born(TFProjectileEntityNames.HealingBolt, owner, forward, right, up,
                             TFProjectileDefs.ArrowMuzzleForward, TFProjectileDefs.ArrowMuzzleRight,
                             TFProjectileDefs.ArrowMuzzleUp) is not TFHealingBolt bolt)
                        return;

                    // The crossbow's fixed charge (CTFCrossbow::GetProjectileSpeed,
                    // tf_weapon_rocketlauncher.cpp:751-754): no ramp to read.
                    bolt.ArmedBy(shot.Damage, script.Id, script.Name, shot.Critical, attack.Command.OwnerIndex);
                    bolt.FiredBy(owner, forward * TFProjectileDefs.CrossbowSpeed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.HealingBolt} created by {owner}\n");
                    break;
                }

            case TFProjectile.Flare:
                {
                    // FireFlare's vecOffset, folded to the ducked height when
                    // the shooter crouches (tf_weaponbase_gun.cpp:746-750) —
                    // the rocket's numbers again.
                    float muzzleUp = owners.DuckingOf(attack.Command.OwnerIndex)
                        ? TFProjectileDefs.FlareMuzzleUpDucked
                        : TFProjectileDefs.FlareMuzzleUp;
                    if (Born(TFProjectileEntityNames.Flare, owner, forward, right, up,
                             TFProjectileDefs.FlareMuzzleForward, TFProjectileDefs.FlareMuzzleRight,
                             muzzleUp) is not TFFlare flare)
                        return;

                    // GetProjectileSpeed (tf_projectile_flare.cpp:617-623) and
                    // the gravity it flies under (Create, :90-92): the same
                    // mult_projectile_speed hook carries both.
                    IAttributeProvider? item = owners.ItemOf(attack.Command.OwnerIndex, script.Name);
                    float speed = attributes.Float(Speed(scriptSpeed, TFProjectileDefs.FlareSpeed),
                                                   "mult_projectile_speed", item);
                    float gravity = attributes.Float(TFProjectileDefs.FlareGravity,
                                                     "mult_projectile_speed", item);

                    // GetFlareGunType (tf_weapon_flaregun.h:63), read once
                    // for the flight: the modes the impact and the alt-fire
                    // both ask.
                    int flareMode = attributes.Int(0, TFProjectileBehaviour.FlareModeAttribute, item);

                    // The shot as the hooks answered it (FireFlare's
                    // SetDamage(GetProjectileDamage()),
                    // tf_weaponbase_gun.cpp:758; the multiplier at :909-910):
                    // the Detonator's damage penalty rides the touch.
                    flare.ArmedBy(shot.Damage, script.Id, script.Name, shot.Critical, gravity,
                                  attack.Command.OwnerIndex, flareMode);
                    flare.FiredBy(owner, forward * speed);

                    // FireFlare's list (tf_weaponbase_gun.cpp:760-763): a
                    // detonating gun's flare joins it, the alt-fire's fuel.
                    if (flareMode == TFProjectileBehaviour.FlareDetonate)
                        flareTray.Register(attack.Command.OwnerIndex, flare);

                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Flare} created by {owner}"
                          + $" at {speed:F0} u/s\n");
                    break;
                }

            case TFProjectile.EnergyRing:
                {
                    // FireEnergyBall's ring arm (tf_weaponbase_gun.cpp:551-599):
                    // the same muzzle offset, the ring's own 1200
                    // (GetProjectileSpeed, tf_weapon_raygun.cpp:214-217), flying
                    // flat (:222-225), no model of its own — the ring flies as
                    // its particle colour. The shot's damage and crit flag are
                    // the behaviour's rolled pair: the ring's hit answers
                    // ShouldPenetrate inside GetDamage, and a crit rides.
                    float muzzleUp = owners.DuckingOf(attack.Command.OwnerIndex)
                        ? TFProjectileDefs.EnergyMuzzleUpDucked
                        : TFProjectileDefs.EnergyMuzzleUp;
                    if (Born(TFProjectileEntityNames.EnergyRing, owner, forward, right, up,
                             TFProjectileDefs.EnergyMuzzleForward, TFProjectileDefs.EnergyMuzzleRight,
                             muzzleUp) is not TFProjectileEnergyRing ring)
                        return;

                    float ringSpeed = Speed(scriptSpeed, TFProjectileDefs.EnergyRingSpeed);
                    ring.ArmedBy(shot.Damage, script.Id, script.Name, shot.Critical, attack.Command.OwnerIndex);
                    ring.FiredBy(owner, forward * ringSpeed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.EnergyRing} created by {owner}"
                          + $" at {ringSpeed:F0} u/s\n");
                    break;
                }

            case TFProjectile.EnergyBall:
                {
                    // The ball arm (:583-595): the same muzzle, the mangler's
                    // own 1100 (tf_weapon_particle_cannon.cpp:72-75) flying flat
                    // (:80-83); its damage is the stored shot, the charge the
                    // held button banked riding beside it to the spawn.
                    float ballMuzzleUp = owners.DuckingOf(attack.Command.OwnerIndex)
                        ? TFProjectileDefs.EnergyMuzzleUpDucked
                        : TFProjectileDefs.EnergyMuzzleUp;
                    if (Born(TFProjectileEntityNames.EnergyBall, owner, forward, right, up,
                             TFProjectileDefs.EnergyMuzzleForward, TFProjectileDefs.EnergyMuzzleRight,
                             ballMuzzleUp) is not TFProjectileEnergyBall ball)
                        return;

                    float ballSpeed = Speed(scriptSpeed, TFProjectileDefs.EnergyBallSpeed);

                    // The charge the held button banked rides on to the ball's
                    // touch: seconds above zero make the shot charged
                    // (CTFParticleCannon::GetChargedShot, m_bChargedShot).
                    ball.ArmedBy(shot.Damage, script.Id, script.Name, shot.Critical, charge > 0f,
                                 attack.Command.OwnerIndex);
                    ball.FiredBy(owner, forward * ballSpeed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.EnergyBall} created by {owner}"
                          + $" at {ballSpeed:F0} u/s\n");
                    break;
                }

            case TFProjectile.Jar:
            case TFProjectile.JarMilk:
            case TFProjectile.FestiveJar:
            case TFProjectile.BreadmonsterJarate:
            case TFProjectile.BreadmonsterMadmilk:
            case TFProjectile.JarGas:
                {
                    // The jar's lob is the pipe's own arc read off the plain
                    // numbers (GetVelocityVector, tf_weapon_jar.cpp:153-157:
                    // forward at TF_JAR_LAUNCH_SPEED, +200 up, the two-draw
                    // jitter), from the pipe's muzzle offset (TossJarThink
                    // :176-182). No hook touches either: the jar's
                    // GetProjectileSpeed is the bare 1000 (tf_weapon_jar.cpp
                    // :103), the flight under SetPipebombMode's gravity.
                    // The jar faces pick their entity name by the projectile
                    // type: milk, gas (CTFJarGas, tf_weapon_jar_gas.h) and the
                    // plain jarate, the overriding items' faces with them.
                    string name = mode.Projectile switch
                    {
                        TFProjectile.JarMilk or TFProjectile.BreadmonsterMadmilk => TFProjectileEntityNames.JarMilk,
                        TFProjectile.JarGas => TFProjectileEntityNames.JarGas,
                        _ => TFProjectileEntityNames.Jar,
                    };
                    if (Born(name, owner, forward, right, up,
                             TFProjectileDefs.PipeMuzzleForward, TFProjectileDefs.PipeMuzzleRight,
                             TFProjectileDefs.PipeMuzzleUp) is not TFJar jar)
                        return;

                    jar.ArmedBy(shot.Critical, attack.Command.OwnerIndex);
                    jar.FiredBy(owner, Lob(forward, TFProjectileDefs.JarSpeed, right, up));
                    log.Msg($"[TF2/Projectiles] {name} created by {owner}\n");
                    break;
                }

            case TFProjectile.Cleaver:
                {
                    // The cleaver's throw (CTFCleaver::GetVelocityVector,
                    // tf_weapon_jar.cpp:906-916): ten forward parts to one up
                    // of the sight basis, normalized, at the override's 3000 —
                    // the jar's muzzle, no jitter and no sideways draw, the
                    // tilt the lob's constants stand in for.
                    if (Born(TFProjectileEntityNames.Cleaver, owner, forward, right, up,
                             TFProjectileDefs.PipeMuzzleForward, TFProjectileDefs.PipeMuzzleRight,
                             TFProjectileDefs.PipeMuzzleUp) is not TFCleaver cleaver)
                        return;

                    cleaver.ArmedBy(shot.Critical, attack.Command.OwnerIndex, script.Name);
                    cleaver.FiredBy(owner, (forward * TFProjectileDefs.CleaverAimForward + up).Normalized
                                          * TFProjectileDefs.CleaverSpeed);
                    log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.Cleaver} created by {owner}\n");
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
    /// <c>FirePipeBomb</c> carry — through the one birth every projectile
    /// shares (<see cref="ITFProjectileSpawner.Born"/>).
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
        => spawner.Born(className, entities.GetEyePosition(owner)
                                   + forward * forwardOffset + right * rightOffset + up * upOffset);

    /// <summary>The script's speed, or stock's when the script names none.</summary>
    private static float Speed(float scriptSpeed, float stock) => TFProjectileDefs.Speed(scriptSpeed, stock);

    /// <summary>The crosshair cone (<c>tf_weapon_pipebomblauncher.cpp:605</c>).</summary>
    private const float AimedDot = 0.975f;

    /// <summary>
    /// The Short Circuit's spawn (<c>CTFMechanicalArm::SecondaryAttack</c>'s
    /// GAME_DLL tail, <c>tf_weapon_mechanical_arm.cpp:250-286</c>): the orb at
    /// the eye's 40 forward, 15 right and −10 up along the aim, out at
    /// <c>tf_mecharm_orb_speed</c>, the owner's team, never critical. The
    /// eye→muzzle hull trace that stands between (:264-268) is the same deferral
    /// the pipes' launch carries — the seam's trace arms no
    /// <c>StartSolid</c>, and a wall at the muzzle is the born orb's own
    /// touch to settle. Stock's mirrored-view flip of the right waits with
    /// the view-flip seam, as every muzzle's does.
    /// </summary>
    public void ShockAttack(in WeaponAttackContext attack, TFWeaponScript script)
    {
        EntityHandle owner = players.GetHandle(attack.Command.OwnerIndex);
        Vector eyes = entities.GetEyeAngles(owner);
        Vector forward = Angles.Forward(eyes);
        (Vector right, Vector up) = Angles.RightUp(forward);

        if (Born(TFProjectileEntityNames.MechanicalArmOrb, owner, forward, right, up,
                 TFProjectileDefs.MechanicalArmMuzzleForward, TFProjectileDefs.MechanicalArmMuzzleRight,
                 TFProjectileDefs.MechanicalArmMuzzleUp) is not TFMechanicalArmOrb orb)
            return;

        orb.ArmedBy(attack.Command.OwnerIndex);
        orb.FiredBy(owner, forward * TFProjectileDefs.MechanicalArmOrbSpeed);
        log.Msg($"[TF2/Projectiles] {TFProjectileEntityNames.MechanicalArmOrb} created by {owner}\n");
    }

    public void Detonate(in WeaponAttackContext attack, TFWeaponScript script)
    {
        // The Detonator's alt (CTFFlareGun::SecondaryAttack,
        // tf_weapon_flaregun.cpp:147-173): every live flare goes, the tray
        // the fire armed. The behaviour gates the mode; this is its arm.
        if (script.Id == TFWeaponId.Flaregun)
        {
            flareTray.DetonateAll(attack.Command.OwnerIndex);
            return;
        }

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
        if (mode == TFProjectileDefs.DetonateModeDot)
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
    internal static bool InView(IEngineEntities entities, Vector eyePosition, Vector forward,
                               Vector playerOrigin, TFStickybomb sticky)
    {
        Vector towards = entities.GetWorldSpaceCenter(sticky.Handle) - eyePosition;
        float dot = towards.Normalized.Dot(forward);
        float distance = (entities.GetOrigin(sticky.Handle) - playerOrigin).Length;
        return dot > AimedDot || distance < sticky.GetDamageRadius();
    }
}
