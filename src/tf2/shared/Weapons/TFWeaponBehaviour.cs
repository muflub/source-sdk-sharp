//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

using TF2.Shared.Econ;
using TF2.Shared.Combat;

namespace TF2.Shared;

/// <summary>
/// Every TF weapon's behaviour (stage1 1.9): what <c>CTFWeaponBase</c> and
/// <c>CTFWeaponBaseGun</c> decide at the core machine's decision points, for a
/// row <see cref="TFWeaponTypes"/> made from the weapon's script. It runs the
/// <see cref="ITFWeaponHook"/> chain at each point (clip, interval, damage,
/// the veto, the tail, the crit policy) and adds TF's rules:
///
/// <para>
/// — THE SHOT (<see cref="ModifyShot"/>): the script's damage through the
///   chain (<c>GetProjectileDamage</c>), and the crit roll of
///   <c>CalcIsAttackCritical</c> (<see cref="TFCritRoll"/>), seeded by the
///   command, so the server and every client replay roll alike. A crit is
///   the damage FLAGGED, not multiplied: stock's weapon only sets
///   <c>DMG_CRITICAL</c> (<c>tf_fx_shared.cpp:277</c>,
///   <c>tf_weaponbase_melee.cpp:858</c>) and
///   <c>CTFGameRules::ApplyOnDamageModifyRules</c> adds
///   <c>(TF_DAMAGE_CRIT_MULTIPLIER - 1) × damage</c> after the range rules
///   (<c>tf_gamerules.cpp:6731-6737</c>, <c>:6799</c>), which is the damage
///   layer's (<c>TFDamageModifyRules</c>, Stage 2.7). The bucket still pays
///   the tripled cost (<see cref="CritBucket"/>).
/// </para>
/// <para>
/// — THE SINGLE-SHELL RELOAD of a weapon that <c>m_bReloadsSingly</c>
///   (<c>CTFWeaponBase::ReloadSingly</c>, <c>tf_weaponbase.cpp:1940-2095</c>,
///   on its no-animation timers <c>UpdateReloadTimers</c>): the first round
///   lands after <c>TimeReloadStart</c> + <c>TimeReload</c>, each next one a
///   <c>TimeReload</c> later, until the clip is full or the reserve empty; the
///   trigger held with a round in the clip aborts it (:2398-2425). The machine
///   loads a whole clip when its reload timer runs out, so the timer is kept
///   one round ahead while rounds remain. A whole-clip reload is timed by the
///   viewmodel's reload animation, finished 0.2 s early
///   (<c>CTFWeaponBase::DefaultReload</c>, <c>tf_weaponbase.cpp:2221-2236</c>);
///   a viewmodel with no reload animation waits the script's
///   <c>TimeReload</c>, stock's no-animation timer.
/// </para>
/// <para>
/// — AIMING belongs to the weapon in the hands (stock removes
///   <c>TF_COND_AIMING</c> on holster: <c>CTFMinigun::WindDown</c>), so a weapon
///   that does not aim clears it.
/// </para>
/// <para>
/// — THE FLASH AND THE BRASS (<see cref="PrimaryAttack"/>): a hitscan gun's
///   shot asks <c>DoFireEffects</c>' muzzle flash — the script's particle
///   through <see cref="IWeaponArms.MuzzleFlash"/> — and
///   <c>OnFireEvent</c>'s brass through <see cref="IWeaponArms.ShellEject"/>,
///   carrying the item's definition index and the script's weapon id; and the
///   crit sound (<see cref="FireSound"/>) answers the predicted roll's crit as
///   <c>PlayWeaponShootSound</c> does, BURST for a crit and SINGLE otherwise.
///   The divergence sits in the deferred ledger: the launcher's muzzle flash
///   (stock's <c>FireProjectile</c> also runs <c>DoFireEffects</c>) is not
///   asked, because those kinds launch themselves before this tail, and the
///   brass is asked at the fire, where stock's rides the viewmodel
///   animation's event 6002.
/// </para>
///
/// <para>
/// Its own predicted state is the weapon's <c>Behaviour*</c> fields. The base
/// uses bit 0 of <see cref="WeaponNetState.BehaviourMode"/> (a single-shell
/// reload) and <see cref="WeaponNetState.BehaviourTimeA"/> (the next round's
/// time) while reloading; a rapid-fire weapon's crit clocks are
/// <see cref="WeaponNetState.BehaviourTimeA"/> (the stream's end) and
/// <see cref="WeaponNetState.BehaviourTimeB"/> (the last check). No
/// single-shell weapon fires rapid-fire crits, so the two never share a field.
/// </para>
/// </summary>
public class TFWeaponBehaviour(TFWeaponParts parts) : IWeaponBehaviour
{
    /// <summary><see cref="WeaponNetState.BehaviourMode"/>'s single-shell-reload bit.</summary>
    public const int ModeReloadingSingly = 1;

    /// <summary>
    /// <see cref="WeaponNetState.BehaviourMode"/>'s <c>m_bInAttack2</c> bit
    /// (tf_weaponbase_gun.cpp:237/:247): the alt-fire is down since the last
    /// skill it reached, cleared by the frame tail when the trigger lifts.
    /// </summary>
    public const int ModeInAttack2 = 1 << 1;

    /// <summary>The class skill's refire (<c>tf_weaponbase_gun.cpp:250</c>'s 0.5).</summary>
    public const float ClassSkillRefire = 0.5f;

    /// <summary>
    /// How much earlier than the reload animation a whole-clip reload ends
    /// (<c>tf_weaponbase.cpp:2225</c>'s 0.2 s — so a held trigger does not
    /// keep aborting the reload).
    /// </summary>
    public const float ReloadAnimLead = 0.2f;

    /// <summary>The first <see cref="WeaponNetState.BehaviourMode"/> bit a kind may use for itself.</summary>
    protected const int ModeKindShift = 8;

    protected TFWeaponParts Parts { get; } = parts ?? throw new ArgumentNullException(nameof(parts));

    /// <summary>The script behind a row, or null for a row without one (it then behaves as stock).</summary>
    protected TFWeaponScript? ScriptOf(in WeaponType type) => Parts.Scripts.Get(type.Name);

    /// <summary>The hook context of an attack: the owner, their conditions and the item.</summary>
    protected TFWeaponHookContext Context(in WeaponAttackContext attack, TFWeaponScript script)
        => ContextFor(attack.Type, script, attack.Command.OwnerIndex);

    /// <summary>
    /// The hook context asked about an owner: their conditions, their item and
    /// their manager, or bare when the index is nobody (0). The points that
    /// never see an attack — <see cref="ModifyClipSize"/>,
    /// <see cref="ModifyFireInterval"/> and <see cref="ModifyReloadTime"/> —
    /// ask through this, so the item and manager arms stock's
    /// <c>GetMaxClip1</c>, <c>ApplyFireDelay</c> and <c>SetReloadTimer</c>
    /// reach (<c>tf_weaponbase.cpp:571-575, 1895-1898, 2274-2277</c>) are read
    /// from the carrier.
    /// </summary>
    protected TFWeaponHookContext ContextFor(in WeaponType type, TFWeaponScript script, int ownerIndex)
    {
        return ownerIndex <= 0
            ? new TFWeaponHookContext(type, script)
            : new TFWeaponHookContext(type, script, ownerIndex,
                                      Parts.Owners.ConditionsOf(ownerIndex), Parts.Owners.ItemOf(ownerIndex, type.Name),
                                      Parts.Owners.ManagerOf(ownerIndex));
    }

    public virtual int ModifyClipSize(in WeaponType type, int clipSize, int ownerIndex) =>
        ScriptOf(in type) is { } script ? Parts.Hooks.ModifyClip(ContextFor(in type, script, ownerIndex), clipSize) : clipSize;

    public virtual float ModifyFireInterval(in WeaponType type, float interval, int ownerIndex) =>
        ScriptOf(in type) is { } script ? Parts.Hooks.ModifyInterval(ContextFor(in type, script, ownerIndex), interval) : interval;

    public virtual float ModifyReloadTime(in WeaponType type, float duration, int ownerIndex) =>
        ScriptOf(in type) is { } script ? Parts.Hooks.ModifyReloadTime(ContextFor(in type, script, ownerIndex), duration) : duration;

    /// <summary>
    /// Stock's <c>CTFWeaponBase::Deploy</c> multiplier (<c>tf_weaponbase.cpp:1255-</c>),
    /// asked where a side writes the deploy clocks. The outgoing weapon's item is
    /// the carrier's word under its name, the same door
    /// <see cref="ModifyClipSize"/> asks the incoming one through; the hold of a
    /// weapon never named is nobody's, as an unheld slot has no stamp.
    /// </summary>
    public virtual float ModifyDeployTime(in WeaponType type, float duration, int ownerIndex, string previousWeapon,
                                          float secondsSincePreviousDeploy) =>
        ScriptOf(in type) is { } script
            ? Parts.Hooks.ModifyDeployTime(
                ContextFor(in type, script, ownerIndex) with
                {
                    PreviousItem = ownerIndex > 0 ? Parts.Owners.ItemOf(ownerIndex, previousWeapon) : null,
                    SecondsSincePreviousDeploy = secondsSincePreviousDeploy,
                },
                duration)
            : duration;

    /// <summary>
    /// The fired shot's pellets through the chain — stock's
    /// <c>CALL_ATTRIB_HOOK_FLOAT_ON_OTHER</c> over the loop's count
    /// (<c>tf_fx_shared.cpp:305</c>), asked once per attack before the volley's
    /// multiplication.
    /// </summary>
    public virtual int ModifyPellets(in WeaponAttackContext attack, int pellets) =>
        ScriptOf(attack.Type) is { } script ? Parts.Hooks.ModifyPellets(Context(in attack, script), pellets) : pellets;

    /// <summary>Stock's base <c>SendWeaponAnim</c>: every send restarts the anim.</summary>
    public virtual bool AnimContinuesOnResend(in WeaponAttackContext attack, WeaponAnim anim) => false;

    public virtual bool BeforePrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return true;
        var context = Context(in attack, script);
        return CanAttack(in attack, in context) && Parts.Hooks.BeforeFire(in context, in attack);
    }

    /// <summary>
    /// <c>CTFPlayer::CanAttack</c>'s stealth arms (tf_player_shared.cpp:12226):
    /// a decloak's no-attack window refuses the attack but for the user buff
    /// still on (<c>TF_COND_STEALTHED_USER_BUFF</c>, the Dead Ringer's
    /// long cloak), and a cloaked spy swings nothing. Stock's grapple-hook
    /// flag arm (:12228) and the hint (:12231) wait for that item; the CYOA,
    /// passtime and feign-ready arms above and below are their lanes'. The
    /// ownerless context (index 0) is nobody to refuse.
    /// </summary>
    protected bool CanAttack(in WeaponAttackContext attack, in TFWeaponHookContext context)
    {
        int owner = attack.Command.OwnerIndex;
        if (owner <= 0)
            return true;
        var conditions = context.Conditions;
        if (Parts.Owners.NoAttackExpire(owner) > attack.Now && conditions?.Has(TFCond.StealthedUserBuff) != true)
            return false;
        return conditions?.Has(TFCond.Stealthed) != true;
    }

    public virtual ShotDamage ModifyShot(in WeaponAttackContext attack, ShotDamage shot)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return shot;

        var context = Context(in attack, script);
        float damage = Parts.Hooks.ModifyDamage(in context, ShotBaseDamage(in attack, script));
        return RollCrit(in attack, script, in context) ? Critical(damage) : new ShotDamage(damage);
    }

    /// <summary>
    /// A crit at <paramref name="damage"/>: the damage as rolled, flagged. The
    /// multiplier is the damage rules' (stock applies it in
    /// <c>ApplyOnDamageModifyRules</c>, never in the weapon), so a crit is
    /// tripled once.
    /// </summary>
    public static ShotDamage Critical(float damage) => new(damage, true);

    /// <summary>A shot's damage before the chain: the script's (a kind with a charge or a ramp answers its own).</summary>
    protected virtual float ShotBaseDamage(in WeaponAttackContext attack, TFWeaponScript script) => script.Primary.Damage;

    /// <summary>Whether this weapon rolls as melee (<c>CTFWeaponBaseMelee</c>'s helpers).</summary>
    protected virtual bool RollsAsMelee => false;

    /// <summary>
    /// <c>CalcIsAttackCritical</c> for this attack: the owner's policy through the
    /// chain, the weapon's bucket on this side, and for a rapid-fire mode its
    /// clocks in the weapon's predicted state.
    /// </summary>
    protected bool RollCrit(in WeaponAttackContext attack, TFWeaponScript script, in TFWeaponHookContext context)
    {
        var settings = Parts.CritSettings.Current;
        int owner = attack.Command.OwnerIndex;
        var weapon = attack.Weapon;

        // The round's two numbers the server measures the crit rate from
        // (stock's FindPlayerStats, tf_weaponbase.cpp:4639-4645); only the
        // server's owners answers them, so only its roll measures.
        var roundStats = owner > 0 ? Parts.Owners.RangedCritDamageOf(owner) : null;

        var policy = KindCritPolicy(in attack, Parts.Hooks.CritPolicy(in context, new TFCritOwner
        {
            WinsCrits = owner > 0 && Parts.Owners.WinsCrits(owner),
            CritMult = owner > 0 ? Parts.Owners.CritMult(owner) : 1f,
            ObservedCritChance = weapon.ObservedCritChance,
            RangedDamage = roundStats?.Total ?? 0,
            RandomCritDamage = roundStats?.RandomCrit ?? 0,
            StatsMeasured = roundStats is not null,
        }));

        var mode = script.Primary;
        bool rapid = mode.UseRapidFireCrits && !RollsAsMelee;

        var shot = new TFCritShot(
            Melee: RollsAsMelee,
            RapidFire: rapid,
            // The bucket's damage is the script's through mult_dmg, never a charge or a ramp.
            Damage: Parts.Hooks.ModifyDamage(in context, mode.Damage),
            BulletsPerShot: mode.BulletsPerShot,
            FireDelay: mode.TimeFireDelay,
            attack.Command,
            attack.Now,
            FirstPrediction: attack.EffectsLive);

        var clocks = rapid
            ? new TFCritClocks { CritUntil = weapon.BehaviourTimeA, LastRapidCheck = weapon.BehaviourTimeB }
            : new TFCritClocks();

        var state = Parts.Buckets.StateFor(attack.Command.WeaponIndex, weapon.WeaponTypeId, settings.BucketDefault);
        TFCritResult result;

        // A kind that answers its crit whole (the knife's helpers over
        // IsBackstab, tf_weapon_knife.cpp:487-500) draws nothing and touches
        // no bucket, on the first roll or a replay's.
        if (Parts.Hooks.CritAnswer(in context) is { } decided)
        {
            result = decided;
            if (attack.EffectsLive)
                state.Remember(attack.Command.Seed, result, clocks);
        }
        else if (attack.EffectsLive)
        {
            result = Parts.Crits.Roll(in shot, in policy, in settings, state.Bucket, ref clocks);
            state.Remember(attack.Command.Seed, result, clocks);
        }
        else if (!state.Recall(attack.Command.Seed, out result, out clocks))
        {
            // A replay of a command this side never rolled: rolled against a
            // copy, so the bucket is not paid twice.
            var copy = state.Bucket.Copy();
            result = Parts.Crits.Roll(in shot, in policy, in settings, copy, ref clocks);
        }

        if (rapid)
        {
            weapon.BehaviourTimeA = clocks.CritUntil;
            weapon.BehaviourTimeB = clocks.LastRapidCheck;
        }

        // Stock's CanFireRandomCriticalShot Set()s the weapon's member before
        // comparing (tf_weaponbase.cpp:4652-4660); the set rides out on the
        // result so a refusal records it too, and only the side that measured
        // — stock's GAME_DLL arm, ours the one whose owners answered — has one.
        if (result.ObservedSet is { } observed)
            weapon.ObservedCritChance = observed;

        return result.Critical;
    }

    /// <summary>A kind's own word on the crit policy after the chain's (the sniper rifle's <c>CanFireCriticalShot</c>).</summary>
    protected virtual TFCritOwner KindCritPolicy(in WeaponAttackContext attack, TFCritOwner policy) => policy;

    /// <summary>
    /// <c>CTFWeaponBaseGun::PlayWeaponShootSound</c>
    /// (<c>tf_weaponbase_gun.cpp:829-839</c>): the script's BURST sound when
    /// this attack rolled a crit, SINGLE otherwise — the sound the predicted
    /// roll is for, the crit's damage staying the server's (the flag travels,
    /// the multiplier is the damage rules'). The gun's shot only: a row with
    /// no script, a swing and a launch answer null, the row's own — the TF
    /// kinds that swing and launch play through their own behaviours. An
    /// empty category answers empty, which is stock's silence and not a
    /// fallback to the row (every arm refuses an empty name).
    /// </summary>
    public virtual string? FireSound(in WeaponAttackContext attack, ShotDamage shot)
        => ScriptOf(attack.Type) is { } script && !attack.Type.IsMelee && attack.Type.Projectile.Length == 0
            ? script.Sound(shot.Critical ? WeaponScriptSound.Burst : WeaponScriptSound.Single)
            : null;

    /// <summary>
    /// <c>tf_use_fixed_weaponspreads</c>: a buckshot load of more than one
    /// pellet leaves in the fixed pattern when the convar is set
    /// (<c>IsFixedWeaponSpreadEnabled</c> at the shot,
    /// <c>tf_fx_shared.cpp:302</c> — the load is the weapon's own count, before
    /// any attribute's). Null leaves the random sample: the convar's default
    /// and every weapon without one.
    /// </summary>
    public virtual PelletPattern? FixedPellets(in WeaponAttackContext attack)
    {
        if (Parts.FixedSpread is not { } fixedSpread || !fixedSpread.Value)
            return null;

        int pellets = attack.Type.Pellets;
        if (pellets <= 1 || ScriptOf(attack.Type) is not { } script
            || (TFWeaponDamageTypes.Of(script.Id) & DamageType.Buckshot) == 0)
            return null;

        // The wide/narrow pick compares the scaled count: stock applies
        // mult_bullets_per_shot before the loop (:305) and tests the scaled
        // value inside it (:317); the gate above reads the row's own count,
        // as stock's bFixedSpread line (:302) computes it before the hook.
        int scaled = Parts.Hooks.ModifyPellets(Context(in attack, script), pellets);
        return scaled >= WidePelletCount ? WidePattern : NarrowPattern;
    }

    /// <summary>
    /// <c>cl_autoreload</c>: the player's wish presses reload for them while
    /// the trigger is down elsewhere and the clip can take rounds
    /// (<c>CTFWeaponBase::ItemPostFrame</c>'s injection,
    /// tf_weaponbase.cpp:2451-2457, the wish stock reads per side —
    /// c_tf_player.cpp:299 here, tf_gamerules.cpp:10269's cached
    /// <c>m_bAutoReload</c> on the server). A weapon whose load leaves whole
    /// (<c>AutoFiresFullClip</c>) reloads by its own arm, never here.
    /// </summary>
    public bool AutoReload(in WeaponAttackContext attack, int buttons)
    {
        if ((buttons & ((int)InputButtons.Attack | (int)InputButtons.Attack2)) != 0)
            return false;

        if (Parts.AutoReload is not { } autoReload || !autoReload.Wants(attack.Command.OwnerIndex))
            return false;

        if (ScriptOf(attack.Type) is { } script && Parts.Hooks.FiresFullClip(Context(in attack, script)))
            return false;

        var type = attack.Type;
        int clipSize = ModifyClipSize(in type, type.ClipSize, attack.Command.OwnerIndex);
        int primary = Math.Min(clipSize - attack.Weapon.Clip, attack.Weapon.Reserve);

        return clipSize > 0 && primary != 0;
    }

    // ---- tf_fx_shared.cpp's fixed pellet tables --------------------------------

    /// <summary>The pellet count from which the wide table answers (<c>nBulletsPerShot &gt;= 15</c>).</summary>
    public const int WidePelletCount = 15;

    private const int NarrowPelletCount = 10;
    private const float NarrowScale = 0.5f;
    private const float WideJitter = 0.07f;

    /// <summary>
    /// <c>g_vecFixedWpnSpreadPellets</c> (<c>tf_fx_shared.cpp:112-124</c>) at
    /// half spread: the ten-pellet cross, first and last down the middle, the
    /// diagonal four pulled in — and the index wrapping, as the shot's while
    /// does, so a wider load than the table repeats it.
    /// </summary>
    public static (float Right, float Up) NarrowPattern(int pellet, SeededUniformStream stream)
    {
        var (right, up) = NarrowPoint(pellet % NarrowPelletCount);
        return (right * NarrowScale, up * NarrowScale);
    }

    private static (float, float) NarrowPoint(int i) => i switch
    {
        1 => (1f, 0f),
        2 => (-1f, 0f),
        3 => (0f, -1f),
        4 => (0f, 1f),
        5 => (0.85f, -0.85f),
        6 => (0.85f, 0.85f),
        7 => (-0.85f, -0.85f),
        8 => (-0.85f, 0.85f),
        _ => (0f, 0f), // the first and the last pellets: down the middle
    };

    /// <summary>
    /// <c>g_vecFixedWpnSpreadPelletsWideLarge</c>
    /// (<c>tf_fx_shared.cpp:127-152</c>) at full spread, each coordinate beside
    /// its own seeded draw of the rectangle's &plusmn;0.07.
    /// </summary>
    public static (float Right, float Up) WidePattern(int pellet, SeededUniformStream stream)
    {
        int i = pellet % WidePelletCount;
        float x = WideColumn(i) + stream.RandomFloat(-WideJitter, WideJitter);
        float y = WideRow(i) + stream.RandomFloat(-WideJitter, WideJitter);
        return (x, y);
    }

    // The wide table's columns run 0, -0.5, -1, 0.5, 1 across each row of five.

    private static float WideColumn(int i) => (i % 5) switch
    {
        1 => -0.5f,
        2 => -1f,
        3 => 0.5f,
        4 => 1f,
        _ => 0f,
    };

    // ...at the rows 0, +0.5, -0.5 the table stacks.

    private static float WideRow(int i) => i < 5 ? 0f : i < 10 ? 0.5f : -0.5f;

    /// <summary>
    /// Whether a shot of this gun brings the muzzle flash
    /// (<c>CTFWeaponBaseGun::ShouldDoMuzzleFlash</c>,
    /// <c>tf_weaponbase_gun.h:61</c>): true. Stock's false overrides are the
    /// minigun and the compound bow (<c>tf_weapon_minigun.h:78</c>,
    /// <c>tf_weapon_compound_bow.h:73</c>), and neither reaches this tail —
    /// they fire themselves before it — so the virtual is here for a future
    /// kind and answered by none today.
    /// </summary>
    protected virtual bool ShouldDoMuzzleFlash => true;

    /// <summary>
    /// Whether a shot of this gun ejects brass
    /// (<c>CTFWeaponBase::ShouldEjectBrass</c>, <c>tf_weaponbase.h:591</c>):
    /// true. The sniper rifle's override adds the jarate-bottle window
    /// (<c>tf_weapon_sniperrifle.cpp:1298</c>) and the net state carries no
    /// jarate time, so the base's true is all that is faithful today.
    /// </summary>
    protected virtual bool ShouldEjectBrass => true;

    /// <summary>
    /// After the machine's shot: the chain's tail, TF's timer, and the gun's
    /// own fire effects — <c>DoFireEffects</c>' muzzle flash
    /// (<c>tf_weaponbase_gun.cpp:330-368</c>) and <c>OnFireEvent</c>'s brass
    /// (<c>tf_weaponbase.cpp:4950-4977</c>) — both on the live run, as every
    /// effect of a predicted shot is. Stock times the brass to the viewmodel
    /// animation's event 6002 and asks the flash on the projectile path too;
    /// this tail runs for the hitscan guns the machine itself fired (every
    /// kind that fires itself vetoes or launches before it), at the fire.
    /// </summary>
    public virtual void PrimaryAttack(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return;

        attack.Weapon.NextPrimaryAttack = attack.Now + FireDelay(in attack, script);
        Parts.Hooks.AfterFire(Context(in attack, script), in attack);

        if (attack.EffectsLive)
        {
            if (ShouldDoMuzzleFlash && script.MuzzleFlashParticleEffect.Length > 0)
                attack.Arms.MuzzleFlash(attack.Type, script.MuzzleFlashParticleEffect);

            if (ShouldEjectBrass)
                attack.Arms.ShellEject(attack.Type, ItemDefIndex(in attack), (int)script.Id);
        }
    }

    /// <summary>
    /// The stock base classes' secondary: <c>CTFWeaponBaseGun::SecondaryAttack</c>
    /// (<c>tf_weaponbase_gun.cpp:234-251</c>), the melee's and the knife's
    /// (<c>tf_weaponbase_melee.cpp:244</c>, <c>tf_weapon_knife.cpp:304</c>),
    /// the PDA's (<c>tf_weapon_pda.cpp:114</c>) and the grenade launcher's
    /// (<c>tf_weapon_grenadelauncher.cpp:499</c>) all call
    /// <c>DoClassSpecialSkill</c> under the <c>m_bInAttack2</c> debounce and
    /// arm a 0.5 s refire. The kinds with a secondary of their own — the
    /// minigun's spin, the flamethrower's airblast, the medigun, the sniper's
    /// zoom, the stickybomb launcher's detonation — override the stock
    /// virtual, so their kinds answer nothing here and run their own frame
    /// arms.
    /// </summary>
    public virtual void SecondaryAttack(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;
        if ((weapon.BehaviourMode & ModeInAttack2) != 0)
            return;

        weapon.BehaviourMode |= ModeInAttack2;
        weapon.NextSecondaryAttack = attack.Now + ClassSkillRefire;

        // No EffectsLive gate: the skill's state half is predicted state and
        // every replay of the command must re-run it (the medigun's
        // PredictCharge is the precedent); the visual halves gate inside.
        Parts.Effects.ClassSpecialSkill(in attack);
    }

    public virtual void Reload(in WeaponAttackContext attack)
    {
        if (ScriptOf(attack.Type) is not { } script)
            return;

        var weapon = attack.Weapon;
        var mode = script.Primary;

        if (ReloadsSinglyFor(in attack, script))
        {
            StartReloadSingly(in attack, script);
            return;
        }

        // CTFWeaponBase::DefaultReload's clock (tf_weaponbase.cpp:2221-2236):
        // the viewmodel's reload animation carries it, considered finished 0.2
        // s early so a held trigger does not abort the anim's tail (:2225);
        // none, the script's — the primary's, this machine never reloads a
        // secondary clip (stock's bReloadSecondary arm :2232-2235 belongs to
        // the alt-fire guns). Either way through SetReloadTimer's arms, as the
        // single-shell clocks are; and the animation plays the base time over
        // the whole clock (:2320-2327), so the rate is the anim over it.
        float anim = attack.Arms.AnimDuration(WeaponAnim.Reload);
        float reload = ScaleReloadTime(attack.Type,
                                       anim > 0f ? anim - ReloadAnimLead : mode.TimeReload,
                                       attack.Command.OwnerIndex);
        weapon.NextPrimaryAttack = attack.Now + reload;
        weapon.TimeWeaponIdle = attack.Now + reload;

        if (anim > 0f)
            attack.ViewModel.PlaybackRate = anim / reload;
    }

    /// <summary>
    /// Whether this reload loads one shell at a time: the kind's own answer
    /// (the constructor's <c>m_bReloadsSingly</c>,
    /// <c>tf_weapon_shotgun.cpp:68</c>), with the item's
    /// <c>set_scattergun_no_reload_single</c> turning the gun's own member off
    /// before the base's reload runs — stock's hook sits at the same decision
    /// point (<c>CTFScatterGun::Reload</c>, :299-309), read every reload alike
    /// on a live run and a replay's.
    /// </summary>
    private bool ReloadsSinglyFor(in WeaponAttackContext attack, TFWeaponScript script) =>
        TFWeaponKinds.ReloadsSingly(script.Id) && !Parts.Hooks.ReloadsWithWholeClip(Context(in attack, script));

    /// <summary>
    /// One of the reload's script clocks through the chain — stock's
    /// <c>SetReloadTimer</c>, whose arms scale (<c>tf_weaponbase.cpp:2274
    /// -2316</c>) and whose floor keeps the time off zero (:2319).
    /// </summary>
    private float ScaleReloadTime(in WeaponType type, float duration, int ownerIndex) =>
        MathF.Max(ModifyReloadTime(in type, duration, ownerIndex), 0.00001f);

    public virtual void ItemPostFrame(in WeaponAttackContext attack)
    {
        var weapon = attack.Weapon;

        // m_bInAttack2's clear on release (tf_weaponbase.cpp:2465-2468), and
        // the busy frame's arm (:2388-2396): a reload does not silence the
        // class special — the demoman detonates in the middle of one
        // (CTFPipebombLauncher::ItemBusyFrame,
        // tf_weapon_pipebomblauncher.cpp:410-423). The machine's step has
        // already returned on a reload before its own alt-fire arm, so this
        // is the only door a busy weapon has.
        if ((weapon.LastButtons & (int)InputButtons.Attack2) == 0)
            weapon.BehaviourMode &= ~ModeInAttack2;
        else if (weapon.InReload && (weapon.BehaviourMode & ModeInAttack2) == 0)
        {
            weapon.BehaviourMode |= ModeInAttack2;
            // Ungated as above: the skill's state is predicted, the replay
            // re-runs it, the visuals gate inside.
            Parts.Effects.ClassSpecialSkill(in attack);
        }

        if (ScriptOf(attack.Type) is not { } script)
            return;

        if ((weapon.BehaviourMode & ModeReloadingSingly) != 0)
            ReloadSinglyFrame(in attack, script);
        // The machine's empty-click reload starts without asking Reload: a
        // single-shell weapon takes it over from its first frame, and the
        // item's no_reload_single holds it back to the machine's whole-clip
        // finish exactly as it held back Reload's own arm.
        else if (weapon.InReload && ReloadsSinglyFor(in attack, script))
            StartReloadSingly(in attack, script);

        if (!HoldsAiming(in attack))
            ClearAiming(attack.Command.OwnerIndex);
    }

    /// <summary>
    /// Plays a viewmodel animation through the arms, as the machine's own
    /// sends do: the sequence, the parity that restarts it, a rate of 1. A
    /// viewmodel without the animation is left as it is.
    ///
    /// <para>
    /// Virtual as stock's <c>SendWeaponAnim</c> is virtual on the weapon class:
    /// <c>CTFMinigun::SendWeaponAnim</c> (<c>tf_weapon_minigun.cpp:945-973</c>)
    /// answers a re-send of the looping fire anim without a restart, and the
    /// override rides here.
    /// </para>
    /// </summary>
    protected virtual void SendAnim(in WeaponAttackContext attack, WeaponAnim anim)
    {
        int sequence = attack.Arms.LookupSequence(anim);
        if (sequence < 0)
            return;

        attack.ViewModel.Sequence = sequence;
        attack.ViewModel.AnimationParity = SourceSharp.Shared.AnimationParity.Next(attack.ViewModel.AnimationParity);
        attack.ViewModel.PlaybackRate = 1f;
    }

    /// <summary>Plays a script sound category, on the live run only.</summary>
    protected static void PlaySound(in WeaponAttackContext attack, TFWeaponScript script, WeaponScriptSound category)
    {
        string sound = script.Sound(category);
        if (attack.EffectsLive && sound.Length > 0)
            attack.Arms.Sound(sound);
    }

    /// <summary>The fire interval this command: the script's <c>TimeFireDelay</c> through the chain (<c>ApplyFireDelay</c>).</summary>
    protected float FireDelay(in WeaponAttackContext attack, TFWeaponScript script) =>
        ModifyFireInterval(attack.Type, script.Primary.TimeFireDelay, attack.Command.OwnerIndex);

    /// <summary>Whether this weapon is holding <c>TF_COND_AIMING</c> this command.</summary>
    protected virtual bool HoldsAiming(in WeaponAttackContext attack) => false;

    /// <summary>Removes <c>TF_COND_AIMING</c> from the owner, if they have it.</summary>
    protected void ClearAiming(int owner)
    {
        if (owner > 0 && Parts.Owners.ConditionsOf(owner) is { } conditions && conditions.Has(TFCond.Aiming))
            conditions.Remove(TFCond.Aiming);
    }

    // The carried item's definition index, as stock's OnFireEvent takes it
    // from GetItem(): the item is the container's definition, and a weapon
    // with no item — an admin give, a bot's — carries 0, the value the shell
    // effect's model pick is built to handle.
    private int ItemDefIndex(in WeaponAttackContext attack)
    {
        int owner = attack.Command.OwnerIndex;
        return owner > 0 && Parts.Owners.ItemOf(owner, attack.Type.Name) is AttributeContainer { Item: { } item }
            ? item.Defindex
            : 0;
    }

    private void StartReloadSingly(in WeaponAttackContext attack, TFWeaponScript script)
    {
        // TF_RELOAD_START's timer, then the first round's — each of the two
        // script clocks through SetReloadTimer's arms and floor, as stock's
        // UpdateReloadTimers runs them (:2248-2261).
        var weapon = attack.Weapon;
        int owner = attack.Command.OwnerIndex;
        float round = ScaleReloadTime(attack.Type, script.Primary.TimeReload, owner);
        float firstRound = attack.Now + ScaleReloadTime(attack.Type, script.Primary.TimeReloadStart, owner) + round;
        weapon.BehaviourMode |= ModeReloadingSingly;
        weapon.BehaviourTimeA = firstRound;
        HoldReloadPast(weapon, firstRound, round);

        // TF_RELOAD_START's own animation (:1978). It lands after the
        // machine's entry send in this same command, whose plain reload it
        // replaces — stock's singly path never plays the plain reload first.
        SendAnim(attack, WeaponAnim.ReloadStart);
    }

    private void ReloadSinglyFrame(in WeaponAttackContext attack, TFWeaponScript script)
    {
        var weapon = attack.Weapon;
        var mode = script.Primary;

        // Switched away, or the machine finished it: nothing in progress.
        if (!weapon.InReload)
        {
            weapon.BehaviourMode &= ~ModeReloadingSingly;
            return;
        }

        // The trigger with a round chambered aborts the reload (:2398-2425):
        // the gun may fire at once, since the reload began only once it could.
        if ((weapon.LastButtons & (int)InputButtons.Attack) != 0 && weapon.Clip > 0)
        {
            EndReload(weapon, attack.Now, aborted: true);
            return;
        }

        if (attack.Now < weapon.BehaviourTimeA)
            return;

        // TF_RELOADING_CONTINUE: IncrementAmmo, one round from the reserve.
        int clipSize = ModifyClipSize(attack.Type, attack.Type.ClipSize, attack.Command.OwnerIndex);
        if (weapon.Reserve > 0 && weapon.Clip < clipSize)
        {
            weapon.Clip++;
            weapon.Reserve--;
        }

        if (weapon.Clip >= clipSize || weapon.Reserve <= 0)
        {
            // TF_RELOAD_FINISH, once there is nothing left to load (:2080).
            SendAnim(attack, WeaponAnim.ReloadFinish);

            // TF_RELOAD_FINISH: the machine's own finish (nothing left to load) ends it next command.
            EndReload(weapon, attack.Now, aborted: false);
            return;
        }

        // The next round: TF_RELOADING's sound and timer, this round's clock
        // through SetReloadTimer like stock's (UpdateReloadTimers(false), :2259),
        // and the reload again over the wait — stock replays it per shell
        // (:2013), which is what keeps the load's rhythm on the viewmodel.
        float round = ScaleReloadTime(attack.Type, mode.TimeReload, attack.Command.OwnerIndex);
        float next = weapon.BehaviourTimeA + round;
        if (attack.EffectsLive && attack.Type.ReloadSound.Length > 0)
            attack.Arms.Sound(attack.Type.ReloadSound);
        SendAnim(attack, WeaponAnim.Reload);
        weapon.BehaviourTimeA = next;
        HoldReloadPast(weapon, next, round);
    }

    private static void EndReload(WeaponNetState weapon, float now, bool aborted)
    {
        weapon.BehaviourMode &= ~ModeReloadingSingly;
        weapon.NextPrimaryAttack = now;
        if (aborted)
            weapon.InReload = false;
    }

    // The machine loads the whole clip when its reload timer runs out: keep it
    // one round past the next one, so only this behaviour loads rounds.
    private static void HoldReloadPast(WeaponNetState weapon, float nextRound, float interval)
    {
        weapon.NextPrimaryAttack = nextRound + MathF.Max(interval, 0.1f);
        weapon.TimeWeaponIdle = weapon.NextPrimaryAttack;
    }
}
