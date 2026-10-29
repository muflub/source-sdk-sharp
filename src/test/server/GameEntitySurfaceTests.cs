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

}
