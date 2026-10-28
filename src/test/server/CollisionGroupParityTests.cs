using System.Text.RegularExpressions;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The two collision vocabularies C# holds a copy of, checked against the
/// native files that own them.
///
/// <para>
/// Only NUMBERS cross the boundary. A collision group leaves C# as 2 and is
/// looked up in a table in shell_entityapi.cpp; a solid flag arrives as 0x0008
/// and is compared with an enum here. Either side renumbered alone is a silent
/// wrong answer — a projectile deciding a wall is a trigger, or a managed brush
/// landing in a group that means something else — and nothing in either build
/// would say so.
/// </para>
///
/// <para>
/// So this reads the native files rather than restating them, the way
/// AbiParityTests reads the ABI header and TierParityTests reads the tier list.
/// A fixture that stands in for something native owns must assert the
/// agreement, not maintain it by hand.
/// </para>
/// </summary>
public class CollisionGroupParityTests
{
    [RepoSourceFact("src/sourcesharp/game/server/collision_group_names.h")]
    public void TheCollisionGroupNumbersMatchTheNativeEnum()
    {
        string header = FindRelative(Path.Combine("src", "sourcesharp", "game", "server",
                                                   "collision_group_names.h"));


        var native = EnumMembers(File.ReadAllText(header), "SourceSharpCollisionGroup_t");

        // The rows the enum in ScriptedEntity.cs claims, under the native
        // spelling. A name added on one side and not the other fails the count.
        var expected = new Dictionary<string, int>
        {
            ["SS_COLLISION_GROUP_NONE"]         = (int)CollisionGroup.None,
            ["SS_COLLISION_GROUP_VEHICLE_CLIP"] = (int)CollisionGroup.VehicleClip,
            ["SS_COLLISION_GROUP_WEAPON"]       = (int)CollisionGroup.Weapon,
            ["SS_COLLISION_GROUP_DEBRIS"]       = (int)CollisionGroup.Debris,
            ["SS_COLLISION_GROUP_INTERACTIVE"]  = (int)CollisionGroup.Interactive,
            ["SS_COLLISION_GROUP_PROJECTILE"]   = (int)CollisionGroup.Projectile,
            ["SS_COLLISION_GROUP_BREAKABLE_GLASS"] = (int)CollisionGroup.BreakableGlass,
        };

        Assert.True(native.Count == expected.Count,
                    $"C# names every collision group the header does (header has {native.Count}, C# has {expected.Count})");

        foreach (var (name, value) in expected)
        {
            Assert.True(native.TryGetValue(name, out int fromHeader),
                        $"the header declares {name}");

            Assert.True(fromHeader == value,
                        $"{name} is {value} on both sides (the header says {fromHeader})");
        }
    }

    [RepoSourceFact("src/public/const.h")]
    public void TheSolidFlagBitsMatchTheEngines()
    {
        string header = FindRelative(Path.Combine("src", "public", "const.h"));


        string text = File.ReadAllText(header);

        // Unlike the groups these are NOT renamed — they are stable bit values
        // the whole SDK reads directly — so what is asserted is the bits.
        Assert.True(FsolidBit(text, "FSOLID_NOT_SOLID") == (int)SolidFlags.NotSolid,
                    "SolidFlags.NotSolid is FSOLID_NOT_SOLID");

        Assert.True(FsolidBit(text, "FSOLID_TRIGGER") == (int)SolidFlags.Trigger,
                    "SolidFlags.Trigger is FSOLID_TRIGGER");

        Assert.True(FsolidBit(text, "FSOLID_VOLUME_CONTENTS") == (int)SolidFlags.VolumeContents,
                    "SolidFlags.VolumeContents is FSOLID_VOLUME_CONTENTS");
    }

    /// <summary>
    /// The solid TYPE numbers are the engine's own, member for member.
    ///
    /// <para>
    /// <c>SolidType_t</c> is a POSITIONAL enumeration, which is the property
    /// that makes <see cref="CollisionGroup"/> next door dangerous — and it is
    /// safe for solid types only because nothing translates them: the number
    /// leaves C# and arrives at <c>ICollideable::GetSolid</c> unchanged, and
    /// comes back the same way. That safety is exactly what this asserts. A
    /// member added to the engine's list in the middle would renumber
    /// everything after it, and the whole native save section rests on a solid
    /// type meaning the same thing on both sides.
    /// </para>
    /// </summary>
    [RepoSourceFact("src/public/const.h")]
    public void TheSolidTypeNumbersAreTheEngines()
    {
        string header = FindRelative(Path.Combine("src", "public", "const.h"));


        var native = ValuedEnumMembers(File.ReadAllText(header), "SolidType_t");

        var expected = new Dictionary<string, int>
        {
            ["SOLID_NONE"]      = (int)SolidType.None,
            ["SOLID_BSP"]       = (int)SolidType.Bsp,
            ["SOLID_BBOX"]      = (int)SolidType.BoundingBox,
            ["SOLID_OBB"]       = (int)SolidType.OrientedBox,
            ["SOLID_OBB_YAW"]   = (int)SolidType.OrientedBoxYaw,
            ["SOLID_CUSTOM"]    = (int)SolidType.Custom,
            ["SOLID_VPHYSICS"]  = (int)SolidType.VPhysics,
        };

        // EVERY member, not a chosen few. SOLID_LAST is a sentinel with no
        // value of its own and is not captured, which is why the counts match.
        Assert.True(native.Count == expected.Count,
                    $"C# names every solid type the engine does (const.h has {native.Count}, C# has {expected.Count})");

        foreach (var (name, value) in expected)
        {
            Assert.True(native.TryGetValue(name, out int fromHeader),
                        $"const.h declares {name}");

            Assert.True(fromHeader == value,
                        $"{name} is {value} on both sides (const.h says {fromHeader})");
        }
    }

    /// <summary>
    /// <b>THE COLLISION GROUP PAIR IS LOSSY, AND THAT IS ASSERTED RATHER THAN
    /// ASSUMED</b> — because a reason recorded in prose is a reason nobody
    /// re-checks.
    ///
    /// <para>
    /// <c>NativeField</c> gives this as the reason the savegame's native store
    /// does NOT carry a collision group: the getter is many-to-one on purpose,
    /// so a group this mod cannot name reads back as <c>None</c> and the setter
    /// would then write <c>COLLISION_GROUP_NONE</c> over whatever the entity
    /// actually had. If somebody ever makes the table total, this fact fails
    /// and the paragraph that cites it has to be revisited — which is the point
    /// of writing it as a test.
    /// </para>
    /// </summary>
    [RepoSourceFact("src/public/const.h",
                    "src/sourcesharp/game/server/shell_entityapi.cpp")]
    public void TheCollisionGroupTableIsManyToOneAndCannotNameThePlayersGroup()
    {
        string engineHeader = FindRelative(Path.Combine("src", "public", "const.h"));
        string table = FindRelative(Path.Combine("src", "sourcesharp", "game", "server",
                                                  "shell_entityapi.cpp"));

        // The engine's own list, which is positional and mostly unvalued, so it
        // is counted by NAME rather than read as an enum with numbers.
        var body = Regex.Match(File.ReadAllText(engineHeader),
                               @"enum\s+Collision_Group_t\s*\{(.*?)\}",
                               RegexOptions.Singleline);

        Assert.True(body.Success, "const.h declares enum Collision_Group_t");

        var engineGroups = new HashSet<string>(
            Regex.Matches(body.Groups[1].Value, @"\bCOLLISION_GROUP_[A-Z0-9_]+\b")
                 .Select(m => m.Value));

        // The rows the native translation table actually carries.
        var rows = Regex.Match(File.ReadAllText(table),
                               @"s_CollisionGroupNames\[\]\s*=\s*\{(.*?)\};",
                               RegexOptions.Singleline);

        Assert.True(rows.Success, "shell_entityapi.cpp declares s_CollisionGroupNames");

        var named = new HashSet<string>(
            Regex.Matches(rows.Groups[1].Value, @"\bCOLLISION_GROUP_[A-Z0-9_]+\b")
                 .Select(m => m.Value));

        // THE POSITIVE HALF FIRST: the table really does name some of them, so
        // "it cannot name the player's" below is about a table that names
        // things rather than about an empty match.
        Assert.True(named.Contains("COLLISION_GROUP_WEAPON")
                 && named.Contains("COLLISION_GROUP_DEBRIS"),
                    $"the table names the groups this mod does have: [{string.Join(", ", named.OrderBy(n => n))}]");

        Assert.True(named.Count < engineGroups.Count,
                    $"the table is many-to-one: it names {named.Count} of the engine's {engineGroups.Count}");

        // And the one that matters to a savegame: the group a player is in.
        Assert.True(engineGroups.Contains("COLLISION_GROUP_PLAYER"),
                    $"const.h declares COLLISION_GROUP_PLAYER (found {engineGroups.Count} groups)");

        Assert.True(!named.Contains("COLLISION_GROUP_PLAYER"),
                    "the table cannot name the group a player is in, so a saved "
                    + "player's group would come back as COLLISION_GROUP_NONE");
    }

    /// <summary>
    /// The edict limit C# refuses to break a prop against.
    ///
    /// <para>
    /// A number rather than a name on the managed side, because the engine has
    /// no slot that reports it — so it is a copy, and a copy of a hard limit
    /// that nobody checks is worse than no limit at all. If the engine's ever
    /// grew, a stale 2048 here would refuse to break props in a world that had
    /// room for them; if it shrank, the guard would stop guarding.
    /// </para>
    /// </summary>
    [RepoSourceFact("src/public/const.h")]
    public void TheEdictLimitIsTheEngines()
    {
        string header = FindRelative(Path.Combine("src", "public", "const.h"));


        string text = File.ReadAllText(header);

        var bits = Regex.Match(text, @"#define\s+MAX_EDICT_BITS\s+(\d+)");

        Assert.True(bits.Success, "const.h declares MAX_EDICT_BITS");

        int max = 1 << int.Parse(bits.Groups[1].Value);

        Assert.True(EngineLimits.MaxEdicts == max,
                    $"EngineLimits.MaxEdicts is MAX_EDICTS (const.h says {max})");
    }

    /// <summary>
    /// The same, for an enum whose members carry a trailing <c>//</c> comment —
    /// which <see cref="EnumMembers"/> deliberately will not cross.
    ///
    /// <para>
    /// A second helper rather than a looser shared one. The fact above counts
    /// members and its count means what it means because that regex is anchored;
    /// widening it to serve <c>SolidType_t</c> would be one edit with two
    /// subjects, and the one that got quieter would be the one nobody was
    /// looking at.
    /// </para>
    /// </summary>
    private static Dictionary<string, int> ValuedEnumMembers(string text, string enumName)
    {
        var body = Regex.Match(text, @"enum\s+" + Regex.Escape(enumName) + @"\s*\{(.*?)\}",
                               RegexOptions.Singleline);

        Assert.True(body.Success, $"the header declares enum {enumName}");

        var members = new Dictionary<string, int>();

        foreach (Match m in Regex.Matches(body.Groups[1].Value,
                                          @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(-?\d+)\s*,",
                                          RegexOptions.Multiline))
        {
            members[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        return members;
    }

    /// <summary>Every <c>NAME = number,</c> inside a named C enum.</summary>
    private static Dictionary<string, int> EnumMembers(string text, string enumName)
    {
        var body = Regex.Match(text, @"enum\s+" + Regex.Escape(enumName) + @"\s*\{(.*?)\}",
                               RegexOptions.Singleline);

        Assert.True(body.Success, $"the header declares enum {enumName}");

        var members = new Dictionary<string, int>();

        foreach (Match m in Regex.Matches(body.Groups[1].Value,
                                          @"^\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(-?\d+)\s*,?\s*$",
                                          RegexOptions.Multiline))
        {
            members[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        return members;
    }

    /// <summary>One <c>FSOLID_X = 0xNNNN,</c> out of const.h.</summary>
    private static int FsolidBit(string text, string name)
    {
        var m = Regex.Match(text, @"^\s*" + Regex.Escape(name) + @"\s*=\s*0x([0-9A-Fa-f]+)",
                            RegexOptions.Multiline);

        Assert.True(m.Success, $"const.h declares {name}");

        return Convert.ToInt32(m.Groups[1].Value, 16);
    }

    /// <summary>
    /// The path, which <see cref="RepoSourceFactAttribute"/> has already
    /// guaranteed exists for every fact here — each declares the files it
    /// reads, and xUnit SKIPS the fact when one is missing rather than running
    /// it against nothing. That is why no caller below re-checks for null.
    /// </summary>
    private static string FindRelative(string relative)
        // NOT REACHABLE from a fact carrying [RepoSourceFact] for this file --
        // xUnit skips it before the body runs. It throws rather than returning
        // null so the callers need no null check, and it names the path so
        // that if the two ever disagree about where they look, the message
        // says which file.
        => RepoSourceFactAttribute.Find(relative)
           ?? throw new FileNotFoundException(
                  $"{relative} is not in {RepoLayout.SdkTree}; the fact should "
                  + "have been skipped by [RepoSourceFact]", relative);
}
