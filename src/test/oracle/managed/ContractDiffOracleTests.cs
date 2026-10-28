using System.Text.RegularExpressions;

using SourceSharp.Abi;

using SourceSharp.Tests;

using static SourceSharp.Tests.TestServices;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// LAYER 0 OF <c>plan_test.md</c> — THE CONTRACT DIFF.
///
/// <para>
/// What a map may say to an entity is declared as data on both sides, so the
/// two are comparable with no game running: <c>DEFINE_INPUTFUNC</c> against
/// <c>[ScriptedEvent]</c>, <c>DEFINE_OUTPUT</c> against an <c>Output</c> field,
/// <c>DEFINE_KEYFIELD</c> against a <c>[ScriptVars]</c> key — and
/// <c>DEFINE_INPUT</c>, which is BOTH, against both.
/// </para>
///
/// <para>
/// The stock half is <see cref="StockContract"/>, scraped out of
/// <c>src/game</c> of the PRISTINE upstream clone (<see cref="ValveSdk"/>). The
/// managed half is the registry the game already publishes.
/// </para>
///
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests</b> with its scraper. The stock
/// datadescs are the files the port stopped compiling, so the farm no longer
/// links them. Reading upstream rather than the fork also compares the
/// classnames whose stock source the fork DELETED
/// (<c>ContractBaseline.StockSourceDeleted</c>): what the baseline's comment
/// called "reading a stock revision out of git history, layer 0's natural next
/// step". The ratchets that need no stock source (universal-gap arguments,
/// open defects, refusals) stay in ContractDiffTests.
/// </para>
///
/// <para>
/// WHAT THIS IS, AND IS NOT. It is a ratchet, not a to-do list. The gaps it
/// finds are real — 1,900-odd of them — and closing them is item after item of
/// other people's work. What this stops is a gap being ADDED without anybody
/// seeing it: a managed class quietly losing an input in a refactor, or an
/// entity ported without one of stock's outputs. Every gap that exists is
/// written down in <see cref="ContractBaseline"/>, and the comparison is an
/// EQUALITY in both directions — a new gap fails by name, and a gap that has
/// been closed fails until it is deleted from the file. A baseline that may
/// only shrink is the difference between a record and an excuse.
/// </para>
///
/// <para>
/// TO REGENERATE, after deliberately closing gaps or adding an entity: run the
/// suite with <c>SOURCESHARP_DUMP_CONTRACT=1</c> and paste what it prints over
/// the corresponding list in <c>ContractBaseline.cs</c>. Read the diff.
/// Regenerating to make a failure go away is exactly how this becomes the check
/// that cannot fail.
/// </para>
///
/// <para>
/// THE UNIVERSAL LIST IS THE EXCEPTION, since step 8b: it is not strings any
/// more but <c>ContractBaseline.UniversalGap</c> records, each carrying the
/// group it belongs to, how many managed classes answer it anyway, and the
/// argument for it. The dump prints entries that already have an argument
/// verbatim and NEW ones as skeletons reading <c>"TODO: argue this entry"</c> —
/// which <c>ContractDiffTests.EveryUniversalGapCarriesAnArgument</c> fails on. So a paste
/// still works and still cannot quietly throw away 67 arguments.
/// </para>
/// </summary>
public class ContractDiffOracleTests
{
    //-------------------------------------------------------------------------
    // The two sides, computed once.
    //-------------------------------------------------------------------------

    private static readonly Lazy<Diff> Current = new(Compute);

    /// <summary>One difference: what kind of declaration, on which map classname, called what.</summary>
    public readonly record struct Gap(string Kind, string ClassName, string Name)
    {
        public override string ToString() => $"{Kind}|{ClassName}|{Name}";
    }

    /// <summary>Everything the comparison produced.</summary>
    public sealed record Diff(
        IReadOnlyList<string> Universal,
        IReadOnlyList<string> ClassGaps,
        IReadOnlyList<string> Unlinked,
        IReadOnlyList<string> Deliberate,
        int ComparedClasses);

    private static Diff Compute()
    {
        var stock = StockContract.Shared.Value;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ContractDiffOracleTests).Assembly);
        registry.Services = Services();

        var managed = registry.DescribeClasses();

        // WHAT THE MANAGED BASE ANSWERS, taken as the intersection over every
        // registered class rather than by reflecting the base type. The
        // intersection cannot over-report: a name in it is one that EVERY class
        // answers, which is the property being relied on when it is subtracted
        // below. Measured, it is exactly {Kill} for inputs and empty for the
        // other two, and TheManagedBaseAnswersOnlyKill pins that.
        var everyInput = Intersect(managed.Select(c => c.Inputs));
        var everyOutput = Intersect(managed.Select(c => c.Outputs));
        var everyVar = Intersect(managed.Select(c => c.Vars.Select(v => v.Name)));

        // CBaseEntity's own contract: what stock declares on the class every
        // entity in the game descends from.
        var root = stock.Accumulate(RootClass);

        // A UNIVERSAL gap is one CBaseEntity declares and NO managed class
        // answers. It is one fact, not 141 copies of one fact, so it is recorded
        // once and subtracted from the per-class diff below.
        var universal = new List<string>();

        foreach (string n in root.Inputs.Where(n => !everyInput.Contains(n)))
            universal.Add($"input|{n}");

        foreach (string n in root.Outputs.Where(n => !everyOutput.Contains(n)))
            universal.Add($"output|{n}");

        foreach (string n in root.Keys.Where(n => !everyVar.Contains(n)))
            universal.Add($"key|{n}");

        universal.Sort(StringComparer.Ordinal);

        var universalNames = new HashSet<string>(universal, StringComparer.OrdinalIgnoreCase);

        var gaps = new List<string>();
        var unlinked = new List<string>();
        var deliberate = new List<string>();
        int compared = 0;

        foreach (var c in managed.OrderBy(c => c.ClassName, StringComparer.Ordinal))
        {
            var s = stock.Resolve(c.ClassName);

            if (s is null)
            {
                unlinked.Add(c.ClassName);
                continue;
            }

            compared++;

            void Take(string kind, IEnumerable<string> stockSide, IEnumerable<string> managedSide)
            {
                var have = new HashSet<string>(managedSide, StringComparer.OrdinalIgnoreCase);

                foreach (string n in stockSide)
                {
                    if (have.Contains(n) || universalNames.Contains($"{kind}|{n}"))
                        continue;

                    string line = $"{kind}|{c.ClassName}|{n}";

                    if (ContractBaseline.IsDeliberate(kind, n))
                        deliberate.Add(line);
                    else
                        gaps.Add(line);
                }
            }

            Take("input", s.Inputs, c.Inputs);
            Take("output", s.Outputs, c.Outputs);
            Take("key", s.Keys, c.Vars.Select(v => v.Name));
        }

        gaps.Sort(StringComparer.Ordinal);
        deliberate.Sort(StringComparer.Ordinal);
        unlinked.Sort(StringComparer.Ordinal);

        return new Diff(universal, gaps, unlinked, deliberate, compared);
    }

    /// <summary>The C++ class every stock entity descends from.</summary>
    private const string RootClass = "CBaseEntity";

    /// <summary>
    /// What a CHAIN terminates at now, which is no longer the same name.
    ///
    /// The close retired `public/ss_cbaseentity_alias.h` and every compiled file
    /// spells the entity type <c>IGameEntity</c>. The four helper bases the port
    /// lifted into the shim -- CPointEntity, CServerOnlyEntity,
    /// CServerOnlyPointEntity, CLogicalEntity -- therefore read
    /// <c>: public IGameEntity</c>, so a stock class under one of them has a
    /// chain that ends at IGameEntity and never mentions CBaseEntity.
    ///
    /// <see cref="RootClass"/> is NOT changed with it, and that is the point:
    /// the ROOT CONTRACT is still scraped from CBaseEntity's own DATADESC in
    /// game/server/baseentity.cpp, a file the build does not compile and whose text is the
    /// stock reference this whole comparison exists to hold managed code
    /// against. The two names answer two different questions -- what the root
    /// DECLARES, and what a chain REACHES -- and conflating them is what made
    /// these two facts red.
    /// </summary>
    private const string RootBase = "IGameEntity";

    /// <summary>A chain that reaches the entity root, under either spelling.</summary>
    private static bool ReachesRoot(IEnumerable<string> chain)
        => chain.Contains(RootClass) || chain.Contains(RootBase);

    private static HashSet<string> Intersect(IEnumerable<IEnumerable<string>> sets)
    {
        HashSet<string>? acc = null;

        foreach (var s in sets)
        {
            var here = new HashSet<string>(s, StringComparer.OrdinalIgnoreCase);

            if (acc is null)
                acc = here;
            else
                acc.IntersectWith(here);
        }

        return acc ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    //-------------------------------------------------------------------------
    // THE SCRAPER REACHES ITS SUBJECT. Four facts, because a diff that read
    // nothing would report no differences and pass — which is the shape every
    // defective harness in this tree has had.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheScrapeReadsStocksDatadescs()
    {
        var stock = StockContract.Shared.Value;

        // Counted from the tree at the time of writing: 829 BEGIN_DATADESC and
        // 1,092 LINK_ENTITY_TO_CLASS across src/game. Floors rather than pins,
        // for the reason tools/leg-floor gives: a pinned count rots and the fix
        // for a stale pin teaches the wrong reflex. A scan that read nothing, or
        // a fifth of the tree, is nowhere near these. The classes are the HL2MP
        // DLL's since 2026-10-26 (SourceFiles): 1,179 classes and 609 map
        // names at valve-sdk.lock 22288b91, where the whole tree's, TF and
        // single-player with it, were over 2,000 and 900.
        Assert.True(stock.Classes.Count > 1000, $"the scrape found stock's C++ entity classes ({stock.Classes.Count})");
        Assert.True(stock.Linked.Count > 500, $"the scrape found stock's map classnames ({stock.Linked.Count})");
    }

    [Fact]
    public void InheritanceResolvesThroughAClassWithNoDatadescOfItsOwn()
    {
        // THE PROTOTYPE'S BLIND SPOT, named in plan_test.md: it followed bases
        // only through DEFINE_BASECLASS, which lives INSIDE a datadesc, so 55
        // map classnames could not be compared at all. CRotDoor is one of them —
        // it has no datadesc, and its base is known only from
        // DECLARE_CLASS( CRotDoor, CBaseDoor ) in doors.cpp.
        var d = StockContract.Shared.Value.Resolve("func_door_rotating");

        Assert.True(d is not null, "func_door_rotating resolves to a C++ class");
        Assert.True(d!.Chain.Contains("CBaseDoor"), $"and up through CBaseDoor, which holds the datadesc ({string.Join(" <- ", d.Chain)})");
        Assert.True(d.Inputs.Contains("Open") && d.Inputs.Contains("Close") && d.Inputs.Contains("Lock"),
                    "so it inherits CBaseDoor's Open, Close and Lock rather than reading as an entity with no inputs");
    }

    [Fact]
    public void SomeBasesAreKnownOnlyFromDeclareClass()
    {
        // WHY THIS EXISTS, and it came out of trying to redden the fact above.
        // The three routes are largely redundant: CRotDoor has both a
        // DECLARE_CLASS and a `class : public`, so dropping either one on its
        // own left func_door_rotating resolving perfectly and the inheritance
        // fact green. Each route is still load-bearing — for OTHER classes — and
        // the only way to say so is to count the classes exactly one route
        // reaches. A route that reaches nothing on its own is a route that can
        // be deleted, and nothing else here would notice.
        int only = OnlyRoute(StockContract.RouteDeclareClass);

        Assert.True(only > 0, $"some C++ classes' base is known only from DECLARE_CLASS ({only})");
    }

    [Fact]
    public void SomeBasesAreKnownOnlyFromTheClassHeader()
    {
        int only = OnlyRoute(StockContract.RouteClassHeader);

        Assert.True(only > 0, $"some C++ classes' base is known only from `class X : public Y` ({only})");
    }

    private static int OnlyRoute(string route)
        => StockContract.Shared.Value.Classes.Values
               .Count(c => c.BaseRoutes.Count == 1 && c.BaseRoutes.Contains(route));

    [Fact]
    public void DefineInputIsBothAMapKeyAndAnInput()
    {
        // The distinction the prototype's first real finding turned on:
        // logic_timer declares its three random bounds with DEFINE_INPUT, which
        // makes each one a key AND an input. A scraper that read DEFINE_INPUT as
        // a keyfield would report no gap here at all.
        var t = StockContract.Shared.Value.Resolve("logic_timer");

        Assert.True(t is not null, "logic_timer resolves");
        Assert.True(t!.Inputs.Contains("LowerRandomBound"), "LowerRandomBound is an INPUT, because DEFINE_INPUT declares one");
        Assert.True(t.Keys.Contains("LowerRandomBound"), "and a map KEY, because DEFINE_INPUT declares that too");
    }

    [Fact]
    public void ACommentedOutDeclarationIsNotScraped()
    {
        // A diff that invents gaps gets switched off, and a commented-out
        // DEFINE_INPUTFUNC is the cheapest way to invent one. The strip is
        // StockContract.CodeOnly, which this leans on rather than repeating.
        string code = StockContract.CodeOnly(
            "BEGIN_DATADESC( CFoo )\n" +
            "  DEFINE_INPUTFUNC( FIELD_VOID, \"Live\", InputLive ),\n" +
            "//DEFINE_INPUTFUNC( FIELD_VOID, \"Dead\", InputDead ),\n" +
            "END_DATADESC()\n");

        Assert.True(code.Contains("\"Live\""), "the live declaration survives the strip");
        Assert.True(!code.Contains("\"Dead\""), "and the commented-out one does not");
    }

    //-------------------------------------------------------------------------
    // THE DIFF ITSELF.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryManagedEntityStockAlsoDeclaresIsCompared()
    {
        var d = Current.Value;

        // A comparison that quietly compared six classes would find six classes
        // worth of gaps and pass everything else.
        Assert.True(d.ComparedClasses > 200, $"the diff compared the build's entity classes ({d.ComparedClasses})");
    }

    [Fact]
    public void NoManagedEntityHasAContractGapThatIsNotWrittenDown()
    {
        var d = Current.Value;

        // The regeneration path lives inside the fact that reads the baseline,
        // rather than in a [Fact] of its own whose only assertion would be
        // Assert.True(true) — a check that cannot fail is not made acceptable by
        // being a convenience.
        Regenerate(d);

        var added = d.ClassGaps.Except(ContractBaseline.ClassGaps, StringComparer.Ordinal).ToArray();

        // NAMED, not counted. "1,957 gaps, expected 1,956" sends a reader to a
        // diff tool; "func_button lost PressIn" sends them to the class.
        foreach (string line in added.Take(20))
            Assert.Fail($"contract gap not in the baseline: {line}");

        Assert.True(added.Length == 0, $"every difference between the two contracts is written down ({d.ClassGaps.Count})");
    }

    [Fact]
    public void TheBaselineCarriesNoGapThatHasBeenClosed()
    {
        var d = Current.Value;

        var stale = ContractBaseline.ClassGaps.Except(d.ClassGaps, StringComparer.Ordinal).ToArray();

        // The direction that makes it a ratchet. Without it the file is a record
        // of what was once true, and the count only ever goes up.
        foreach (string line in stale.Take(20))
            Assert.Fail($"baseline names a gap that is closed; delete the line: {line}");

        Assert.True(stale.Length == 0, "the baseline lists nothing that is no longer a gap");
    }

    [Fact]
    public void TheUniversalGapsAreExactlyTheOnesWrittenDown()
    {
        var d = Current.Value;

        // The CBaseEntity tier: what stock puts on the root class and no managed
        // entity answers. Pinned by EQUALITY in both directions, because it is
        // subtracted from every per-class comparison — a name that crept into
        // this list would silence 254 classes at once.
        var added = d.Universal.Except(ContractBaseline.Universal, StringComparer.Ordinal).ToArray();
        var stale = ContractBaseline.Universal.Except(d.Universal, StringComparer.Ordinal).ToArray();

        foreach (string line in added.Take(20))
            Assert.Fail($"new universal gap on {RootClass}: {line}");

        foreach (string line in stale.Take(20))
            Assert.Fail($"universal gap is answered now; delete the line: {line}");

        Assert.True(added.Length == 0 && stale.Length == 0,
                    $"the {RootClass} tier is exactly what is written down ({d.Universal.Count})");
    }

    //-------------------------------------------------------------------------
    // THE CLASSNAMES STOCK DOES NOT DECLARE, which is three different answers
    // wearing one shape.
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryManagedClassnameStockDoesNotDeclareIsAccountedFor()
    {
        var d = Current.Value;

        var unexplained = d.Unlinked
            .Where(n => !ContractBaseline.Invented.Contains(n, StringComparer.OrdinalIgnoreCase)
                     && !ContractBaseline.StockSourceDeleted.Contains(n, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        foreach (string n in unexplained.Take(20))
            Assert.Fail($"managed classname with no stock declaration and no reason: {n}");

        Assert.True(unexplained.Length == 0,
                    $"every classname stock does not declare is either invented here or had its C++ deleted ({d.Unlinked.Count})");
    }

    [Fact]
    public void NoInventedClassnameIsOneStockActuallyDeclares()
    {
        var stock = StockContract.Shared.Value;

        // Staleness in the other direction. A name listed as "ours" that stock
        // turns out to declare is a class that stopped being compared and said
        // nothing about it.
        var wrong = ContractBaseline.Invented.Where(stock.Linked.ContainsKey).ToArray();

        foreach (string n in wrong.Take(20))
            Assert.Fail($"listed as invented here, but stock declares it: {n}");

        Assert.True(wrong.Length == 0, $"nothing on the invented list is stock's ({ContractBaseline.Invented.Count})");
    }

    [Fact]
    public void EveryDeliberateRuleStillMatchesSomething()
    {
        var d = Current.Value;

        // A rule matching nothing is a rule whose subject moved — a renamed
        // output, an entity dropped — and it would go on excusing nothing
        // forever. Each rule has to be earning its place.
        foreach (var rule in ContractBaseline.Deliberate)
        {
            int hits = d.Deliberate.Count(line => rule.Matches(Kind(line), Leaf(line)));

            Assert.True(hits > 0, $"the deliberate rule for {rule.Description} still excuses something");
        }
    }

    //-------------------------------------------------------------------------
    // STEP 8b: THE SCRAPER'S TWO HOLES, AND THE COMMENT TRAP BESIDE THEM.
    //-------------------------------------------------------------------------

    [Fact]
    public void TheScrapeReadsGlobalKeyfields()
    {
        // DEFINE_GLOBAL_KEYFIELD declares a map key exactly as DEFINE_KEYFIELD
        // does, and KeyFieldRe did not match it — so `model`, the most-used
        // keyvalue in any map, was on NEITHER side of this comparison. Three of
        // the seven sites are on CBaseEntity itself.
        var root = StockContract.Shared.Value.Accumulate(RootClass);

        Assert.True(root.Keys.Contains("model"), "CBaseEntity's `model` keyfield is scraped");
        Assert.True(root.Keys.Contains("modelindex"), "and `modelindex`");
        Assert.True(root.Keys.Contains("globalname"), "and `globalname`");
    }

    [Fact]
    public void TheScrapeReadsAutoArrayKeyfields()
    {
        // The other unmatched spelling, three sites, none on the root — so it
        // needs a class of its own to be visible at all.
        var c = StockContract.Shared.Value.Resolve("color_correction_volume");

        Assert.True(c is not null, "color_correction_volume resolves");
        Assert.True(c!.Keys.Contains("filename"),
                    "and its DEFINE_AUTO_ARRAY_KEYFIELD `filename` is scraped");
    }

    // THERE IS NO FACT HERE ASSERTING THAT THE WIDENED KEYFIELD REGEX INVENTS
    // NOTHING, AND THAT IS DELIBERATE. Step 8b wrote two and mutation killed
    // both, which is worth more than either would have been:
    //
    //   * "cbase.cpp's macro list in a COMMENT does not become a map key called
    //     `mapname`" PASSED with CodeOnly removed from the scrape entirely —
    //     the comment's third argument is unquoted, so KeyFieldRe could never
    //     have matched it. The needle had no subject.
    //   * "the scrape still requires a QUOTED map name" PASSED with the quotes
    //     made optional in the regex — after CodeOnly, every DEFINE_*KEYFIELD
    //     left in the tree has a quoted name, so the requirement guards nothing
    //     observable.
    //
    // The over-match direction is already policed, and by something that CAN
    // fail: Universal is pinned by EQUALITY in both directions, and ClassGaps by
    // NoManagedEntityHasAContractGapThatIsNotWrittenDown. A regex that invented
    // a key on CBaseEntity adds a Universal line; one that invented a key
    // anywhere else adds a ClassGaps line. Verified by mutation — teaching
    // KeyFieldRe to also match DEFINE_INPUTFUNC reddens both.

    [Fact]
    public void TheScrapeFollowsABaseThisPortMovedOutOfSrcGame()
    {
        // `ab0335abd` moved CPointEntity, CServerOnlyEntity,
        // CServerOnlyPointEntity and CLogicalEntity out of
        // src/game/server/baseentity.h and into the shim, and the scrape reads
        // src/game. Every stock entity under one of those four then had a chain
        // that stopped before CBaseEntity — 111 of 252 compared classnames —
        // and nothing failed, because a truncated stock side reports FEWER
        // differences.
        var stock = StockContract.Shared.Value;
        var spark = stock.Resolve("env_spark");

        Assert.True(spark is not null, "env_spark resolves");
        Assert.True(spark!.Chain.Contains("CPointEntity"), "through CPointEntity");
        Assert.True(ReachesRoot(spark.Chain),
                    $"and on up to {RootClass} or {RootBase} " +
                    $"({string.Join(" <- ", spark.Chain)})");
    }

    [Fact]
    public void AlmostEveryComparedClassnameReachesTheRootClass()
    {
        // The count behind the fact above, as a floor rather than a pin. It read
        // 141 of 252 before step 8b and 243 after; the nine that still do not
        // reach are a separate, smaller hole and are not claimed to be fixed.
        var stock = StockContract.Shared.Value;

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ContractDiffOracleTests).Assembly);
        registry.Services = Services();

        int reaches = registry.DescribeClasses()
            .Select(c => stock.Resolve(c.ClassName))
            .Count(r => r is not null && ReachesRoot(r.Chain));

        Assert.True(reaches > 200,
                    $"the compared classnames reach {RootClass} or {RootBase} ({reaches}); " +
                    "it was 141 before step 8b and 243 after");

        // THE `OR` WAS LOAD-BEARING WHILE THIS READ THE FARM: the classes under
        // one of the four helper bases the port lifted into the shim reached
        // IGameEntity and never mentioned CBaseEntity. Upstream has neither the
        // lift nor IGameEntity, so from the pristine clone EVERY chain reaches
        // CBaseEntity and a chain that reaches IGameEntity without it means the
        // scrape read the port's tree after all (ValveSdk pointed at the farm or
        // the fork, or BaseOnlyFiles found the shim's entity_bases.h). The
        // assertion is kept and turned round to say so.
        int viaOldSpelling = registry.DescribeClasses()
            .Select(c => stock.Resolve(c.ClassName))
            .Count(r => r is not null && r.Chain.Contains(RootClass));
        int viaNewSpelling = registry.DescribeClasses()
            .Select(c => stock.Resolve(c.ClassName))
            .Count(r => r is not null && !r.Chain.Contains(RootClass) && r.Chain.Contains(RootBase));

        Assert.True(viaOldSpelling > 0,
                    $"no chain reaches {RootClass} at all ({viaOldSpelling}); the stock " +
                    "reference in src/game has stopped being readable, and this fact would " +
                    "then be satisfied by the new spelling alone");
        Assert.True(viaNewSpelling == 0,
                    $"{viaNewSpelling} chain(s) reach {RootBase} without {RootClass}; upstream has " +
                    "no IGameEntity and no lifted helper bases, so the scrape read the port's " +
                    "tree rather than the pristine Valve SDK");
        Assert.Equal(reaches, viaOldSpelling + viaNewSpelling);
    }

    [Fact]
    public void ThePortsOwnMapClassnamesNeverLandOnStocksSideOfTheDiff()
    {
        // The base-only files are read for INHERITANCE ONLY, and this is what
        // enforces it. Widen BaseOnlyFiles or drop the guard in Scrape and the
        // shim's own LINK_ENTITY_TO_CLASS registrations join stock's table — at
        // which point the diff compares this build against itself, reports no
        // differences for those classes, and passes.
        //
        // The two names below are registered ONLY by src/sourcesharp: measured
        // with `grep -rhoP 'LINK_ENTITY_TO_CLASS\s*\(\s*\K[A-Za-z_]\w*'` over
        // src/sourcesharp/game/server/*.cpp, which finds three, of which env_fire
        // is also stock's and these two are not.
        var stock = StockContract.Shared.Value;

        foreach (string n in new[] { "sourcesharp_areaportal", "sourcesharp_abi_sdk_fixture" })
            Assert.True(!stock.Linked.ContainsKey(n),
                        $"'{n}' is the port's own map classname and it is on stock's side of the diff");
    }

    private static string Kind(string line) => line.Split('|')[0];

    private static string Leaf(string line) => line.Split('|')[2];

    //-------------------------------------------------------------------------
    // Regeneration.
    //-------------------------------------------------------------------------

    private static void Regenerate(Diff d)
    {
        if (Environment.GetEnvironmentVariable("SOURCESHARP_DUMP_CONTRACT") != "1")
            return;

        // THE UNIVERSAL LIST IS PRINTED AS SKELETONS, NOT AS STRINGS. Step 8b
        // made every entry carry its own argument, and a dump that printed a
        // bare list would let the next lane paste 67 unargued lines over the
        // top and lose the whole of it in one commit. What comes out is
        // ContractBaseline.UniversalGap constructors with the group and the
        // reason left as TODO, and EveryUniversalGapCarriesAnArgument fails on
        // a TODO — so the paste compiles, runs, and tells you what is missing.
        // An entry that already has an argument is reprinted with it.
        var argued = ContractBaseline.UniversalArgued.ToDictionary(g => g.Line, StringComparer.Ordinal);

        Console.WriteLine("--- BEGIN CONTRACT UNIVERSAL ---");
        foreach (string l in d.Universal)
        {
            if (argued.TryGetValue(l, out var have))
                Console.WriteLine($"        new(\"{l}\", \"{have.Group}\", {have.AnsweredBy}, \"{have.Reason}\"),");
            else
                Console.WriteLine($"        new(\"{l}\", \"TODO: which group\", 0, \"TODO: argue this entry\"),");
        }
        Console.WriteLine("--- END CONTRACT UNIVERSAL ---");

        Console.WriteLine("--- BEGIN CONTRACT GAPS ---");
        foreach (string l in d.ClassGaps)
            Console.WriteLine($"        \"{l}\",");
        Console.WriteLine("--- END CONTRACT GAPS ---");

        Console.WriteLine("--- BEGIN CONTRACT UNLINKED ---");
        foreach (string l in d.Unlinked)
            Console.WriteLine($"        \"{l}\",");
        Console.WriteLine("--- END CONTRACT UNLINKED ---");

        Console.WriteLine($"--- deliberate {d.Deliberate.Count} compared {d.ComparedClasses}");
    }
}
