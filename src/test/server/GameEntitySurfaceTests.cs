using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// That <c>IGameEntity</c>'s surface is exactly the one on record — pinned,
/// because its member count is the plan's headline metric and it has had
/// three different answers with nothing testing any of them.
///
/// <para>
/// WHAT IS COUNTED, stated once so the next reader does not re-derive it a
/// fourth way: the <b>pure virtual member functions declared inside the class
/// body of <c>IGameEntity</c></b> in <c>src/public/igameentity.h</c>. The
/// protected destructor is virtual but not pure and is NOT counted — it is
/// asserted separately, because an interface whose destructor stopped being
/// virtual would delete through the wrong type. The file has no conditional
/// compilation inside the class body (also asserted), so the count cannot be
/// side-dependent: the number is the same on server and client alike.
/// </para>
///
/// <para>
/// Why the historical answers disagreed: 17 was the surface before the touch
/// edges and <c>GetSpawnFlags</c> arrived (the plan records 17 → 20); 20 was
/// true until <c>TakeDamage</c>, <c>IsPlayer</c> and <c>GetSpawnFlags</c>
/// left for <c>IEntityDamageTarget</c> and the <c>AsDamageTarget</c> accessor
/// arrived (20 − 3 + 1 = 18); 13 and "18" were re-derivations under counting
/// rules nobody wrote down. This test IS the counting rule now. A member
/// added or removed fails the set comparison by NAME, not just by number, so
/// the failure says what changed rather than that something did.
/// </para>
///
/// <para>
/// This list is expected to SHRINK as plan Phase 11 moves migration members
/// onto their named interfaces — each departure updates this expectation in
/// the same commit, which is the point: the metric moves only when someone
/// says so in a diff, never by drift.
/// </para>
/// </summary>
public class GameEntitySurfaceTests
{
    /// <summary>
    /// The pinned surface, in declaration order. Update this list in the same
    /// commit that changes the header, with the argument for the change.
    /// </summary>
    private static readonly string[] PinnedMembers =
    {
        // Engine-reached (plan Part 2's audit: these stay).
        "ShouldCollide",
        "GetCollisionGroup",
        // The migration accessors are GONE — AsDamageTarget, AsIo and
        // AsMapPlaceable each died with its interface when the last game-DLL
        // callers came down to the relays in standalone_relays.h. This is the
        // boundary at Part 2's target: engine-reached methods and nothing
        // else, which is Phase 13's precondition for the typedef.
        // Engine-reached: MarkEntitiesAsTouching and the VPhysics trigger
        // edges (plan Part 2's audit: these stay).
        "Touch",
        "StartTouch",
        "EndTouch",
        // Engine-reached: the solver's once-a-tick report.
        "VPhysicsUpdate",
    };

    [Fact]
    public void TheSurfaceIsThePinnedOne()
    {
        string? header = FindHeader();

        if (header is null)
        {
            // Skipped rather than failed, matching AbiParityTests: the tests
            // must still run from a directory with no source tree next to it.
            Assert.True(true, "igameentity.h was found (skipped, not found)");
            return;
        }

        string body = ClassBodyOf(File.ReadAllText(header));
        List<string> declared = PureVirtualNamesIn(body);

        Assert.True(declared.SequenceEqual(PinnedMembers),
            "IGameEntity's pure virtual members are the pinned ones, in order.\n" +
            $"  pinned   ({PinnedMembers.Length}): {string.Join(", ", PinnedMembers)}\n" +
            $"  declared ({declared.Count}): {string.Join(", ", declared)}\n" +
            $"  missing from header: {string.Join(", ", PinnedMembers.Except(declared).DefaultIfEmpty("(none)"))}\n" +
            $"  unpinned in header:  {string.Join(", ", declared.Except(PinnedMembers).DefaultIfEmpty("(none)"))}");
    }

    /// <summary>
    /// The number itself, asserted against the name list rather than beside
    /// it, so the two cannot disagree — anything citing "IGameEntity has N
    /// members" means THIS: pure virtuals in the class body, destructor
    /// excluded.
    /// </summary>
    [Fact]
    public void TheCountIsTheListsLength()
    {
        Assert.True(PinnedMembers.Length == 6,
            $"IGameEntity's pinned member count is 6 (pure virtuals, destructor excluded): exactly the six engine-reached methods the plan's Part 2 audit named — ShouldCollide, GetCollisionGroup, Touch, StartTouch, EndTouch, VPhysicsUpdate. The three migration accessors died with their interfaces, replaced by the relays in standalone_relays.h; this is Phase 13's precondition, met. The pin says {PinnedMembers.Length}. " +
            "If a member legitimately left or arrived, update BOTH the list and this number in the same commit, " +
            "and correct plan_baseentity.md's figures against it.");
    }

    /// <summary>
    /// The twelve members that plan_baseentity.md's Phase 13 audit left with no
    /// answer. Each is a member SDK code asks of a <c>CBaseEntity</c> that the
    /// typedef cannot compile, and each was, for a long time, dated to "the
    /// phase that removes its caller" — a date that kept not arriving, so the
    /// twelve kept being re-audited and re-dated instead of answered.
    /// </summary>
    private static readonly string[] TheTwelve =
    {
        "VPhysicsGetObject",
        "GetMoveType",
        "GetFlags",
        "EyePosition",
        "GetAbsVelocity",
        "IsEFlagSet",
        "GetTeamNumber",
        "m_takedamage",
        "m_debugOverlays",
        "SetTransmit",
        "GetMoveParent",
        "AddEFlags",
    };

    /// <summary>
    /// Two things at once, and the pair is the point.
    ///
    /// <para>
    /// FIRST, none of the twelve is a member. That already follows from the
    /// pinned-six list, but it follows in a way whose failure says "the list
    /// changed" rather than "somebody let SDK bookkeeping onto the boundary" —
    /// and those want different reactions from the reader.
    /// </para>
    ///
    /// <para>
    /// SECOND, and the reason this test exists at all: each of the twelve is
    /// NAMED IN THE HEADER, inside the class body, with its answer beside it.
    /// The failure mode being prevented is not a wrong answer, it is an ABSENT
    /// one. Twelve members went round the audit loop three times because the
    /// reasoning lived only in a plan document, so every reader who met one at a
    /// call site had to re-derive it and every re-derivation ended in a new
    /// date. A comment can drift and prose cannot be run — but a comment that
    /// must MENTION a specific name to keep a test green cannot silently
    /// disappear when the next person tidies the header, which is the only
    /// failure that would put the loop back.
    /// </para>
    ///
    /// <para>
    /// If one of the twelve genuinely joins the boundary, this test and
    /// <see cref="PinnedMembers"/> and <see cref="TheCountIsTheListsLength"/>
    /// all fail together, which is the correct amount of noise for adding a
    /// permanent cost to every implementor.
    /// </para>
    /// </summary>
    [Fact]
    public void TheTwelveHaveAnAnswerOnRecord()
    {
        string? header = FindHeader();
        if (header is null) { Assert.True(true, "skipped, header not found"); return; }

        string body = ClassBodyOf(File.ReadAllText(header));
        List<string> declared = PureVirtualNamesIn(body);

        foreach (string member in TheTwelve)
        {
            Assert.True(!declared.Contains(member),
                $"'{member}' is one of the twelve and is NOT a member of IGameEntity. " +
                "None of the twelve is engine-reached, so none of them passes the membership rule. " +
                "If this one genuinely has to join, say so loudly in the commit and update the pinned list, " +
                "the count and the answer block together.");

            Assert.True(body.Contains(member, StringComparison.Ordinal),
                $"igameentity.h's class body names '{member}' and says what answers it instead. " +
                "The whole reason the twelve stayed open for so long is that their reasoning lived in " +
                "plan_baseentity.md and nowhere a reader at a call site would find it. Do not delete the " +
                "answer block; correct it.");
        }
    }

    [Fact]
    public void TheDestructorIsVirtualAndNotPure()
    {
        string? header = FindHeader();
        if (header is null) { Assert.True(true, "skipped, header not found"); return; }

        string body = ClassBodyOf(File.ReadAllText(header));

        Assert.True(Regex.IsMatch(body, @"virtual\s+~IGameEntity\s*\(\s*\)\s*\{\s*\}"),
            "IGameEntity declares a virtual, non-pure destructor (deleting through the interface must reach the concrete type)");
        Assert.True(!Regex.IsMatch(body, @"~IGameEntity[^;{]*=\s*0"),
            "the destructor is not pure, so it is not a member of the pinned count");
    }

    /// <summary>
    /// No conditional compilation inside the class body, so the pinned count
    /// is the same number on both sides. The three-answers confusion this
    /// suite exists to end was first suspected to be side-ness; this is the
    /// assertion that it cannot be.
    /// </summary>
    [Fact]
    public void TheSurfaceIsNotSideDependent()
    {
        string? header = FindHeader();
        if (header is null) { Assert.True(true, "skipped, header not found"); return; }

        string body = ClassBodyOf(File.ReadAllText(header));

        Assert.True(!body.Contains("#if"),
            "IGameEntity's class body has no #if/#ifdef/#ifndef, so the member count cannot differ by side");
    }

    /// <summary>
    /// The text between the class introducer and its closing brace. Anchored
    /// on <c>abstract_class IGameEntity</c> and the first <c>};</c> at column
    /// zero after it, which is how the header is written; a second class in
    /// the file would not leak into the count.
    /// </summary>
    private static string ClassBodyOf(string text)
    {
        int start = text.IndexOf("abstract_class IGameEntity", StringComparison.Ordinal);
        Assert.True(start >= 0, "igameentity.h declares abstract_class IGameEntity");

        int end = text.IndexOf("\n};", start, StringComparison.Ordinal);
        Assert.True(end > start, "the class body closes with }; at column zero");

        return text.Substring(start, end - start);
    }

    /// <summary>
    /// Every <c>virtual … = 0;</c> declaration in the body, by method name —
    /// the identifier immediately before the parameter list's opening paren.
    /// <c>[^;{}]</c> lets a declaration span lines (AcceptInput does) while
    /// refusing to swallow a neighbour.
    /// </summary>
    private static List<string> PureVirtualNamesIn(string body)
    {
        // Comments go first, and not as tidiness: the header's own prose says
        // "hands each to a virtual" two lines above KeyValue, and a matcher
        // that reads comments would anchor there. Verified by breaking it.
        body = Regex.Replace(body, @"//[^\n]*", "");

        var names = new List<string>();

        foreach (Match m in Regex.Matches(body, @"virtual\s+(?<sig>[^;{}]*?)=\s*0\s*;", RegexOptions.Singleline))
        {
            Match name = Regex.Match(m.Groups["sig"].Value, @"([A-Za-z_]\w*)\s*\(");
            Assert.True(name.Success, $"a pure virtual declaration has a name before its paren: {m.Groups["sig"].Value.Trim()}");
            names.Add(name.Groups[1].Value);
        }

        return names;
    }

    /// <summary>The header in the compat farm, as TierParityTests finds its own.</summary>
    private static string? FindHeader()
    {
        string relative = Path.Combine("src", "public", "igameentity.h");

        return RepoLayout.FindSdkFile(relative);
    }
}
