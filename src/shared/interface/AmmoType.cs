namespace SourceSharp.Abi;

/// <summary>
/// Source's DMG_* damage bits, from shareddefs.h.
///
/// <para>
/// A genuine bit field rather than a list of kinds: one blow carries several of
/// these at once, and the code that reads them asks about a SET -- glass goes
/// down whole to blast, crush or slash, and a cannister is finished off by
/// bullet, buckshot, burn or blast. <see cref="Armour.Unprotected"/> is four of
/// them ORed together and was already being used as a mask while the members
/// were loose ints.
/// </para>
///
/// <para>
/// Only the ones this mod actually uses are named, rather than mirroring the
/// whole set and implying support for the rest. Values are shareddefs.h's and
/// must not be renumbered: they cross the ABI into the engine's own damage
/// path, and a map's filter_damage_type writes the raw number as a keyvalue.
/// </para>
/// </summary>
[Flags]
public enum DamageType
{
    /// <summary>
    /// No bit set. shareddefs.h calls this DMG_GENERIC and it is 0 there too --
    /// ordinary damage that names no kind, which is what an entity gets when
    /// nothing says otherwise.
    /// </summary>
    Generic     = 0b00000000_00000000_00000000_00000000,

    /// <inheritdoc cref="Generic"/>
    None        = Generic,

    /// <summary>Being squashed — a door closing on you, or a sheet of glass
    /// taken down all at once rather than a pane at a time.</summary>
    Crush       = 0b00000000_00000000_00000000_00000001,

    Bullet      = 0b00000000_00000000_00000000_00000010,
    Slash       = 0b00000000_00000000_00000000_00000100,

    /// <summary>Heat. What a fire deals, and what sets things alight.</summary>
    Burn        = 0b00000000_00000000_00000000_00001000,

    Fall        = 0b00000000_00000000_00000000_00100000,

    /// <summary>Explosives — grenades, rockets, the AR2's alt fire.</summary>
    Blast       = 0b00000000_00000000_00000000_01000000,

    Club        = 0b00000000_00000000_00000000_10000000,
    NeverGib    = 0b00000000_00000000_00010000_00000000,  // DMG_NEVERGIB, shareddefs.h -- 0x1000

    /// <summary>A laser or other high-energy beam. What env_laser deals.</summary>
    EnergyBeam  = 0b00000000_00000000_00000100_00000000,  // DMG_ENERGYBEAM, shareddefs.h -- 0x400

    Drown       = 0b00000000_00000000_01000000_00000000,  // DMG_DROWN, shareddefs.h -- 0x4000
    Poison      = 0b00000000_00000010_00000000_00000000,  // DMG_POISON, shareddefs.h -- 0x20000
    Radiation   = 0b00000000_00000100_00000000_00000000,  // DMG_RADIATION, shareddefs.h -- 0x40000

    /// <summary>
    /// A blast on the SURFACE of water, which cannot reach anything under it.
    /// DMG_BLAST_SURFACE, shareddefs.h -- 1&lt;&lt;27.
    ///
    /// <para>
    /// Additive rather than a kind of its own: it is OR'd onto whatever the
    /// blast already was, and env_explosion's SURFACEONLY spawnflag is the one
    /// thing that sets it.
    /// </para>
    /// </summary>
    BlastSurface = 0b00001000_00000000_00000000_00000000,

    /// <summary>The Cremator's fire, which env_fire's plasma type also deals.</summary>
    Plasma      = 0b00000001_00000000_00000000_00000000,  // DMG_PLASMA, shareddefs.h -- 0x1000000

    /// <summary>DMG_DISSOLVE, shareddefs.h -- 0x4000000: physics impact from a dissolving object.</summary>
    Dissolve    = 0b00000100_00000000_00000000_00000000,

    //-------------------------------------------------------------------------
    // The kinds the GAME RULES sort by, rather than the ones a weapon deals.
    //
    // These arrived with the damage-rules port: CMultiplayRules answered
    // "which damage is time-based", "which needs no physics force" and three
    // more by spelling a bitmask inline, and those masks are policy -- a mod
    // deciding that acid keeps hurting you and a bullet does not. Policy is
    // C#'s, so the masks moved (see DamageRules), and a mask needs a name for
    // every bit it mentions.
    //
    // Every value below is DMG_* from shareddefs.h, and DamageRulesTests reads
    // that header rather than trusting this comment.
    //-------------------------------------------------------------------------

    /// <summary>Slows the affected creature down. DMG_PARALYZE, 1&lt;&lt;15.</summary>
    Paralyze    = 1 << 15,

    /// <summary>Nerve toxins, "very bad" in stock's own words. DMG_NERVEGAS, 1&lt;&lt;16.</summary>
    NerveGas    = 1 << 16,

    /// <summary>
    /// Drowning recovery -- the damage type that HEALS you back up after you
    /// surface. DMG_DROWNRECOVER, 1&lt;&lt;19.
    ///
    /// <para>
    /// It is on the time-based list because being applied a tick at a time is
    /// what it needs; that what it applies is negative is not that list's
    /// business, and it is the one entry that makes the list's name read oddly.
    /// </para>
    /// </summary>
    DrownRecover = 1 << 19,

    /// <summary>Toxic chemicals or acid burns. DMG_ACID, 1&lt;&lt;20.</summary>
    Acid        = 1 << 20,

    /// <summary>In an oven. DMG_SLOWBURN, 1&lt;&lt;21.</summary>
    SlowBurn    = 1 << 21,

    /// <summary>
    /// Hit by the physics manipulator. Usually deals no damage at all --
    /// stock's own comment -- and it is on the no-physics-force list because
    /// the gravity gun is already applying the force itself.
    /// DMG_PHYSGUN, 1&lt;&lt;23.
    /// </summary>
    PhysGun     = 1 << 23,

    /// <summary>
    /// Suppresses the physics force this damage would otherwise apply.
    /// DMG_PREVENT_PHYSICS_FORCE, 1&lt;&lt;11.
    ///
    /// <para>
    /// A MODIFIER rather than a kind: it is OR'd onto some other type to say
    /// "hurt it but do not push it", which is why it appears in the
    /// no-physics-force mask beside genuine kinds.
    /// </para>
    /// </summary>
    PreventPhysicsForce = 1 << 11,

    /// <summary>
    /// Gib the victim whatever killed them. DMG_ALWAYSGIB, shareddefs.h 1&lt;&lt;13.
    ///
    /// <para>
    /// A MODIFIER, like <see cref="PreventPhysicsForce"/> above: it is OR'd
    /// onto a kind rather than being one. What reads it is the death rule, and
    /// the damage TYPE is what that rule reads — so a suicide that claimed to
    /// be generic would be a different death from the one that was asked for,
    /// with a different effect and a different body left behind.
    /// </para>
    ///
    /// <para>
    /// Its one caller is <c>explode</c>, which is <c>kill</c> with
    /// <see cref="Blast"/> and this bit on it — the whole difference between
    /// the two commands in stock too (<c>kill_helper</c>'s <c>bExplode</c>
    /// picks the damage type <c>CommitSuicide</c> passes).
    /// </para>
    /// </summary>
    AlwaysGib   = 1 << 13,

    /// <summary>A sound pulse shockwave. DMG_SONIC, 1&lt;&lt;9.</summary>
    Sonic       = 1 << 9,

    /// <summary>Electric shock. DMG_SHOCK, 1&lt;&lt;8.</summary>
    Shock       = 1 << 8,

    Buckshot    = 0b00100000_00000000_00000000_00000000,  // DMG_BUCKSHOT, shareddefs.h -- 0x20000000

    /// <summary>
    /// Hurt AT the point of contact rather than through the falloff of a blast
    /// — DMG_DIRECT.
    ///
    /// It is what <c>CEntityFlame</c> adds to the damage it deals to the thing
    /// it is actually attached to, so that the burn rate does not depend on
    /// where the flame happens to sit relative to the body target.
    /// </summary>
    Direct      = 0b00010000_00000000_00000000_00000000,  // DMG_DIRECT, shareddefs.h -- 1<<28
}
