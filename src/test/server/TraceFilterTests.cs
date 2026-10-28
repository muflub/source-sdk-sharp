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

        Assert.True(native.Count == 6,
            $"the header declares six filter modes and this found {native.Count} — the pattern "
          + "stopped matching rather than found a disagreement");

        // HIT_ALL -> HitAll, WORLD_ONLY -> WorldOnly.
        (string Native, TraceFilter Managed)[] pairs =
        [
            ("HIT_ALL",    TraceFilter.HitAll),
            ("SIMPLE",     TraceFilter.Simple),
            ("WORLD_ONLY", TraceFilter.WorldOnly),
            ("NO_NPCS_OR_PLAYER", TraceFilter.NoNpcsOrPlayer),
            ("SKIP_PLAYER_AND_VIEW_MODEL", TraceFilter.SkipPlayerAndViewModel),
            ("GLOW", TraceFilter.Glow),
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
    /// <summary>
    /// The mask values cross the boundary as numbers too: each managed
    /// <see cref="TraceMask"/> is the header's SOURCESHARP_TRACE_ value of
    /// the same meaning, comments stripped first (the filter fact says why).
    /// </summary>
    [Fact]
    public void TheManagedMaskValuesAreTheOnesTheHeaderDeclares()
    {
        string? header = RepoLayout.FindSdkFile(Path.Combine("src", "sourcesharp", "host", "sourcesharp.h"));
        Assert.True(header is not null, "sourcesharp.h is not in the farm: the mask values cannot be checked");

        string text = Regex.Replace(File.ReadAllText(header!), @"/\*.*?\*/", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"//[^\n]*", " ");
        var native = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(text, @"\bSOURCESHARP_TRACE_(?!FILTER_)(\w+)\s*=\s*(\d+)"))
            native[m.Groups[1].Value] = int.Parse(m.Groups[2].Value);

        (string Native, TraceMask Managed)[] pairs =
        [
            ("BRUSHES", TraceMask.Brushes),
            ("SHOT", TraceMask.Shot),
            ("BRUSHES_AND_WATER", TraceMask.BrushesAndWater),
            ("ALL", TraceMask.All),
            ("SHOT_HULL", TraceMask.ShotHull),
            ("SOLID", TraceMask.Solid),
            ("BLOCKLOS", TraceMask.BlockLos),
            ("OPAQUE_AND_NPCS_WINDOW", TraceMask.OpaqueAndNpcsWindow),
            ("GLOW_SIGHT", TraceMask.GlowSight),
            ("CONTENTS_SOLID", TraceMask.ContentsSolid),
            ("SHOT_NO_WINDOWS", TraceMask.ShotNoWindows),
            ("OPAQUE", TraceMask.Opaque),
        ];
        Assert.Equal(pairs.Length, native.Count);
        Assert.Equal(pairs.Length, Enum.GetValues<TraceMask>().Length);
        foreach ((string name, TraceMask managed) in pairs)
        {
            Assert.True(native.TryGetValue(name, out int value), $"the header declares SOURCESHARP_TRACE_{name}");
            Assert.True(value == (int)managed, $"SOURCESHARP_TRACE_{name} is {value}, TraceMask.{managed} is {(int)managed}");
        }
    }

    /// <summary>
    /// The client maps a SOURCESHARP_TRACE_ value to an engine mask in one
    /// place: every trace slot's body takes its mask from ClientTraceMaskOf,
    /// and no comparison of a mask value is made outside it, so a value one
    /// slot knew could not be read by another as the brushes-only default.
    /// </summary>
    [Fact]
    public void EveryClientTraceSlotReadsTheOneMaskTable()
    {
        string code = AbiParityTests.CodeOnly(File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_worldquery_client.cpp")));
        string[] slots = ["ClientTraceLine", "ClientClipRayToEntities", "ClientTraceHull", "ClientTraceHullEntities"];
        foreach (string slot in slots)
        {
            int at = code.IndexOf($"int32_t SourceSharp_{slot}(", StringComparison.Ordinal);
            Assert.True(at >= 0, $"the client defines {slot}'s body");
            string body = code[at..code.IndexOf("\n}\n", at, StringComparison.Ordinal)];
            Assert.Matches(@"const\s+unsigned\s+int\s+nTraceMask\s*=\s*ClientTraceMaskOf\(\s*(?:req\.)?nMask\s*\)\s*;", body);
        }

        int table = code.IndexOf("static unsigned int ClientTraceMaskOf(", StringComparison.Ordinal);
        Assert.True(table >= 0, "the client defines the one mask table");
        int tableEnd = code.IndexOf("\n}\n", table, StringComparison.Ordinal);
        const string Compare = @"nMask\s*==\s*SOURCESHARP_TRACE_\w+";
        // Every value but BRUSHES, which is the table's default.
        Assert.Equal(Enum.GetValues<TraceMask>().Length - 1, Regex.Matches(code[table..tableEnd], Compare).Count);
        Assert.Empty(Regex.Matches(code[..table] + code[tableEnd..], Compare));
    }

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
