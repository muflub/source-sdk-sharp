using System.Text;
using System.Text.RegularExpressions;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The armed sites a RELEASE build cannot see, held to a ledger so they cannot
/// come back silently.
///
/// <para>
/// Phase 13 makes <c>CBaseEntity</c> a typedef for <c>IGameEntity</c>, and the
/// whole port measures its distance from that by arming
/// <c>SS_CBASEENTITY_ALIAS</c> and counting compile diagnostics. Every one of
/// those measurements is taken through <c>survey_patch_ninja.py</c>, which
/// patches the <b>release</b> ninja graph — so every armed count this port has
/// ever recorded carries <c>-DNDEBUG</c>, and two whole classes of site are
/// invisible to all of them:
/// </para>
/// <list type="bullet">
///   <item>the body of an <c>Assert()</c>. <c>public/tier0/dbgflag.h</c> sets
///     <c>DBGFLAG_ASSERT</c> only under <c>_DEBUG</c> (or
///     <c>RELEASEASSERTS</c>), and <c>dbg.h</c> then defines
///     <c>Assert(_exp)</c> as <c>((void)0)</c> — the expression is never
///     parsed, so no diagnostic about it can exist;</item>
///   <item>anything inside <c>#ifdef _DEBUG</c> or <c>#ifdef DEBUG</c>.</item>
/// </list>
///
/// <para>
/// Step 6 arms the alias for real, and <c>make debug</c> is what meets it
/// first. So a tree that measures clean in release can still fail to compile
/// armed in debug — which is not a hypothetical: two lanes found it
/// independently on the same day. Shard T6 found six sites in two files its
/// release survey had already reported clean, and shard T5 found
/// <c>soundenvelope.cpp</c>'s <c>#ifdef _DEBUG</c> classname block, which was
/// also a <em>link-time consumer</em> of <c>c_baseentity.o</c> that appeared in
/// none of its 194 sites.
/// </para>
///
/// <para>
/// The compiler is the authority on this population and the tree-wide debug
/// survey (<c>src/survey_debug_graph.py</c> + <c>src/survey_run_debug</c>)
/// measures it. But a survey is something a person has to remember to run, and
/// the gap existed for the whole port precisely because nobody did. This test
/// is the part that runs on every build. It cannot see everything the compiler
/// sees — a member ask through an entity pointer needs types — but the
/// <b>qualified static</b> shape, <c>CBaseEntity::Foo</c> in a debug-only
/// region, is decidable from the text, and it is the shape five of T6's six
/// sites had.
/// </para>
///
/// <para>
/// It is held to a <b>ledger</b> rather than to zero, because fixing these
/// sites belongs to the lanes that own the files. The ledger names every known
/// occurrence with its owner and verdict, and the test fails if the tree and
/// the ledger disagree <em>in either direction</em> — a new debug-only site
/// appears, or a ledgered one is fixed and the ledger not updated. Silence is
/// not available.
/// </para>
/// </summary>
public class DebugOnlyAliasSitesTests
{
    // -----------------------------------------------------------------------
    // What a release build does not compile.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The assert macros whose ARGUMENT is compiled away under -DNDEBUG.
    ///
    /// <para>
    /// Read off <c>public/tier0/dbg.h</c>'s <c>#else // DBGFLAG_ASSERT</c> arm,
    /// which is the definitive statement of which ones vanish. Three families
    /// that look like they belong here do NOT, and getting that wrong in either
    /// direction is a wrong answer rather than a conservative one:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>AssertFatal*</c> — <c>DBGFLAG_ASSERTFATAL</c> is defined in
    ///     the release arm of dbgflag.h too ("note: fatal asserts are enabled
    ///     in release builds"), so a fatal assert's body IS in the release
    ///     survey and is not a debug-only site;</item>
    ///   <item><c>Verify</c>, <c>VerifyMsg1..3</c>, <c>VerifyEquals</c>,
    ///     <c>DbgVerify</c> — these become <c>(_exp)</c>, not
    ///     <c>((void)0)</c>. The expression survives, deliberately, because
    ///     callers depend on its side effect;</item>
    ///   <item><c>AssertAlways</c>, <c>AssertMsgAlways</c> — defined outside
    ///     the flag entirely.</item>
    /// </list>
    /// </summary>
    internal static readonly string[] CompiledOutAsserts =
    {
        "Assert", "AssertOnce", "AssertMsg", "AssertMsgOnce", "AssertFunc",
        "AssertEquals", "AssertFloatEquals",
        "AssertMsg1", "AssertMsg2", "AssertMsg3", "AssertMsg4", "AssertMsg5",
        "AssertMsg6", "AssertMsg7", "AssertMsg8", "AssertMsg9",
    };

    /// <summary>
    /// `Assert(` and friends. The alternation is ordered longest-first and
    /// closed with a word boundary, or `Assert` would match the front of
    /// `AssertMsg2` and the argument span would be measured from the wrong
    /// parenthesis.
    /// </summary>
    private static readonly Regex AssertCall = new(
        @"\b(?:" + string.Join("|", CompiledOutAsserts.OrderByDescending(s => s.Length))
        + @")\s*\(", RegexOptions.Compiled);

    /// <summary>Any preprocessor conditional line.</summary>
    private static readonly Regex Conditional = new(
        @"(?m)^[ \t]*#[ \t]*(ifdef|ifndef|if|elif|else|endif)\b([^\n]*)$",
        RegexOptions.Compiled);

    /// <summary>
    /// `CBaseEntity::Name` / `C_BaseEntity::Name`.
    ///
    /// <para>
    /// Both spellings explicitly: <c>CBaseEntity</c> is NOT a substring of
    /// <c>C_BaseEntity</c> — the underscore — and a rule written on one of them
    /// silently misses the other side. That mistake is recorded three times in
    /// this port's instrument-fault list.
    /// </para>
    /// </summary>
    internal static readonly Regex QualifiedEntityStatic = new(
        @"\b(CBaseEntity|C_BaseEntity)\s*::\s*([A-Za-z_]\w*)", RegexOptions.Compiled);

    // -----------------------------------------------------------------------
    // Span-finding.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Comments and string literals blanked to spaces, length-preserving so
    /// every offset still indexes the original text.
    ///
    /// <para>
    /// Not cosmetic. <c>c_baseentity.h:871</c> is
    /// <c>AssertMsg( m_pfnThink != &amp;C_BaseEntity::Think, "Infinite recursion
    /// is infinitely bad." )</c> — a parenthesis inside that string would
    /// unbalance the argument scan, and a `//` comment naming an entity static
    /// would be reported as code.
    /// </para>
    /// </summary>
    internal static string Scrub(string text)
    {
        var buf = new StringBuilder(text);
        int i = 0, n = text.Length;

        while (i < n)
        {
            char c = text[i];

            if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                int j = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                j = j < 0 ? n : j + 2;
                Blank(buf, i, j);
                i = j;
            }
            else if (c == '/' && i + 1 < n && text[i + 1] == '/')
            {
                int j = text.IndexOf('\n', i);
                j = j < 0 ? n : j;
                Blank(buf, i, j);
                i = j;
            }
            else if (c == '"' || c == '\'')
            {
                int j = i + 1;
                while (j < n && text[j] != c)
                {
                    if (text[j] == '\\')
                        j++;
                    j++;
                }
                j = Math.Min(j + 1, n);
                Blank(buf, i, j);
                i = j;
            }
            else
            {
                i++;
            }
        }

        return buf.ToString();
    }

    private static void Blank(StringBuilder buf, int from, int to)
    {
        for (int k = from; k < to; k++)
        {
            if (buf[k] != '\n')
                buf[k] = ' ';
        }
    }

    /// <summary>
    /// The character spans a release build does not compile: every compiled-out
    /// assert's argument list, and every <c>#ifdef _DEBUG</c> / <c>#ifdef
    /// DEBUG</c> arm.
    ///
    /// <para>
    /// The <c>#else</c> of a debug conditional is the RELEASE arm and is
    /// deliberately not included; the <c>#else</c> of an <c>#ifndef _DEBUG</c>
    /// is, which is why the arm's debug-ness is tracked as a flag that flips
    /// rather than as a property of the opening directive.
    /// </para>
    /// </summary>
    internal static List<(int Start, int End)> ReleaseInvisibleSpans(string scrubbed)
    {
        var spans = new List<(int, int)>();

        foreach (Match m in AssertCall.Matches(scrubbed))
        {
            int depth = 0, j = m.Index + m.Length - 1;
            for (; j < scrubbed.Length; j++)
            {
                if (scrubbed[j] == '(')
                    depth++;
                else if (scrubbed[j] == ')' && --depth == 0)
                    break;
            }

            spans.Add((m.Index, Math.Min(j + 1, scrubbed.Length)));
        }

        spans.AddRange(ConditionalArms(scrubbed, IsDebugArm));
        return spans;
    }

    /// <summary>
    /// The arms of <c>#ifndef SS_CBASEENTITY_ALIAS</c> — the UNARMED half of a
    /// guard.
    ///
    /// <para>
    /// A qualified entity static there is not an open site: it is the code that
    /// runs when the alias is off, and the armed arm next to it is the fix. 323
    /// such guards are intact in this tree. Counting them would make this test
    /// fail on the work that closes it.
    /// </para>
    /// </summary>
    internal static List<(int Start, int End)> UnarmedGuardSpans(string scrubbed) =>
        ConditionalArms(scrubbed, (kind, rest) =>
            (kind == "ifndef"
             && rest.Contains("SS_CBASEENTITY_ALIAS", StringComparison.Ordinal))
            // `#if defined( DBGFLAG_ASSERT ) && !defined( SS_CBASEENTITY_ALIAS )`
            // is the same guard combined with another condition, and it is the
            // shape a debug-only guard has to take. Matching only `#ifndef`
            // would report the guarded arm as an open site.
            || (kind == "if"
                && Regex.IsMatch(rest, @"!\s*defined\s*\(?\s*SS_CBASEENTITY_ALIAS\b")));

    /// <summary>
    /// The macros that are defined in a debug build of this mod and not in a
    /// release one, read off <c>public/tier0/dbgflag.h</c> rather than guessed.
    ///
    /// <para>
    /// dbgflag.h's debug arm is <c>#if (defined(_DEBUG) || defined(RELEASEASSERTS))</c>
    /// and sets <c>DBGFLAG_MEMORY</c>, <c>DBGFLAG_ASSERT</c>,
    /// <c>DBGFLAG_ASSERTFATAL</c>, <c>DBGFLAG_ASSERTDLG</c> and
    /// <c>DBGFLAG_STRINGS</c>; its release arm sets <c>DBGFLAG_ASSERTFATAL</c>
    /// and <c>DBGFLAG_ASSERTDLG</c> unconditionally and <c>DBGFLAG_ASSERT</c>
    /// only under <c>STEAM</c>. So <b>ASSERTFATAL and ASSERTDLG are not
    /// debug-only</b> and naming them here would file release-visible code as
    /// hidden.
    /// </para>
    /// <para>
    /// <c>DBGFLAG_ASSERT</c> is the one that matters and it is a FOURTH
    /// spelling of "debug only", distinct from an <c>Assert()</c> body and from
    /// <c>#ifdef _DEBUG</c>: <c>game/server/client.cpp</c> wraps a whole helper
    /// in <c>#ifdef DBGFLAG_ASSERT</c>, and that helper is the flag the other
    /// two are built out of.
    /// </para>
    /// </summary>
    internal static readonly string[] DebugOnlyMacros =
        { "_DEBUG", "DEBUG", "DBGFLAG_ASSERT", "DBGFLAG_MEMORY", "DBGFLAG_STRINGS" };

    private static readonly string MacroAlt =
        string.Join("|", DebugOnlyMacros.OrderByDescending(s => s.Length));

    /// <summary>
    /// Whether this conditional opens an arm only a debug build compiles.
    ///
    /// <para>
    /// THREE SPELLINGS OF THE CONDITION, and the third is the one a rule
    /// written from memory misses. <c>game/server/physics.cpp:1166</c> is
    /// <c>#if _DEBUG</c> — bare macro truthiness, no <c>defined</c> and no
    /// <c>ifdef</c> — and it holds five armed sites the release survey reports
    /// none of.
    /// </para>
    /// <para>
    /// <c>DEBUG</c> without the underscore is a separate macro and the Debug
    /// configuration defines both (it did under VPC, <c>source_posix_base.vpc</c>:
    /// <c>"$BASE;DEBUG;_DEBUG"</c>), so both are matched.
    /// </para>
    /// </summary>
    private static bool IsDebugArm(string kind, string rest)
    {
        if (kind == "ifdef")
            return Regex.IsMatch(rest, $@"^\s*(?:{MacroAlt})\s*(?://.*|/\*.*)?$");

        if (kind == "if")
        {
            // `#if !defined(_DEBUG)` is the RELEASE arm wearing the same words,
            // and so is `#if !_DEBUG`. Both are excluded first.
            if (Regex.IsMatch(rest, $@"!\s*(?:defined\s*\(?\s*)?(?:{MacroAlt})\b"))
                return false;

            // `#if defined(_DEBUG)`, `#if defined _DEBUG`, and the bare
            // `#if _DEBUG`.
            return Regex.IsMatch(rest, $@"\bdefined\s*\(?\s*(?:{MacroAlt})\b")
                   || Regex.IsMatch(rest, $@"^\s*\(?\s*(?:{MacroAlt})\s*\)?\s*(?://.*|/\*.*)?$");
        }

        return false;
    }

    /// <summary>
    /// Every span whose enclosing conditional arm satisfies <paramref name="opens"/>.
    ///
    /// <para>
    /// Nesting is tracked with a stack, and the arm's flag is cleared at
    /// <c>#else</c> / <c>#elif</c> so only the matching arm is collected. An
    /// unbalanced file (more <c>#endif</c> than openings) pops nothing rather
    /// than throwing: a scanner that dies on one malformed file reports zero
    /// for the whole tree, which is the vacuous pass this test exists to
    /// prevent.
    /// </para>
    /// </summary>
    private static List<(int Start, int End)> ConditionalArms(
        string scrubbed, Func<string, string, bool> opens)
    {
        var spans = new List<(int, int)>();
        var stack = new List<(bool Match, int From)>();

        foreach (Match m in Conditional.Matches(scrubbed))
        {
            string kind = m.Groups[1].Value, rest = m.Groups[2].Value;

            switch (kind)
            {
                case "ifdef":
                case "ifndef":
                case "if":
                    stack.Add((opens(kind, rest), m.Index + m.Length));
                    break;

                case "else":
                case "elif":
                    if (stack.Count > 0)
                    {
                        var top = stack[^1];
                        if (top.Match)
                            spans.Add((top.From, m.Index));
                        stack[^1] = (false, m.Index + m.Length);
                    }
                    break;

                case "endif":
                    if (stack.Count > 0)
                    {
                        var top = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        if (top.Match)
                            spans.Add((top.From, m.Index));
                    }
                    break;
            }
        }

        return spans;
    }

    private static bool Inside(int pos, List<(int Start, int End)> spans) =>
        spans.Any(s => pos >= s.Start && pos < s.End);

    // -----------------------------------------------------------------------
    // The scan.
    // -----------------------------------------------------------------------

    /// <summary>
    /// `file:line\tCBaseEntity::Name` for every qualified entity static that a
    /// release build does not compile and an armed debug build does.
    /// </summary>
    internal static List<string> Scan(string relPath, string text)
    {
        var found = new List<string>();
        string scrubbed = Scrub(text);
        var invisible = ReleaseInvisibleSpans(scrubbed);

        if (invisible.Count == 0)
            return found;

        var guarded = UnarmedGuardSpans(scrubbed);

        foreach (Match m in QualifiedEntityStatic.Matches(scrubbed))
        {
            if (!Inside(m.Index, invisible) || Inside(m.Index, guarded))
                continue;

            int line = 1;
            for (int i = 0; i < m.Index; i++)
            {
                if (text[i] == '\n')
                    line++;
            }

            found.Add($"{relPath}:{line}\t{m.Groups[1].Value}::{m.Groups[2].Value}");
        }

        return found;
    }

    // -----------------------------------------------------------------------
    // Which files. Only the ones the two DLLs actually compile.
    // -----------------------------------------------------------------------

    /// <summary>
    /// The directories the two DLLs draw source from, read off the vpc-generated
    /// ninja graph at bffdd479 and pinned here.
    ///
    /// <para>
    /// The SDK ships <c>game/*/hl2</c>, <c>tf</c>, <c>sdk</c>, <c>hl2mp</c>,
    /// <c>episodic</c>, <c>econ</c>, <c>NextBot</c>, <c>workshop</c> and more,
    /// and this mod compiles none of them — every one of those `$File` entries
    /// is behind a `[$TF]`-style condition that is false here. Scanning them
    /// would report sites in code that is in no binary and can never fail a
    /// build.
    /// </para>
    /// </summary>
    private static readonly string[] CompiledDirs =
    {
        "game/client", "game/client/game_controls", "game/client/replay",
        "game/client/sixense",
        "game/server",
        "game/shared", "game/shared/sixense",
    };

    /// <summary>
    /// The files the two DLLs actually compile: every `.cpp` the CMake build
    /// compiles into server.so or client.so (<see cref="CompiledCppFiles"/>),
    /// plus every `.h` in <see cref="CompiledDirs"/> and the C# shims.
    ///
    /// <para>
    /// THIS USED TO BE A DIRECTORY SWEEP MINUS THE `-$File` REMOVALS, and the
    /// subtraction was the point: a plain sweep reported four sites in
    /// <c>game/client/prediction.cpp</c> and one in
    /// <c>game/client/c_particle_system.cpp</c>, which this mod does not build.
    /// Five of fifteen rows were noise. Then it was the ADDITIONS of the two
    /// flattened vpcs (<c>plan_vpc.md</c>, <c>plan_vpc_client.md</c>); now it
    /// is the build's own compiled set, which is exact.
    /// </para>
    /// <para>
    /// HEADERS ARE SWEPT FROM THE DIRECTORIES rather than read from the build,
    /// and that is not an oversight: a CMake source list names only translation
    /// units, and a header is compiled through whatever translation unit
    /// includes it.
    /// </para>
    /// <para>
    /// HEADERS ARE NOT SUBTRACTED, and the asymmetry is deliberate. A header is
    /// compiled through whatever translation unit includes it, so removing its
    /// `.cpp` from the build does not remove the header from the build —
    /// <c>baseentity.h</c> carries five debug-only Asserts that every server TU
    /// sees. Over-reporting a header nothing includes costs one ledger row a
    /// reader can strike out; missing one costs a compile failure on flip day.
    /// </para>
    /// </summary>
    private static IEnumerable<string> CompiledSources(string src)
    {
        var compiled = CompiledCppFiles();
        var seen = new SortedSet<string>(StringComparer.Ordinal);

        void Take(string dir, SearchOption how)
        {
            if (!Directory.Exists(dir))
                return;

            foreach (string f in Directory.EnumerateFiles(dir, "*.*", how))
            {
                if (!f.EndsWith(".cpp", StringComparison.Ordinal)
                    && !f.EndsWith(".h", StringComparison.Ordinal))
                    continue;

                string rel = Rel(src, f);
                if (f.EndsWith(".cpp", StringComparison.Ordinal) && !compiled.Contains(rel))
                    continue;

                seen.Add(rel);
            }
        }

        foreach (string d in CompiledDirs)
            Take(Path.Combine(src, d.Replace('/', Path.DirectorySeparatorChar)),
                 SearchOption.TopDirectoryOnly);

        Take(Path.Combine(src, "sourcesharp", "game"), SearchOption.AllDirectories);

        return seen;
    }

    /// <summary>
    /// Every `.cpp` the CMake build compiles into server.so or client.so,
    /// farm-relative (<see cref="CMakeGraph.GameSources"/>: compile_commands.json,
    /// equal to each target's CMakeLists list; it throws on a missing, empty or
    /// stale build). It read the <c>$File</c> rows of the mod's two vpc files
    /// until VPC was replaced.
    /// </summary>
    internal static SortedSet<string> CompiledCppFiles() =>
        new(CMakeGraph.GameSources().Where(f => f.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)),
            StringComparer.Ordinal);

    private static string Rel(string src, string full) =>
        Path.GetRelativePath(src, full).Replace(Path.DirectorySeparatorChar, '/');

    private static string LedgerPath(string src) =>
        Path.Combine(src, "..", "docs", "typedef-dryrun", "debug_survey",
                     "debug_only_static_ledger.tsv");

    /// <summary>The ledgered sites, as `file:line\tCBaseEntity::Name`.</summary>
    internal static List<string> Ledger(string path)
    {
        var rows = new List<string>();

        foreach (string ln in File.ReadAllLines(path))
        {
            if (ln.Length == 0 || ln[0] == '#')
                continue;

            string[] cols = ln.Split('\t');
            if (cols.Length < 2)
                continue;

            rows.Add(cols[0] + "\t" + cols[1]);
        }

        return rows;
    }

    [Fact]
    public void TheScannerFindsTheShapeItIsFor()
    {
        // T6's own text, before it was fixed: four sites in cdll_client_int.cpp
        // that the release survey reported none of.
        var hits = Scan("cdll_client_int.cpp",
            "void CHLClient::ExtraMouseSample( float frametime, bool active )\n"
            + "{\n"
            + "\tAssert( C_BaseEntity::IsAbsRecomputationsEnabled() );\n"
            + "\tAssert( C_BaseEntity::IsAbsQueriesValid() );\n"
            + "}\n");

        Assert.Equal(
            new[]
            {
                "cdll_client_int.cpp:3\tC_BaseEntity::IsAbsRecomputationsEnabled",
                "cdll_client_int.cpp:4\tC_BaseEntity::IsAbsQueriesValid",
            },
            hits);
    }

    [Fact]
    public void TheScannerFindsAnIfdefDebugSite()
    {
        // T6's gameinterface.cpp:1433 shape, and T5's soundenvelope.cpp block
        // shape: not an Assert at all, a whole `#ifdef _DEBUG` region.
        var hits = Scan("gameinterface.cpp",
            "void CServerGameDLL::PreClientUpdate( bool simulating )\n"
            + "{\n"
            + "#ifdef _DEBUG\n"
            + "\tCBaseAnimating *anim = dynamic_cast< CBaseAnimating * >( "
            + "CBaseEntity::Instance( engine->PEntityOfEntIndex( 1 ) ) );\n"
            + "#endif\n"
            + "}\n");

        Assert.Equal(new[] { "gameinterface.cpp:4\tCBaseEntity::Instance" }, hits);
    }

    [Fact]
    public void TheScannerIgnoresACallAReleaseBuildCompiles()
    {
        // The same expression outside a debug-only region is an ordinary armed
        // site: the release survey reports it, a lane already owns it, and
        // counting it here would drown the ledger in work that is not this
        // test's subject. Three shapes at once, and all three are traps:
        // AssertFatal survives -DNDEBUG, Verify becomes (_exp) rather than
        // ((void)0), and the bare call was never conditional.
        var hits = Scan("x.cpp",
            "void f()\n"
            + "{\n"
            + "\tCBaseEntity::Instance( 1 );\n"
            + "\tAssertFatal( CBaseEntity::IsAbsQueriesValid() );\n"
            + "\tVerify( CBaseEntity::IsAbsQueriesValid() );\n"
            + "}\n");

        Assert.Empty(hits);
    }

    [Fact]
    public void TheScannerFindsTheBareIfDebugSpelling()
    {
        // game/server/physics.cpp:1166 is `#if _DEBUG` — no `ifdef`, no
        // `defined`. Five armed sites live under it and the release survey
        // reports none of them. A rule written from memory has two spellings in
        // it and misses this one.
        var hits = Scan("physics.cpp",
            "#if _DEBUG\n"
            + "\tCBaseEntity::Instance( 1 );\n"
            + "#endif\n");

        Assert.Equal(new[] { "physics.cpp:2\tCBaseEntity::Instance" }, hits);
    }

    [Fact]
    public void TheScannerFindsTheDbgflagAssertSpelling()
    {
        // game/server/client.cpp wraps IsInGroundList in `#ifdef DBGFLAG_ASSERT`
        // — the flag `Assert()` itself is built out of, set by dbgflag.h only
        // under _DEBUG. Not an Assert body, not `#ifdef _DEBUG`, and invisible
        // to both if the scanner knows only those two.
        var hits = Scan("client.cpp",
            "#ifdef DBGFLAG_ASSERT\n"
            + "static bool f() { return CBaseEntity::IsServer(); }\n"
            + "#endif\n");

        Assert.Equal(new[] { "client.cpp:2\tCBaseEntity::IsServer" }, hits);
    }

    [Fact]
    public void TheScannerDoesNotTreatAssertFatalFlagsAsDebugOnly()
    {
        // DBGFLAG_ASSERTFATAL and DBGFLAG_ASSERTDLG are set in dbgflag.h's
        // RELEASE arm as well. Filing them as debug-only would put
        // release-visible code — which every existing measurement already
        // reports — into a ledger that is supposed to hold what nothing else
        // reports.
        Assert.Empty(Scan("x.cpp",
            "#ifdef DBGFLAG_ASSERTFATAL\n\tCBaseEntity::Instance( 1 );\n#endif\n"));
        Assert.Empty(Scan("x.cpp",
            "#ifdef DBGFLAG_ASSERTDLG\n\tCBaseEntity::Instance( 1 );\n#endif\n"));
    }

    [Fact]
    public void TheScannerIgnoresACombinedAliasGuard()
    {
        // The shape a debug-only guard has to take: the debug flag AND the
        // alias guard in one condition. Matching only `#ifndef` would report
        // the guarded arm as an open site and the ledger would grow a row for
        // work that is already done.
        Assert.Empty(Scan("client.cpp",
            "#if defined( DBGFLAG_ASSERT ) && !defined( SS_CBASEENTITY_ALIAS )\n"
            + "static bool f() { return CBaseEntity::IsServer(); }\n"
            + "#endif\n"));
    }

    [Fact]
    public void TheScannerIgnoresTheNegatedDebugConditional()
    {
        // `#if !_DEBUG` and `#if !defined(_DEBUG)` are RELEASE arms. Treating
        // them as debug-only would file sites the release survey already
        // reports, and the ledger would fill with work that is not its subject.
        Assert.Empty(Scan("x.cpp", "#if !_DEBUG\n\tCBaseEntity::Instance( 1 );\n#endif\n"));
        Assert.Empty(Scan("x.cpp",
            "#if !defined(_DEBUG)\n\tCBaseEntity::Instance( 1 );\n#endif\n"));
    }

    [Fact]
    public void TheScannerIgnoresTheReleaseArmOfADebugConditional()
    {
        // The `#else` of `#ifdef _DEBUG` is compiled by the RELEASE build, so
        // anything there is already in every measurement this port has taken.
        var hits = Scan("x.cpp",
            "#ifdef _DEBUG\n"
            + "\tint a = 0;\n"
            + "#else\n"
            + "\tCBaseEntity::Instance( 1 );\n"
            + "#endif\n");

        Assert.Empty(hits);
    }

    [Fact]
    public void TheScannerIgnoresAnAlreadyGuardedSite()
    {
        // The unarmed arm of an SS_CBASEENTITY_ALIAS guard is the code that
        // runs when the alias is off. It is the FIX, not an open site.
        var hits = Scan("x.cpp",
            "#ifndef SS_CBASEENTITY_ALIAS\n"
            + "\tAssert( CBaseEntity::IsAbsQueriesValid() );\n"
            + "#else\n"
            + "\tAssert( EntityAbs_AreQueriesValid() );\n"
            + "#endif\n");

        Assert.Empty(hits);
    }

    [Fact]
    public void TheScannerDoesNotReadCommentsOrStringsAsCode()
    {
        var hits = Scan("x.cpp",
            "\t// Assert( CBaseEntity::Instance( 1 ) );\n"
            + "\tAssertMsg( x, \"CBaseEntity::Instance is not called here\" );\n");

        Assert.Empty(hits);
    }

    [Fact]
    public void TheScannerMatchesBothSpellingsOfTheDoomedClass()
    {
        // CBaseEntity is NOT a substring of C_BaseEntity. A rule written on one
        // spelling is silently server-only or client-only, which this port has
        // recorded three separate times.
        Assert.Single(Scan("s.cpp", "\tAssert( CBaseEntity::IsServer() );\n"));
        Assert.Single(Scan("c.cpp", "\tAssert( C_BaseEntity::IsServer() );\n"));
    }
}
