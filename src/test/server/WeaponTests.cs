using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What makes one weapon differ from another.
///
/// The attack itself is the engine's again — CBaseCombatWeapon's state machine
/// fires, spends and reloads — so what is testable here is the table that feeds
/// it, and the table is the whole of what replaces HL2's per-weapon C++ classes.
/// </summary>
public class WeaponTests
{

    /// <summary>
    /// What a weapon launches, and how it leaves.
    ///
    /// The distinction under test is thrown versus aimed, and it is not a
    /// preference: a bolt that arced would miss where the player pointed, and a
    /// grenade that did not arc would land at their feet. Both failures look
    /// like the weapon being badly tuned rather than like the wrong branch.
    /// </summary>
    [Fact]
    public void Projectiles()
    {
        var straightUp = new SourceSharp.Abi.Vector(0f, 0f, 1f);
        var straightDown = new SourceSharp.Abi.Vector(0f, 0f, -1f);

        // FromVector, which is what points a projectile the way it is going. An
        // unpointed rocket flies sideways, which is visible and baffling.
        Assert.True(NearAngles(Angles.FromVector(new SourceSharp.Abi.Vector(1f, 0f, 0f)), 0f, 0f), "a direction along +X is zero pitch and zero yaw");

        Assert.True(NearAngles(Angles.FromVector(new SourceSharp.Abi.Vector(0f, 1f, 0f)), 0f, 90f), "a direction along +Y yaws 90");

        // Negated, because a positive pitch looks DOWN. Getting this backwards
        // is the classic Source sign error and produces a rocket that dives
        // when aimed at the sky.
        Assert.True(NearAngles(Angles.FromVector(straightDown), 90f, 0f), "a direction along -Z pitches down, not up");

        Assert.True(NearAngles(Angles.FromVector(straightUp), 270f, 0f), "straight up is 270, not -90");

        // The degenerate case. atan2(0,0) is a coin toss, so a shot at the sky
        // would yaw at random without the special case.
        Assert.True(Angles.FromVector(straightUp).Y == 0f, "straight up has a defined yaw");

        // Round trip: the angles must point back along the direction they came
        // from, which is the property anything downstream actually relies on.
        var slanted = new SourceSharp.Abi.Vector(0.6f, -0.48f, 0.64f);
        var back = Angles.Forward(Angles.FromVector(slanted));
        Assert.True(Math.Abs(back.X - slanted.X) < 0.001f &&
            Math.Abs(back.Y - slanted.Y) < 0.001f &&
            Math.Abs(back.Z - slanted.Z) < 0.001f, "angles from a direction point back along it");

        // Launch velocity: the thrown/aimed split.
        var level = Angles.Vectors(SourceSharp.Abi.Vector.Zero);

        var bolt = WeaponCatalog.Stock().For("weapon_crossbow")!.Value;
        var frag = WeaponCatalog.Stock().For("weapon_frag")!.Value;

        Assert.True(!bolt.Thrown && frag.Thrown, "the crossbow is aimed, the frag is thrown");

        var boltVelocity = WeaponLogic.LaunchVelocity(level, bolt.LaunchSpeed, bolt.Thrown);
        var fragVelocity = WeaponLogic.LaunchVelocity(level, frag.LaunchSpeed, frag.Thrown);

        Assert.True(Math.Abs(boltVelocity.Z) < 0.001f, "an aimed shot leaves flat");
        Assert.True(fragVelocity.Z > 0f, "a thrown one leaves upward");

        Assert.True(Math.Abs(boltVelocity.Length - bolt.LaunchSpeed) < 0.1f, "an aimed shot leaves at exactly its launch speed");

        // The loft must not cost the throw its speed: normalising before scaling
        // is what keeps a lofted grenade as fast as a flat one.
        Assert.True(Math.Abs(fragVelocity.Length - frag.LaunchSpeed) < 0.1f, "a thrown one leaves at its launch speed too");

        // Every weapon that names a projectile must be able to launch it.
        Assert.True(WeaponTypes.All.Where(w => w.Projectile.Length > 0)
                           .All(w => w.LaunchSpeed > 0f), "every projectile weapon has a launch speed");

        Assert.True(WeaponTypes.All.All(w => w.LaunchSpeed == 0f || w.Projectile.Length > 0), "no weapon launches something it did not name");

        // The gap this closes: both used to be hitscan.
        Assert.True(WeaponCatalog.Stock().For("weapon_crossbow")!.Value.Projectile == EntityNames.SsBolt &&
            WeaponCatalog.Stock().For("weapon_rpg")!.Value.Projectile == EntityNames.SsRocket, "the crossbow and the RPG launch rather than trace");

        SecondaryFire();
    }

    /// <summary>
    /// The other trigger.
    ///
    /// Most of what is checked here is that a secondary can be PAID for, since
    /// every way of getting that wrong is quiet: an alt-fire that spends the
    /// wrong pool, or fires without spending anything, plays and sounds exactly
    /// like one that works.
    /// </summary>
    private static void SecondaryFire()
    {
        var withSecondary = WeaponTypes.All.Where(w => w.Secondary is not null).ToArray();

        Assert.True(withSecondary.Length >= 3, "some weapons have a second trigger");

        // Not all of them, deliberately: a pistol's right mouse button does
        // nothing in Half-Life either, and inventing one for every weapon would
        // be worse than leaving it.
        Assert.True(withSecondary.Length < WeaponTypes.All.Length, "not every weapon has one");

        // Every alt-fire must DO something. One that neither shoots nor
        // launches spends ammunition and produces nothing, which reads as the
        // button being broken.
        Assert.True(withSecondary.All(w => w.Secondary!.Value.Projectile.Length > 0
                                || w.Secondary!.Value.Pellets > 0), "every second trigger either shoots or launches");

        Assert.True(withSecondary.All(w => w.Secondary!.Value.LaunchSpeed == 0f
                                || w.Secondary!.Value.Projectile.Length > 0), "no second trigger launches something it did not name");

        // A zero interval is a hold-to-empty-the-pool button.
        Assert.True(withSecondary.All(w => w.Secondary!.Value.Interval > 0f), "every second trigger costs time");

        Assert.True(withSecondary.All(w => w.Secondary!.Value.AmmoCost > 0), "every second trigger costs ammunition");

        var shotgun = WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Secondary!.Value;
        var smg = WeaponCatalog.Stock().For("weapon_smg1")!.Value.Secondary!.Value;

        // The shotgun is the one that draws on the clip, and the reason
        // FromClip exists: its double blast is the same buckshot the primary
        // fires, spent two shells at a time.
        Assert.True(shotgun.FromClip, "the shotgun's double blast comes out of the clip");
        Assert.True(shotgun.AmmoCost > 1, "it costs more than one shell");

        // Worth more than it looks: a double blast that were merely twice as
        // strong would have no drawback, and nobody would ever use the primary.
        Assert.True(shotgun.Spread > WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Spread, "the double blast is wider than the single");

        Assert.True(shotgun.Pellets > WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Pellets, "it fires more pellets than the single");

        // Everything else draws on a pool the primary never touches, so a full
        // clip says nothing about whether the alt-fire can fire.
        Assert.True(!smg.FromClip, "the SMG's grenade does not come out of its clip");
        Assert.True(smg.Thrown, "it throws rather than aims");

        // The launch machinery is shared with the primary, which is the point:
        // a grenade is a grenade whichever trigger sent it.
        var level = Angles.Vectors(SourceSharp.Abi.Vector.Zero);

        Assert.True(WeaponLogic.LaunchVelocity(level, smg.LaunchSpeed, smg.Thrown).Z > 0f, "a thrown second trigger lofts like a thrown primary");

        var ar2 = WeaponCatalog.Stock().For("weapon_ar2")!.Value.Secondary!.Value;

        Assert.True(Math.Abs(WeaponLogic.LaunchVelocity(level, ar2.LaunchSpeed, ar2.Thrown).Z) < 0.001f, "an aimed second trigger leaves flat");
    }

    private static bool NearAngles(SourceSharp.Abi.Vector angles, float pitch, float yaw) =>
        Math.Abs(angles.X - pitch) < 0.001f &&
        Math.Abs(angles.Y - yaw) < 0.001f &&
        angles.Z == 0f;

    /// <summary>
    /// The catch-up loop, which is the one piece of the state machine that is
    /// arithmetic rather than engine calls.
    ///
    /// It is what keeps rate of fire independent of framerate: on a slow frame a
    /// fast weapon owes more than one shot. Getting it wrong is not loud — every
    /// automatic weapon simply fires slower on a busy server than on an idle
    /// one, which reads as the server being laggy rather than as a bug.
    /// </summary>
    [Fact]
    public void RateOfFire()
    {
        // Not due yet: nothing owed.
        Assert.True(WeaponLogic.ShotsOwed(nextAttack: 10f, now: 9f, interval: 0.1f) == 0, "a weapon on cooldown owes nothing");

        // Exactly due, or a fraction past: one.
        Assert.True(WeaponLogic.ShotsOwed(10f, 10f, 0.1f) == 1, "a weapon exactly due owes one");
        Assert.True(WeaponLogic.ShotsOwed(10f, 10.05f, 0.1f) == 1, "a fraction of an interval past still owes one");

        // Three intervals late owes four — the one due plus the three missed.
        Assert.True(WeaponLogic.ShotsOwed(10f, 10.3f, 0.1f) == 4, "a slow frame owes the shots it missed");

        // The bound matters: a weapon deployed but never fired has a deadline of
        // zero, and without a ceiling it would owe one shot per interval since
        // the map loaded.
        Assert.True(WeaponLogic.ShotsOwed(0f, 600f, 0.1f) == WeaponLogic.MaxShotsPerFrame, "the catch-up is bounded");

        // A zero interval is a config mistake, not a machine gun.
        Assert.True(WeaponLogic.ShotsOwed(10f, 20f, 0f) == 1, "a zero interval owes exactly one");

        // The deadline advances by whole intervals from the LAST one rather than
        // from now, or a fast weapon loses a fraction every frame and ends up
        // firing slower than its own rate.
        Assert.True(Math.Abs(WeaponLogic.NextAttackAfter(10f, 10.05f, 0.1f) - 10.1f) < 0.0001f, "the next deadline advances by whole intervals");

        Assert.True(WeaponLogic.NextAttackAfter(10f, 10.25f, 0.1f) > 10.25f, "and keeps advancing while it is behind");

        // A deadline too far behind to catch up is reset rather than left owing
        // shots forever.
        Assert.True(WeaponLogic.NextAttackAfter(0f, 600f, 0.1f) > 600f, "a hopeless deadline is reset from now");
    }

    [Fact]
    public void Weapons()
    {
        Assert.True(WeaponCatalog.Stock().For("weapon_shotgun") is not null, "a known weapon has an entry");

        // The table is the ONLY place the mod says what weapons it has: each row
        // claims its own classname for the weapon shell at startup, and there is
        // no second list in C++ any more. So the nine names being here is not
        // decoration — a name missing from this array is a weapon that does not
        // exist, and the symptom is "unknown entity" rather than anything about
        // rates or spread.
        string[] expected =
        {
            EntityNames.Weapon357, EntityNames.WeaponAr2, EntityNames.WeaponCrossbow,
            EntityNames.WeaponCrowbar, EntityNames.WeaponFrag, EntityNames.WeaponPistol,
            EntityNames.WeaponRpg, EntityNames.WeaponShotgun, EntityNames.WeaponSmg1,
        };

        foreach (string name in expected)
            Assert.True(WeaponCatalog.Stock().For(name) is not null, $"{name} is declared");

        // And nothing beyond them, because a row nobody expected is a classname
        // claimed by the shell that no loadout, map or script asks for.
        Assert.True(WeaponTypes.All.Length == expected.Length, "the table declares those nine and no others");

        // A duplicate row would register the same classname twice. Native
        // refuses the second install rather than asserting, so the tell would be
        // the SECOND row's fire rate quietly winning the type lookup while the
        // first row's is the one the factory was made from.
        Assert.True(WeaponTypes.All.Select(w => w.Name)
                           .Distinct(StringComparer.OrdinalIgnoreCase)
                           .Count() == WeaponTypes.All.Length, "no classname is declared twice");

        // The shotgun is the only reason the pellet count exists: stock puts it
        // in CWeaponShotgun, and there is no per-weapon class here to put it in.
        Assert.True(WeaponCatalog.Stock().For("weapon_shotgun")!.Value.Pellets == 7, "the shotgun fires more than one pellet");

        Assert.True(WeaponTypes.All.Where(w => w.Name != "weapon_shotgun").All(w => w.Pellets == 1), "everything else fires one");

        // Case comes from the engine and from map keyvalues, and those do not
        // agree as reliably as one would like.
        Assert.True(WeaponCatalog.Stock().For("WEAPON_SHOTGUN") is not null, "the lookup ignores case");

        // An unregistered weapon is not an error: the shell falls back to the
        // base class, which is what a generic weapon should do.
        Assert.True(WeaponCatalog.Stock().For("weapon_nonesuch") is null, "an unknown weapon has no entry");
        Assert.True(WeaponCatalog.Stock().For("") is null, "an empty name has no entry");

        // Every weapon must be able to fire at all. A zero interval would fire
        // once per tick, and a zero pellet count would fire nothing.
        foreach (var weapon in WeaponTypes.All)
        {
            Assert.True(weapon.Interval > 0f && weapon.Pellets >= 1 && weapon.Spread >= 0f, $"{weapon.Name} can be fired");
        }

        // Two weapons on the same ammo must differ by something, or naming them
        // separately buys nothing.
        var pistol = WeaponCatalog.Stock().For("weapon_pistol")!.Value;
        var smg = WeaponCatalog.Stock().For("weapon_smg1")!.Value;

        Assert.True(pistol.Interval != smg.Interval || pistol.Spread != smg.Spread, "weapons sharing a damage source still differ");
    }

    /// <summary>
    /// Damage belongs to the ammunition, not to the weapon — which is how stock
    /// does it, and it is the reason a weapon entry has no damage field.
    /// </summary>
    [Fact]
    public void Ammunition()
    {
        // Zero damage was what shipped before this, and it is worth a test
        // precisely because nothing complains: every bullet in the game simply
        // did nothing.
        Assert.True(AmmoTypes.All.Where(a => a.DamageType == SourceSharp.Abi.DamageType.Bullet)
                         .All(a => a.Damage > 0f), "every ammo type a weapon fires does damage");

        Assert.True(AmmoTypes.All.Select(a => a.Damage).Distinct().Count() > 1, "ammo types differ in damage");

        Assert.True(AmmoTypes.All.All(a => a.MaxCarry > 0), "carry limits are still positive");
    }
}
