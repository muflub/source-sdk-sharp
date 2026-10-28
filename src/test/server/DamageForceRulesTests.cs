using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// WHAT A HIT DOES TO THE PHYSICS OF WHAT IT HITS, and whether the C# saying so
/// still agrees with the C++ doing it.
///
/// <para>
/// Two halves, and the second is the one that can actually go wrong. The first
/// pins <see cref="DamageForceRules"/>' own answers. The second reads
/// <c>takedamageinfo_library.cpp</c> and asserts the literals still there match
/// the constants declared here — because until the registration slot this lane
/// requested lands, the native literals are what runs, and a managed
/// declaration that has drifted from the code it describes is worse than no
/// declaration at all. See <c>fixture-must-be-checked-against-native</c>.
/// </para>
/// </summary>
public class DamageForceRulesTests
{
    //-------------------------------------------------------------------------
    // The rules themselves.
    //-------------------------------------------------------------------------

    [Fact]
    public void ABlastImpartsForce()
    {
        // THE GATE'S OWN FACT. A DMG_BLAST hit on a physics prop still throws
        // it: Blast is not in the NoPhysicsForce mask and is not Generic.
        Assert.True(DamageForceRules.ImpartsForce(DamageType.Blast));
    }

    [Fact]
    public void GenericDamageImpartsNoForce()
    {
        // Its twin. DMG_GENERIC is ZERO, so no mask test can exclude it and
        // stock needs the separate `!= DMG_GENERIC` clause -- which is also
        // what stock's own comment offers as the way to say "this damage should
        // cause no force".
        Assert.False(DamageForceRules.ImpartsForce(DamageType.Generic));
    }

    [Fact]
    public void GenericIsZeroWhichIsWhyTheSecondClauseIsNeeded()
    {
        // The check that makes the fact above mean something: if Generic were a
        // bit of its own, putting it in NoPhysicsForce would have done, and the
        // second clause really would be redundant.
        Assert.Equal(0, (int)DamageType.Generic);
    }

    [Fact]
    public void AFallImpartsNoForce()
    {
        // Through the mask rather than the Generic clause -- the other arm.
        Assert.False(DamageForceRules.ImpartsForce(DamageType.Fall));
    }

    [Fact]
    public void BulletDamageImpartsForce()
    {
        Assert.True(DamageForceRules.ImpartsForce(DamageType.Bullet));
    }

    [Fact]
    public void ABulletIsCalculatedAsABullet()
        => Assert.Equal(DamageForceRules.ForceKind.Bullet,
                        DamageForceRules.KindFor(DamageType.Bullet));

    [Fact]
    public void ABlastIsCalculatedAsAnExplosion()
        => Assert.Equal(DamageForceRules.ForceKind.Explosive,
                        DamageForceRules.KindFor(DamageType.Blast));

    [Fact]
    public void AClubIsCalculatedAsAMeleeHit()
        => Assert.Equal(DamageForceRules.ForceKind.Melee,
                        DamageForceRules.KindFor(DamageType.Club));

    [Fact]
    public void ADamageThatIsBothABulletAndABlastIsABullet()
    {
        // The ORDER of GuessDamageForce's arms, which a reader would otherwise
        // have to infer. A shotgun slug through an explosive barrel arrives as
        // exactly this pair.
        Assert.Equal(DamageForceRules.ForceKind.Bullet,
                     DamageForceRules.KindFor(DamageType.Bullet | DamageType.Blast));
    }

    [Fact]
    public void OnePointOfDamageIsWorthThreeHundredOfImpulse()
        => Assert.Equal(300f, DamageForceRules.PerDamagePoint, 3);

    [Fact]
    public void AnExplosionsForceIsCappedAtThirtyThousand()
        => Assert.Equal(30000f, DamageForceRules.ExplosionForceCeiling, 3);

    [Fact]
    public void ImpulseIsMassTimesSpeed()
        => Assert.Equal(750f, DamageForceRules.ImpulseScale(75f, 10f), 3);

    [Fact]
    public void TheBlastJitterIsCentredOnOne()
    {
        // Not a fact about the draw -- the draw is native, on the shared
        // stream. A fact about the RANGE: a jitter whose midpoint was not one
        // would be a systematic change to every blast's force wearing the word
        // "jitter".
        Assert.Equal(1f, (DamageForceRules.JitterMin + DamageForceRules.JitterMax) / 2f, 5);
    }

    //-------------------------------------------------------------------------
    // ...and the C++ that is still doing it.
    //-------------------------------------------------------------------------

    private static string NativeSource()
    {
        string path = Path.Combine(RepoLayout.SdkTree,
            "src", "sourcesharp", "game", "shared", "takedamageinfo_library.cpp");

        Assert.True(File.Exists(path), $"{path} is not there");
        return File.ReadAllText(path);
    }

    [Fact]
    public void ThePushScaleDeclarationStillDefaultsToOne()
    {
        // THE DECLARATION MOVED, plan_shim.md Phase 5 (lane 5d). This used to
        // read the default off the C++:
        //
        //     Assert.Matches(new Regex("phys_pushscale\\(\\s*\"phys_pushscale\"\\s*,\\s*\"1\""),
        //                    NativeSource());
        //
        // and that assertion is retired rather than reworded, because there is
        // no native declaration left for it to read -- it would now be a needle
        // that can only be satisfied by a regression. The claim it stood for is
        // unchanged and is checked here against the declaration's new home:
        // DamageForceRules.PushScaleDefault must agree with what
        // SourceSharp.Server/MoveVars.cs declares, whichever side of the
        // boundary that is.
        var declared = typeof(SourceSharp.Server.PhysPushScaleConVar)
            .GetCustomAttribute<ConVarAttribute>();

        Assert.True(declared is not null,
            "phys_pushscale is not declared in managed code, and it is not in C++ "
          + "either -- see docs/convar-ledger.tsv. Nothing declares it at all.");
        Assert.Equal("1", declared!.DefaultValue);
        Assert.Equal(1f, DamageForceRules.PushScaleDefault, 5);
    }

    /// <summary>
    /// The other half, and it is a debt rather than a tidy-up. The native rule
    /// is gone and NOTHING replaced it, so this constant is the only record
    /// that a physics-prop inflictor is meant to be reported by its MODEL
    /// rather than by its classname — "killed by prop_physics" names every
    /// crate, barrel and filing cabinet in the game.
    /// </summary>
    [Fact]
    public void TheDeletedRuleIsStillRecordedOnTheManagedSide()
    {
        Assert.Equal("prop_physics", DamageForceRules.ReportedByModelInstead);
    }
}
