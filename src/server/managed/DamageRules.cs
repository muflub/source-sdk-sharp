//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// Which kinds of damage behave which way — five bitmasks that used to be
/// <c>CMultiplayRules</c>'s and are policy, not mechanism.
///
/// <para>
/// <b>Why these are a decision and not plumbing.</b> Stock spells each of them
/// as a literal OR of <c>DMG_*</c> flags inside a method body: acid keeps
/// hurting you over time, a fall applies no physics force, a crowbar can gib a
/// corpse. Every one of those is a sentence about how this mod's combat feels,
/// and a mod that wanted burning to stop being time-based had to edit C++ to
/// say so. They are here now, and <c>shell_gamerules.cpp</c> reads them.
/// </para>
///
/// <para>
/// <b>Pushed once, not asked per call</b>, following
/// the <c>RegisterMovementParams</c> slot: these are constants, the
/// native side reads them from damage paths that run per hit, and a crossing
/// per hit to fetch a value that never changes is the shape
/// <c>plan_server_all.md</c> §3 says to avoid. The native struct carries stock's
/// values as its defaults, so a build where managed code never registers
/// behaves exactly like Half-Life rather than treating no damage as special.
/// </para>
///
/// <para>
/// <b>One mask, not two.</b> Stock has both <c>Damage_GetTimeBased()</c>, which
/// returns the mask, and <c>Damage_IsTimeBased(t)</c>, which re-spells the same
/// seven flags inline instead of calling it — two copies of one list, free to
/// drift, and nothing would have reported it if they had. Here there is one
/// list and the test asks it both ways.
/// </para>
/// </summary>
public static class DamageRules
{
    /// <summary>
    /// Damage applied a tick at a time rather than all at once.
    ///
    /// <para>
    /// Stock: <c>DMG_PARALYZE | DMG_NERVEGAS | DMG_POISON | DMG_RADIATION |
    /// DMG_DROWNRECOVER | DMG_ACID | DMG_SLOWBURN</c>. DrownRecover is on the
    /// list because being applied over time is what it needs; that what it
    /// applies is negative is not this list's business.
    /// </para>
    /// </summary>
    public const DamageType TimeBased =
        DamageType.Paralyze | DamageType.NerveGas | DamageType.Poison |
        DamageType.Radiation | DamageType.DrownRecover | DamageType.Acid |
        DamageType.SlowBurn;

    /// <summary>
    /// Damage that need not supply a physics force and position.
    ///
    /// <para>
    /// Stock: <c>DMG_FALL | DMG_BURN | DMG_PLASMA | DMG_DROWN | (time-based) |
    /// DMG_CRUSH | DMG_PHYSGUN | DMG_PREVENT_PHYSICS_FORCE</c>. Note it
    /// INCLUDES the time-based set rather than repeating it, and that
    /// composition is kept: adding a flag to <see cref="TimeBased"/> adds it
    /// here too, which is what stock does and is easy to lose when copying.
    /// </para>
    /// </summary>
    public const DamageType NoPhysicsForce =
        DamageType.Fall | DamageType.Burn | DamageType.Plasma |
        DamageType.Drown | TimeBased | DamageType.Crush |
        DamageType.PhysGun | DamageType.PreventPhysicsForce;

    /// <summary>
    /// Damage violent enough to gib a corpse.
    ///
    /// <para>Stock: <c>DMG_CRUSH | DMG_FALL | DMG_BLAST | DMG_SONIC | DMG_CLUB</c>.</para>
    /// </summary>
    public const DamageType ShouldGibCorpse =
        DamageType.Crush | DamageType.Fall | DamageType.Blast |
        DamageType.Sonic | DamageType.Club;

    /// <summary>
    /// Damage that draws no blood.
    ///
    /// <para>Stock: <c>DMG_POISON | DMG_ACID</c>.</para>
    /// </summary>
    public const DamageType ShouldNotBleed = DamageType.Poison | DamageType.Acid;

    /// <summary>
    /// Damage the HUD shows a directional indicator for.
    ///
    /// <para>
    /// Stock: <c>DMG_POISON | DMG_ACID | DMG_DROWN | DMG_BURN | DMG_SLOWBURN |
    /// DMG_NERVEGAS | DMG_RADIATION | DMG_SHOCK</c>, read from
    /// <c>multiplay_gamerules.cpp</c>:187 rather than remembered — and it is
    /// worth saying that this branch has no freeze flags for it to mention,
    /// because other Source branches put <c>DMG_FREEZE</c> and
    /// <c>DMG_SLOWFREEZE</c> in this very mask and a reader coming from one of
    /// those would look for them.
    /// </para>
    /// </summary>
    public const DamageType ShowOnHud =
        DamageType.Poison | DamageType.Acid | DamageType.Drown |
        DamageType.Burn | DamageType.SlowBurn | DamageType.NerveGas |
        DamageType.Radiation | DamageType.Shock;

    /// <summary>
    /// Hands them to the native side. Called at server init, before any damage
    /// can be dealt.
    /// </summary>
    public static void Register(IEngineDamage damage)
    {
        damage.RegisterRules(new DamageRuleMasks
        {
            TimeBased       = (int)TimeBased,
            NoPhysicsForce  = (int)NoPhysicsForce,
            ShouldGibCorpse = (int)ShouldGibCorpse,
            ShouldNotBleed  = (int)ShouldNotBleed,
            ShowOnHud       = (int)ShowOnHud,
        });
    }
}

/// <summary>
/// WHAT A HIT DOES TO THE PHYSICS OF THE THING IT HITS.
///
/// <para>
/// <c>takedamageinfo_library.cpp</c>'s four force calculators, the rule that
/// picks between them, and the three numbers they stand on. Every one of them
/// is a sentence about how this mod's combat feels — how hard a blast throws a
/// ragdoll, how much a crowbar shoves a crate, whether a fall pushes anything
/// at all — and every one was a literal inside a C++ function body.
/// </para>
///
/// <para>
/// <b>Declared here, not yet pushed.</b> The calculators are called per hit
/// from paths that run before the managed runtime is up, so the values reach
/// them the way <see cref="DamageRules"/>' masks do: a registered table with
/// stock's numbers as the native defaults. That registration needs an ABI slot
/// this lane may not append —
/// <c>.claude/entity-port/abi-requests/policy-rules.md</c> asks for it. Until
/// it lands the native literals are still what runs, and
/// <c>DamageForceRulesTests</c> asserts that they and these AGREE, by reading
/// <c>takedamageinfo_library.cpp</c>. A declaration that drifts from the code
/// it describes is worse than no declaration, and this is the check that
/// notices.
/// </para>
/// </summary>
public static class DamageForceRules
{
    /// <summary>
    /// Stock's <c>ImpulseScale</c>: the impulse needed to push
    /// <paramref name="targetMass"/> kilogrammes at
    /// <paramref name="desiredSpeed"/> inches per second. Momentum, so it is
    /// the product — named rather than multiplied inline because the two calls
    /// below read as sentences with it and as magic numbers without it.
    /// </summary>
    public static float ImpulseScale(float targetMass, float desiredSpeed)
        => targetMass * desiredSpeed;

    /// <summary>
    /// The impulse one point of damage is worth: enough to push a 75kg man
    /// 4 in/sec. <c>ImpulseScale( 75, 4 )</c> = 300.
    /// </summary>
    public static float PerDamagePoint => ImpulseScale(75f, 4f);

    /// <summary>
    /// The ceiling on an explosion's force: enough to push a 75kg man
    /// 400 in/sec, and no faster however large the blast.
    /// <c>ImpulseScale( 75, 400 )</c> = 30000.
    ///
    /// <para>
    /// It is what stops a big enough explosion flinging a body at a speed the
    /// solver cannot integrate. Melee and bullet forces are NOT clamped, which
    /// is stock's asymmetry and is kept: neither scales with a radius.
    /// </para>
    /// </summary>
    public static float ExplosionForceCeiling => ImpulseScale(75f, 400f);

    /// <summary>
    /// The per-victim jitter on a blast's force, as a closed range.
    ///
    /// <para>
    /// Stock's <c>random-&gt;RandomFloat( 0.85, 1.15 )</c>, and its own comment
    /// says why: it stands in for bodyweight and the other person-to-person
    /// variables that are identical for every character sharing a model, so a
    /// crowd caught in one explosion does not fly apart in formation.
    /// </para>
    ///
    /// <para>
    /// <b>The DRAW stays native and must.</b> It comes off the server's shared
    /// stream, which demo playback and prediction both have to reproduce; the
    /// managed side has no business being a source of those. What is declared
    /// here is the RANGE, which is the decision.
    /// </para>
    /// </summary>
    public const float JitterMin = 0.85f;

    /// <inheritdoc cref="JitterMin"/>
    public const float JitterMax = 1.15f;

    /// <summary>
    /// <c>phys_pushscale</c>'s default — every damage force is multiplied by it.
    /// One at stock, which is to say the knob is off.
    /// </summary>
    public const float PushScaleDefault = 1f;

    /// <summary>
    /// Which calculator a damage type gets when nobody supplied a force —
    /// stock's <c>GuessDamageForce</c>, whose own comment says it is for
    /// "mapmaker specified damages" and should not be used where the force is
    /// known.
    /// </summary>
    public enum ForceKind
    {
        /// <summary>The ammo type's own <c>DamageForce</c>, along the shot.</summary>
        Bullet,

        /// <summary>Scaled by damage, clamped and jittered.</summary>
        Explosive,

        /// <summary>Scaled by damage, neither clamped nor jittered.</summary>
        Melee,
    }

    /// <summary>
    /// <c>GuessDamageForce</c>'s selection, in stock's order: bullets first,
    /// blasts second, everything else a melee shove.
    ///
    /// <para>
    /// The ORDER is the rule, not a formality. A damage that is both
    /// <c>DMG_BULLET</c> and <c>DMG_BLAST</c> — a shotgun slug through an
    /// explosive barrel arrives as exactly that — is a bullet here, and would
    /// be an explosion if the two arms were swapped.
    /// </para>
    /// </summary>
    public static ForceKind KindFor(DamageType type)
    {
        if ((type & DamageType.Bullet) != 0)
            return ForceKind.Bullet;

        if ((type & DamageType.Blast) != 0)
            return ForceKind.Explosive;

        return ForceKind.Melee;
    }

    /// <summary>
    /// A bullet's physics force: stock's <c>CalculateBulletDamageForce</c>
    /// (takedamageinfo.cpp:332-342), the shot's direction normalised and
    /// scaled by the ammo type's <c>DamageForce</c>, <c>phys_pushscale</c> and
    /// the caller's scale. It acts at the trace's end (plan_physics_managed A30).
    /// </summary>
    public static Vector BulletForce(Vector direction, float ammoDamageForce, float pushScale = PushScaleDefault, float scale = 1f)
        => direction.Normalized * (ammoDamageForce * pushScale * scale);

    /// <summary>
    /// A melee hit's physics force: stock's <c>CalculateMeleeDamageForce</c>
    /// (takedamageinfo.cpp:347-359), enough to push a 75 kg man 4 in/s per
    /// point of base damage, along the swing, times <c>phys_pushscale</c> and the
    /// caller's scale (plan_physics_managed A30).
    /// </summary>
    public static Vector MeleeForce(Vector direction, float damage, float pushScale = PushScaleDefault, float scale = 1f)
        => direction.Normalized * (damage * PerDamagePoint * pushScale * scale);

    /// <summary>
    /// Whether accumulated damage of this type pushes what it hits at all —
    /// stock's <c>AddMultiDamage</c> guard,
    /// <c>!Damage_NoPhysicsForce( t ) &amp;&amp; t != DMG_GENERIC</c>.
    ///
    /// <para>
    /// <b>Two conditions and neither is redundant.</b> The first is
    /// <see cref="DamageRules.NoPhysicsForce"/>, the mask of kinds that need
    /// not supply a force — a fall, a burn, a drowning. The second excludes
    /// <c>DMG_GENERIC</c>, which is NOT in that mask and could not be: it is
    /// zero, so a mask test against it is true of everything. Stock's own
    /// comment offers <c>DMG_GENERIC</c> as the way to say "this damage should
    /// cause no force", and that only works because of this second clause.
    /// </para>
    /// </summary>
    public static bool ImpartsForce(DamageType type)
        => (type & DamageRules.NoPhysicsForce) == 0 && type != DamageType.Generic;

    /// <summary>
    /// The classname a rule used to be keyed on — the LAST one in the shim,
    /// and now the only place the rule exists at all.
    ///
    /// <para>
    /// It answers "what killed me" for the death message. An inflictor that is
    /// a physics prop is reported by its MODEL rather than by its classname,
    /// because "killed by prop_physics" names every crate, barrel and filing
    /// cabinet in the game and tells the reader nothing.
    /// </para>
    ///
    /// <para>
    /// <b>The native site is gone.</b> It was
    /// <c>Q_strcmp( pszAmmoType, "prop_physics" )</c> inside
    /// <c>CTakeDamageInfo::GetAmmoName</c>, and <c>plan_shim.md</c> §5 3f
    /// deleted that function: 0 importers on either link line and no caller
    /// inside its own translation unit. So the shim audit's count of
    /// classname-keyed rules is 0 rather than 1, and this constant stopped
    /// being a pointer at native code and became the rule's only record.
    /// </para>
    ///
    /// <para>
    /// <b>That makes it a debt rather than a note.</b> Nothing answers "what
    /// killed me" today — not here and not natively — so a death by physics
    /// prop has no death-message rule to apply. Implementing it needs the
    /// inflictor's model name on the managed side of a death, which is weapons
    /// work rather than rules work. Recorded, not done, and now with nothing
    /// covering for it.
    /// </para>
    /// </summary>
    public const string ReportedByModelInstead = "prop_physics";
}
