using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The damage-type bits the five masks are built from, held against
/// <c>shareddefs.h</c> (which the build still compiles), and the one
/// composition between two of the masks.
///
/// <para>
/// The masks themselves against <c>CMultiplayRules</c>' method bodies in
/// <c>multiplay_gamerules.cpp</c> are oracles: that file is no longer compiled
/// by either DLL, so the farm no longer links it, and those five facts live in
/// SourceSharp.OracleTests (DamageRulesOracleTests), which reads the pristine
/// upstream clone.
/// </para>
/// </summary>
public class DamageRulesTests
{
    private const string SharedDefs = "src/game/shared/shareddefs.h";

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoLayout.SdkTree, relative));

    /// <summary>Every <c>#define DMG_X (1 &lt;&lt; n)</c> in shareddefs.h, by name.</summary>
    private static Dictionary<string, int> DmgFlags()
    {
        var text = Read(SharedDefs);
        var flags = new Dictionary<string, int>();

        foreach (Match m in Regex.Matches(text,
            @"^#define\s+(?<name>DMG_\w+)\s+\(\s*1\s*<<\s*(?<bit>\d+)\s*\)", RegexOptions.Multiline))
        {
            flags[m.Groups["name"].Value] = 1 << int.Parse(m.Groups["bit"].Value);
        }

        return flags;
    }

    /// <summary>
    /// NoPhysicsForce really does CONTAIN TimeBased, which is the composition
    /// that is easy to lose when copying a mask into a new language.
    /// </summary>
    [Fact]
    public void NoPhysicsForceContainsEveryTimeBasedKind()
    {
        Assert.True((DamageRules.NoPhysicsForce & DamageRules.TimeBased) == DamageRules.TimeBased,
            "NoPhysicsForce no longer contains the whole of TimeBased. Stock ORs "
            + "Damage_GetTimeBased() into it rather than repeating the flags, so "
            + "adding a kind to TimeBased must add it here too.");
    }

    /// <summary>
    /// The DamageType values this port declares are the bits shareddefs.h
    /// defines — checked for every flag the five masks mention, because a mask
    /// that is right about which NAMES it lists and wrong about one of their
    /// VALUES is wrong in a way no name comparison can see.
    /// </summary>
    [Fact]
    public void EveryFlagTheseMasksNameHasTheValueSharedDefsGivesIt()
    {
        var flags = DmgFlags();

        (string Name, DamageType Value)[] used =
        {
            ("DMG_PARALYZE", DamageType.Paralyze),
            ("DMG_NERVEGAS", DamageType.NerveGas),
            ("DMG_POISON", DamageType.Poison),
            ("DMG_RADIATION", DamageType.Radiation),
            ("DMG_DROWNRECOVER", DamageType.DrownRecover),
            ("DMG_ACID", DamageType.Acid),
            ("DMG_SLOWBURN", DamageType.SlowBurn),
            ("DMG_FALL", DamageType.Fall),
            ("DMG_BURN", DamageType.Burn),
            ("DMG_PLASMA", DamageType.Plasma),
            ("DMG_DROWN", DamageType.Drown),
            ("DMG_CRUSH", DamageType.Crush),
            ("DMG_PHYSGUN", DamageType.PhysGun),
            ("DMG_PREVENT_PHYSICS_FORCE", DamageType.PreventPhysicsForce),
            ("DMG_BLAST", DamageType.Blast),
            ("DMG_SONIC", DamageType.Sonic),
            ("DMG_CLUB", DamageType.Club),
            ("DMG_SHOCK", DamageType.Shock),
        };

        foreach (var (name, value) in used)
        {
            Assert.True(flags.ContainsKey(name),
                name + " is not defined as (1 << n) in shareddefs.h any more");
            Assert.True(flags[name] == (int)value,
                name + " is 0x" + flags[name].ToString("x") + " in shareddefs.h and 0x"
                + ((int)value).ToString("x") + " in DamageType");
        }
    }
}
