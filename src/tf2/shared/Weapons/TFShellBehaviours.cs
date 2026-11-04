//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Player;

namespace TF2.Shared;

/// <summary>
/// The launchers (rocket launcher, grenade launcher, stickybomb launcher,
/// syringe gun): the shared half of <c>CTFWeaponBaseGun::PrimaryAttack</c>
/// (<c>tf_weaponbase_gun.cpp:95-200</c>) — the round spent, the crit rolled,
/// the next shot a fire delay from now — with the projectile itself asked of
/// the server (<see cref="ITFWeaponEffects.Projectile"/>, Stage 2.8). The
/// core's own attack is vetoed: it has no door for a TF projectile.
///
/// <para>
/// The stickybomb launcher charges instead of firing
/// (<c>CTFPipebombLauncher::PrimaryAttack</c> / <c>ItemPostFrame</c>,
/// <c>tf_weapon_pipebomblauncher.cpp:156-235</c>): a press starts the charge,
/// and the bomb leaves on release or when <c>TF_PIPEBOMB_MAX_CHARGE_TIME</c>
/// (4 s) is reached, carrying the seconds charged; the secondary detonates
/// (<c>SecondaryAttack</c> :428, the server's). The charge's start is
/// <see cref="WeaponNetState.BehaviourMeter"/>.
/// </para>
/// </summary>
public class TFProjectileBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>TF_PIPEBOMB_MAX_CHARGE_TIME</c> (<c>tf_weapon_pipebomblauncher.h:21</c>).</summary>
    public const float MaxChargeTime = 4f;

    private static bool Charges(TFWeaponScript script) => script.Id == TFWeaponId.Pipebomblauncher;

    /// <summary>
    /// The stickybomb launcher's own secondary is the detonation, not the
    /// class skill (<c>CTFPipebombLauncher::SecondaryAttack</c>,
    /// <c>tf_weapon_pipebomblauncher.cpp:428-469</c>), which the frame tail
    /// below runs every command the trigger is down; the other launchers keep
    /// the base's (<c>CTFGrenadeLauncher::SecondaryAttack</c>,
    /// <c>tf_weapon_grenadelauncher.cpp:491-502</c>).
    /// </summary>
    public override void SecondaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script && Charges(script))
            return;

        base.SecondaryAttack(in attack);
    }

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack))
            return false;

        // The machine reloads an empty clip itself, after this veto: say yes to it.
        if (weapon.Clip == 0 && ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex) > 0)
            return true;

        if (Charges(script))
        {
            if (weapon.BehaviourMeter <= 0f)
            {
                weapon.BehaviourMeter = attack.Now;
                SendAnim(in attack, WeaponAnim.Primary);
            }
            else if (attack.Now - weapon.BehaviourMeter >= MaxChargeTime)
            {
                Launch(in attack, script, in context);
            }
            return false;
        }

        Launch(in attack, script, in context);
        return false;
    }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script && Charges(script))
        {
            var weapon = attack.Weapon;
            bool held = (weapon.LastButtons & (int)InputButtons.Attack) != 0;

            if (weapon.BehaviourMeter > 0f
                && ((!held && weapon.Clip > 0) || attack.Now - weapon.BehaviourMeter >= MaxChargeTime))
                Launch(in attack, script, Context(in attack, script));

            if ((weapon.LastButtons & (int)InputButtons.Attack2) != 0 && attack.EffectsLive)
                Parts.Effects.Detonate(in attack, script);
        }

        base.ItemPostFrame(in attack);
    }

    private void Launch(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context)
    {
        var weapon = attack.Weapon;
        float charge = Charges(script) && weapon.BehaviourMeter > 0f
            ? MathF.Min(attack.Now - weapon.BehaviourMeter, MaxChargeTime)
            : 0f;
        weapon.BehaviourMeter = 0f;

        int cost = Math.Max(1, script.Primary.AmmoPerShot);
        if (ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex) > 0)
        {
            if (weapon.Clip < cost)
                return;
            weapon.Clip -= cost;
        }
        else
        {
            if (weapon.Reserve < cost)
                return;
            weapon.Reserve -= cost;
        }

        var shot = ModifyShot(in attack, default);

        SendAnim(in attack, WeaponAnim.Primary);
        PlaySound(in attack, script, shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single);
        if (attack.EffectsLive)
            Parts.Effects.Projectile(in attack, script, shot, charge);

        weapon.NextPrimaryAttack = attack.Now + FireDelay(in attack, script);
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack;
        Parts.Hooks.AfterFire(in context, in attack);

        // The launch ends as every stock shot of a gun does, in the tail
        // DoFireEffects of CTFWeaponBaseGun::FireProjectile
        // (tf_weaponbase_gun.cpp:357, the case block's common end :330-368):
        // the muzzle flash, unless the weapon vetoes it
        // (ShouldDoMuzzleFlash, :1008-1018 — no launcher does). The rocket
        // launcher's shipped script names no effect, so its ask is the empty
        // name's no-op, as stock's is.
        if (attack.EffectsLive && ShouldDoMuzzleFlash && script.MuzzleFlashParticleEffect.Length > 0)
            attack.Arms.MuzzleFlash(attack.Type, script.MuzzleFlashParticleEffect);
    }
}

/// <summary>
/// The flamethrower's shell (<c>CTFFlameThrower::PrimaryAttack</c>,
/// <c>tf_weapon_flamethrower.cpp:760-935</c>): a flame tick every
/// <c>tf_flamethrower_new_flame_fire_delay</c> (0.02 s, which replaces the
/// script's delay, :822-825) at <c>tf_flamethrower_damage_per_tick</c> 13
/// through the chain and the rapid-fire crit, using
/// <c>TF_FLAMETHROWER_AMMO_PER_SECOND_PRIMARY_ATTACK</c> 14 a second through a
/// remainder kept to two digits (:912-929). The flames are the server's
/// (<see cref="ITFWeaponEffects.Flames"/>, Stage 2.11). Stock spends the ammo
/// on the server only; here both sides spend it (the remainder is
/// <see cref="WeaponNetState.BehaviourMeter"/>, predicted). The airblast is Stage 2.11's.
///
/// <para>
/// The weapon state rides <see cref="WeaponNetState.BehaviourMode"/> beside
/// the shell's own fields, stock's networked <c>m_iWeaponState</c>
/// (<c>FlameState_t</c>, <c>tf_weapon_flamethrower.h</c>): the machine of
/// <c>PrimaryAttack</c> :743-787 — idle, the 0.16 s start (:753, in
/// <see cref="WeaponNetState.BehaviourTimeC"/>), firing — the release home to
/// idle (<c>ItemPostFrame</c> :604-611, the SECONDARY arm stock exempts
/// kept), the blast's SECONDARY (<c>FireAirBlast</c> :978), and the last
/// tick's crit (<c>m_bCritFire</c>, :844) on the bit beside them, which the
/// client's flame colour reads. Stock's <c>SetWeaponState</c> arms on the
/// flame manager's firing gate (:1102-1112) and the forward-pull conds wait
/// with those features.
/// </para>
/// </summary>
public class TFFlamethrowerBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary><c>FlameState_t</c> (<c>tf_weapon_flamethrower.h</c>).</summary>
    public enum FlameState
    {
        Idle = 0,
        StartFiring,
        Firing,
        Secondary,
    }

    /// <summary><c>FT_STATE_*</c> state bits in <see cref="WeaponNetState.BehaviourMode"/>.</summary>
    private const int StateMask = 7 << ModeKindShift;

    /// <summary><c>m_bCritFire</c>, the bit beside the state (:844).</summary>
    public const int CritLatchMask = 1 << (ModeKindShift + 3);

    /// <summary>The start's window (<c>m_flStartFiringTime</c>, :753 — 5 frames at 30 fps).</summary>
    public const float StartFiringDelay = 0.16f;

    /// <summary>The weapon's flame state.</summary>
    public static FlameState StateOf(WeaponNetState weapon) => (FlameState)((weapon.BehaviourMode & StateMask) >> ModeKindShift);

    /// <summary>Whether the last tick burnt as a crit — the flame colour's bit.</summary>
    public static bool CritLatched(WeaponNetState weapon) => (weapon.BehaviourMode & CritLatchMask) != 0;

    private static void SetState(WeaponNetState weapon, FlameState state) =>
        weapon.BehaviourMode = (weapon.BehaviourMode & ~StateMask) | ((int)state << ModeKindShift);

    public const float FlameFireDelay = 0.02f;
    public const float DamagePerTick = 13f;
    public const float AmmoPerSecond = 14f;

    /// <summary><c>tf_flamethrower_burstammo</c> (<c>tf_weapon_flamethrower.cpp:70</c>): the airblast's ammunition.</summary>
    public int AirBlastAmmo => Parts.FlamethrowerBurstammo;

    /// <summary><c>FireAirBlast</c>'s refires (<c>tf_weapon_flamethrower.cpp:1044-1045</c>).</summary>
    public const float AirBlastRefire = 0.75f;
    public const float AirBlastPrimaryRefire = 1.0f;

    protected override float ShotBaseDamage(in WeaponAttackContext attack, TFWeaponScript script) => DamagePerTick;

    /// <summary>The airblast is the flamethrower's own secondary (<c>CTFFlameThrower::SecondaryAttack</c>), not the class skill.</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) { }

    /// <summary>
    /// The secondary is the airblast (<c>CTFFlameThrower::SecondaryAttack</c>
    /// through <c>FireAirBlast</c>, <c>tf_weapon_flamethrower.cpp:1152-1208</c>,
    /// :965-1049): refused inside the refire delay, with the eyes under the
    /// water (:1175-1176, before the ammunition is even counted) and short of
    /// the ammunition, and on a blast the tick is not also thrown this frame
    /// (<c>m_bFiredSecondary</c>, :556/:595).
    /// </summary>
    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        // The release (ItemPostFrame :604-611): no trigger over a burning
        // state comes home to idle, the crit with it — the SECONDARY state
        // stock exempts stays.
        if ((attack.Weapon.LastButtons & (int)InputButtons.Attack) == 0
            && StateOf(attack.Weapon) is not (FlameState.Idle or FlameState.Secondary))
        {
            SetState(attack.Weapon, FlameState.Idle);
            attack.Weapon.BehaviourMode &= ~CritLatchMask;
        }

        if ((attack.Weapon.LastButtons & (int)InputButtons.Attack2) != 0
            && ScriptOf(attack.Type) is { } script && AirBlast(in attack, script))
            return;

        base.ItemPostFrame(in attack);
    }

    private bool AirBlast(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        if (weapon.NextSecondaryAttack > attack.Now)
            return false;

        if (Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
            return false;

        if (weapon.Reserve < AirBlastAmmo)
            return false;

        weapon.Reserve -= AirBlastAmmo;

        // The blast's state (FireAirBlast :978); the client answers it with
        // the blast's puff, the flame stopped.
        SetState(weapon, FlameState.Secondary);

        if (attack.EffectsLive)
            Parts.Effects.AirBlast(in attack, script);

        weapon.NextSecondaryAttack = attack.Now + AirBlastRefire;
        weapon.NextPrimaryAttack = attack.Now + AirBlastPrimaryRefire;
        return true;
    }

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;

        var weapon = attack.Weapon;
        var context = Context(in attack, script);
        if (!CanAttack(in attack, in context) || !Parts.Hooks.BeforeFire(in context, in attack) || weapon.Reserve <= 0)
        {
            // The refusal answers idle (PrimaryAttack :710-717).
            SetState(weapon, FlameState.Idle);
            return false;
        }

        // The state machine (PrimaryAttack :743-787): the start's window is
        // 0.16 s (:753, our <see cref="StartFiringDelay"/> in BehaviourTimeC),
        // the firing state after it (:768-771). The player animation events
        // and the min-burst window wait with the player-anim layer.
        switch (StateOf(weapon))
        {
            case FlameState.Idle:
                weapon.BehaviourTimeC = attack.Now + StartFiringDelay;
                SetState(weapon, FlameState.StartFiring);
                break;

            case FlameState.StartFiring when attack.Now > weapon.BehaviourTimeC:
                SetState(weapon, FlameState.Firing);
                break;
        }

        var shot = ModifyShot(in attack, default);

        // m_bCritFire, the latch the client's flame colour reads (:844).
        if (shot.Critical)
            weapon.BehaviourMode |= CritLatchMask;
        else
            weapon.BehaviourMode &= ~CritLatchMask;

        // "Don't attack if we're underwater" (:827-908): the tick is not
        // thrown, but the ammo and the refires below still are (:910-931).
        if (attack.EffectsLive && !Parts.Owners.EyesUnderwater(attack.Command.OwnerIndex))
            Parts.Effects.Flames(in attack, script, shot);

        float remainder = weapon.BehaviourMeter + AmmoPerSecond * FlameFireDelay;
        int spend = (int)remainder;
        if (spend > 0)
        {
            weapon.Reserve = Math.Max(0, weapon.Reserve - spend);
            remainder -= spend;
            remainder = (int)(remainder * 100) / 100f;
        }
        weapon.BehaviourMeter = remainder;

        SendAnim(in attack, WeaponAnim.Primary);
        weapon.NextPrimaryAttack = attack.Now + FlameFireDelay;
        weapon.TimeWeaponIdle = attack.Now + FlameFireDelay;
        Parts.Hooks.AfterFire(in context, in attack);
        return false;
    }
}

/// <summary>
/// The medigun's shell: no ammunition and no swing (the core would swing a
/// row without ammo); the trigger held or let go is told to the server every
/// command (<see cref="ITFWeaponEffects.Heal"/>, Stage 2.9's beam, target and
/// charge). The charge LEVEL is the carried weapon's own state
/// (<see cref="WeaponNetState.BehaviourMeter"/>, stock's m_flChargeLevel on
/// DT_WeaponMedigun): the server writes the meter at every command the gun
/// answers, and the client's replay of a command advances it here between
/// the server's words — stock's predicted <c>CWeaponMedigun</c> runs the
/// same build and drain in its own prediction.
/// </summary>
public class TFMedigunBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    /// <summary>Stock's weapon_medigun_chargerelease_rate default (tf_weapon_medigun.cpp:77);
    /// the release rate's convar is still to come (docs/divergences.md).</summary>
    public const float ChargeReleaseRate = 8f;    // seconds of release to an empty meter

    public override bool BeforePrimaryAttack(in WeaponAttackContext attack) => false;

    /// <summary>The medigun's secondary is its own (<c>CWeaponMedigun::SecondaryAttack</c>, tf_weapon_medigun.cpp:1813), not the class skill.</summary>
    public override void SecondaryAttack(in WeaponAttackContext attack) { }

    public override void ItemPostFrame(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is { } script)
        {
            bool held = (attack.Weapon.LastButtons & (int)InputButtons.Attack) != 0;

            // The replayed half — the target pick stock runs on both sides
            // (tf_weapon_medigun.cpp:1214) — every pass, before the charge:
            // a pass that wrote no beam would stand judged against the
            // server's word. The server's arm answers null and runs the whole
            // frame in its own.
            Parts.MedigunPrediction?.Frame(in attack, script, held);

            if (attack.EffectsLive)
                Parts.Effects.Heal(in attack, script, held);
            else if (Parts.PlayerData is { } data)
                PredictCharge(in attack, data);
        }

        // The tail's stamp, every command the gun answers: the server's arm
        // closes with the release clock at this command (the dt the drain and
        // the compare run on), so the replay — the release's own write below
        // is this same value — stamps it too, on the first prediction and
        // every pass, or a held gun's clock stands at the decode.
        attack.Weapon.BehaviourTimeB = attack.Now;

        base.ItemPostFrame(in attack);
    }

    /// <summary>
    /// The replay's charge: the build while the predicted beam is up, the
    /// drain while the predicted release is on, at the plain rates — the
    /// halved crawl near the buffed max and the healers' share are the
    /// server's word, which corrects the meter at every decode. A command
    /// whose dt is the server's own clock (fresh out of a decode) or is
    /// otherwise outside a command interval pays nothing: the clocks are
    /// written back on this machine's own from then on.
    /// </summary>
    private void PredictCharge(in WeaponAttackContext attack, ITFPlayerData data)
    {
        if (data.Predicted(attack.Command.OwnerIndex) is not { } carrier)
            return;

        var weapon = attack.Weapon;
        float now = attack.Now;
        if (carrier.ChargeReleasing)
        {
            float dt = now - weapon.BehaviourTimeB;
            if (dt is < 0f or > 1f)
                dt = 0f;
            weapon.BehaviourMeter = Math.Max(0f, weapon.BehaviourMeter - dt / ChargeReleaseRate);
        }
        else if (carrier.HealTargetIndex >= 0)
        {
            float dt = now - weapon.BehaviourTimeA;
            if (dt is < 0f or > 1f)
                dt = 0f;
            weapon.BehaviourTimeA = now;
            // The setup triple reads the same gate the server's build does
            // (tf_weapon_medigun.cpp:1347-1350); the crawl and the healers'
            // share stay the server's word, corrected at every decode.
            float setup = Parts.RoundSetup is { InSetupWithRoundTimer: true } ? 3f : 1f;
            weapon.BehaviourMeter = Math.Min(1f, weapon.BehaviourMeter + dt / Parts.MedigunChargeRate * setup);
        }
    }
}

/// <summary>
/// A PDA (<c>WeaponType "pda"</c>): its trigger has no shot — the attack is
/// vetoed and the owner instead goes back to the item held before this,
/// <c>CTFWeaponPDA::PrimaryAttack</c>'s one call to
/// <c>SelectLastItem</c> (<c>shared/tf/tf_weapon_pda.cpp:89-98</c>). The
/// secondary (the class special skill) and the spy's menu ride their lanes.
/// </summary>
public class TFPDABehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (attack.EffectsLive && ScriptOf(attack.Type) is { } script)
            Parts.Effects.SelectLast(in attack, script);

        return false;
    }
}

/// <summary>
/// A weapon with no attack of its own — the sapper and the invisibility
/// watch (the PDAs' trigger has the item switch, the builder its own kind):
/// every attack is vetoed (the core would swing a row without ammo). Their
/// uses are Stage 2.12's.
/// </summary>
public class TFInertBehaviour(TFWeaponParts parts) : TFWeaponBehaviour(parts)
{
    public override bool BeforePrimaryAttack(in WeaponAttackContext attack) => false;
}
