using System.Text.RegularExpressions;
using SourceSharp.Abi;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="TraceFilter"/> — which entities a client trace may hit, and the
/// reason it is an enumeration rather than an ignore-entity parameter.
///
/// <para>
/// AN IGNORE PARAMETER WOULD NOT HAVE BEEN A SUPERSET. It has to be implemented
/// with <c>CTraceFilterSimple</c>, which differs from <c>CTraceFilterHitAll</c>
/// in more than the pass entity — it also applies collision groups and
/// <c>g_pGameRules-&gt;ShouldCollide</c>. So "ignore nothing" would answer a
/// filtered trace where the caller asked for an unfiltered one: agreeing on most
/// maps, disagreeing wherever a rule says so, and showing up as one gib falling
/// through one door.
/// </para>
///
/// <para>
/// The values are array indices across a language boundary, so they get the same
/// disk-parity check the declarations and the forward ids get: read the C header
/// and compare, rather than trust two lists to stay in step.
/// </para>
/// </summary>
public class TraceFilterTests
{
    [Fact]
    public void TheManagedFilterValuesAreTheOnesTheHeaderDeclares()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine(
            "src", "sourcesharp", "host", "sourcesharp.h"));

        if (header is null)
        {
            Assert.True(true, "sourcesharp.h read for the filter check (SKIPPED)");
            return;
        }

        var native = new Dictionary<string, int>(StringComparer.Ordinal);

        // COMMENTS STRIPPED FIRST, and it is not tidiness. Measured: comment
        // out `SOURCESHARP_TRACE_FILTER_WORLD_ONLY = 2,` in sourcesharp.h — the
        // enumerator is GONE from the build, the managed value now names
        // nothing, and this fact passed. A needle that matches the text of a
        // header matches its prose too, so the one thing a parity check must
        // notice, a declaration being deleted, is the thing it cannot see when
        // the deletion leaves the words behind.
        string text = Regex.Replace(File.ReadAllText(header), @"/\*.*?\*/", " ",
                                    RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", " ");

        foreach (Match m in Regex.Matches(text, @"SOURCESHARP_TRACE_FILTER_(\w+)\s*=\s*(\d+)"))
        {
            native[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);
        }

        Assert.True(native.Count == 4,
            $"the header declares four filter modes and this found {native.Count} — the pattern "
          + "stopped matching rather than found a disagreement");

        // HIT_ALL -> HitAll, WORLD_ONLY -> WorldOnly.
        (string Native, TraceFilter Managed)[] pairs =
        [
            ("HIT_ALL",    TraceFilter.HitAll),
            ("SIMPLE",     TraceFilter.Simple),
            ("WORLD_ONLY", TraceFilter.WorldOnly),
            ("NO_NPCS_OR_PLAYER", TraceFilter.NoNpcsOrPlayer),
        ];

        foreach ((string nativeName, TraceFilter managed) in pairs)
        {
            Assert.True(native.TryGetValue(nativeName, out int value),
                $"the header declares SOURCESHARP_TRACE_FILTER_{nativeName}");

            Assert.True(value == (int)managed,
                $"SOURCESHARP_TRACE_FILTER_{nativeName} is {value} and TraceFilter.{managed} is "
              + $"{(int)managed}. These cross a language boundary as a number, so a disagreement "
              + "is a world-only trace running as hit-all with nothing to say so");
        }

        // HIT-ALL IS ZERO ON PURPOSE, so a caller that does not choose gets what
        // the trace did before there were modes.
        Assert.True((int)TraceFilter.HitAll == 0,
            "hit-all is the zero value, so the unfiltered trace stays what a caller gets by not "
          + "choosing — the overload without a filter is not a behaviour change");
    }

    /// <summary>
    /// The filter and the pass entity REACH the engine. Recording them was a
    /// deliberate choice: a test asserting a trace was world-only is asserting
    /// the difference between a gib that bounces off a door and one that falls
    /// through it, and a recording that dropped the filter could not tell them
    /// apart.
    /// </summary>
    [Fact]
    public void TheFilterAndThePassEntityReachTheEngine()
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("WorldQuery.ClientTraceLine",
                       new ClientTraceResult(true, 0.5f, Vector.Zero, Vector.Zero, 0, 7, 0, 0));

        IEngineWorldQuery world = engine;

        world.ClientTraceLine(Vector.Zero, new Vector(64f, 0f, 0f), TraceMask.All,
                              TraceFilter.WorldOnly);

        var call = engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(call.ArgAs<TraceFilter>(3) == TraceFilter.WorldOnly,
            $"the filter is argument three and arrived as {call.ArgAs<TraceFilter>(3)}");

        Assert.True(call.ArgAs<int>(4) == -1,
            "and the pass entity defaults to -1, which passes nothing through — NOT the same as "
          + "hit-all, because a simple filter with no pass entity still applies the collision "
          + "group and the game rules");

        world.ClientTraceLine(Vector.Zero, new Vector(64f, 0f, 0f), TraceMask.All,
                              TraceFilter.Simple, passEntityIndex: 12);

        var second = engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(second.ArgAs<TraceFilter>(3) == TraceFilter.Simple
                 && second.ArgAs<int>(4) == 12,
            $"and a simple trace carries the entity it passes through: "
          + $"[{second.ArgAs<TraceFilter>(3)}, {second.ArgAs<int>(4)}]");
    }

    /// <summary>
    /// THE OVERLOAD WITHOUT A FILTER IS THE OLD BEHAVIOUR, unchanged. Both
    /// existing callers — the trace probe and the projected decal — trace
    /// hit-all, and adding modes must not have moved them.
    /// </summary>
    [Fact]
    public void TheUnfilteredOverloadStillTracesHitAll()
    {
        var engine = Fake();
        engine.Strict = false;
        engine.Returns("WorldQuery.ClientTraceLine",
                       new ClientTraceResult(false, 1f, Vector.Zero, Vector.Zero, 0, -1, 0, 0));

        IEngineWorldQuery world = engine;

        world.ClientTraceLine(Vector.Zero, new Vector(1f, 0f, 0f), TraceMask.Brushes);

        var call = engine.Last("WorldQuery.ClientTraceLine")!.Value;

        Assert.True(call.ArgAs<TraceFilter>(3) == TraceFilter.HitAll,
            $"the three-argument overload traces hit-all, not {call.ArgAs<TraceFilter>(3)} — "
          + "c_te_projecteddecal.cpp's trace hits everything and moving it would land a decal on "
          + "a different surface");
    }
}
