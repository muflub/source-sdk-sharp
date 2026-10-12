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
    public void TheNativeExplosionCeilingIsStillThirtyThousand()
    {
        // ImpulseScale( 75, 400 ). Matched as the CALL rather than as the
        // product, because that is how the C++ spells it -- and a test needling
        // for "30000" would pass over a file that had stopped containing it.
        Assert.Matches(new Regex(@"ImpulseScale\(\s*75\s*,\s*400\s*\)"), NativeSource());
        Assert.Equal(30000f, DamageForceRules.ExplosionForceCeiling, 3);
    }

    [Fact]
    public void TheNativePerDamageImpulseIsStillThreeHundred()
    {
        Assert.Matches(new Regex(@"ImpulseScale\(\s*75\s*,\s*4\s*\)"), NativeSource());
        Assert.Equal(300f, DamageForceRules.PerDamagePoint, 3);
    }

    [Fact]
    public void TheNativeBlastJitterIsStillTheSameRange()
    {
        Assert.Matches(new Regex(@"RandomFloat\(\s*0\.85\s*,\s*1\.15\s*\)"), NativeSource());
        Assert.Equal(0.85f, DamageForceRules.JitterMin, 5);
        Assert.Equal(1.15f, DamageForceRules.JitterMax, 5);
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

    [Fact]
    public void ThePushScaleDeclarationIsNotAlsoStillInTheCpp()
    {
        // The other half. Two ConVars under one name is neither a compile error
        // nor a link error, and neither the console nor the game says which one
        // it read -- so "it is declared in C# now" is only half a claim.
        Assert.DoesNotMatch(new Regex("ConVar\\s+phys_pushscale\\s*\\("), NativeSource());
    }

    [Fact]
    public void TheNativeGuessIsGoneSoKindForIsTheOnlyRule()
    {
        // WAS TheNativeGuessStillPicksBulletBeforeBlast, which read the order
        // KindFor reproduces (DMG_BULLET before DMG_BLAST) off the C++. The
        // server gc cull deleted GuessDamageForce: server.so's link discarded it
        // and the client never compiled it. The order is pinned on the managed
        // side by ADamageThatIsBothABulletAndABlastIsABullet; this asserts the
        // native copy stays gone, so restoring it means coming back here.
        string s = NativeSource();

        // Positive control: the file is read and still holds the blast rule.
        Assert.Contains("void CalculateExplosiveDamageForce(", s);
        Assert.DoesNotContain("void GuessDamageForce(", s);
    }

    /// <summary>
    /// WAS <c>TheOneClassnameKeyedRuleIsStillTheOnlyOne</c>, and the count it
    /// pinned is 0 now rather than 1.
    ///
    /// <para>
    /// The shim's one classname-keyed rule was
    /// <c>Q_strcmp( pszAmmoType, "prop_physics" )</c> inside
    /// <c>CTakeDamageInfo::GetAmmoName</c>. <c>plan_shim.md</c> §5 3f deleted
    /// that function — 0 importers on either link line and no caller inside its
    /// own translation unit — so the literal went with it.
    /// </para>
    ///
    /// <para>
    /// A count that falls to zero is where a fact usually stops being a fact
    /// quietly, so the successor asserts BOTH halves: the native file compares
    /// no classname at all, AND the managed constant recording what the rule
    /// WAS is still there. Delete the constant and the second fails while the
    /// first still passes.
    /// </para>
    /// </summary>
    [Fact]
    public void TheClassnameKeyedRuleIsGoneFromTheNativeFile()
    {
        string s = NativeSource();

        // Positive control: this is still takedamageinfo_library.cpp and it
        // still holds the calculators this class is about. Without it the
        // absence below is satisfied by an unreadable file.
        Assert.Contains("void CalculateExplosiveDamageForce(", s);

        var comparisons = Regex.Matches(s, @"Q_strcmp\(\s*pszAmmoType\s*,\s*""[^""]+""");
        Assert.True(comparisons.Count == 0,
            $"takedamageinfo_library.cpp compares the ammo name against " +
            $"{comparisons.Count} classname literals again. The shim audit's " +
            "count of classname-keyed rules was driven to 0 when GetAmmoName " +
            "went; a new one is a new finding, not a restoration.");
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
