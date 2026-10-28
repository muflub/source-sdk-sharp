using System.Text.RegularExpressions;

namespace SourceSharp.OracleTests;

/// <summary>
/// WHAT A MAP MAY SAY TO A STOCK ENTITY, READ OUT OF <c>src/game</c>.
///
/// <para>
/// This is one half of `plan_test.md`'s layer 0 — the contract diff. The other
/// half is the managed registry, which the game already publishes. Both sides
/// declare the same three things as data, so they are comparable with no engine
/// running, no map loaded and no game built:
/// </para>
///
/// <list type="table">
///   <item><term><c>DEFINE_INPUTFUNC</c></term><description>an input</description></item>
///   <item><term><c>DEFINE_OUTPUT</c></term><description>an output</description></item>
///   <item><term><c>DEFINE_KEYFIELD</c></term><description>a map key</description></item>
///   <item><term><c>DEFINE_INPUT</c></term><description>a map key AND an input, which is the one that has bitten</description></item>
/// </list>
///
/// <para>
/// THE INHERITANCE IS THE WHOLE DIFFICULTY. A prototype of this walked bases
/// only through <c>DEFINE_BASECLASS</c>, which appears inside a datadesc block,
/// so any class whose datadesc it could not find was uncomparable — 55 map
/// classnames, including <c>func_door_rotating</c>, <c>trigger_once</c> and
/// every <c>item_ammo_*</c>. This resolves the base three ways instead, in
/// order of authority: <c>DEFINE_BASECLASS</c> in the datadesc,
/// <c>DECLARE_CLASS( CFoo, CBar )</c> in the class body, and finally
/// <c>class CFoo : public CBar</c>. A class with a base by ANY of those routes
/// is comparable, whether or not it has a datadesc of its own.
/// </para>
///
/// <para>
/// EVERYTHING IS READ COMMENT-STRIPPED, through <see cref="CodeOnly"/> (a copy of
/// SourceSharp.Tests' AbiParityTests.CodeOnly),
/// for the reason that scan gives: a commented-out <c>DEFINE_INPUTFUNC</c> read
/// as a live one invents a gap, and a diff that invents gaps gets switched off.
/// </para>
///
/// <para>
/// WHAT IT DOES NOT MODEL: the preprocessor. A datadesc inside
/// <c>#ifdef HL2_DLL</c> or <c>#ifndef SOURCESHARP</c> is read exactly like any
/// other, because the question this asks is "what does the SDK declare for this
/// map classname", not "what does today's server binary contain". The managed
/// side is what the eviction left behind, so the stock declaration is the thing
/// worth comparing against.
/// </para>
/// </summary>
public sealed class StockContract
{
    /// <summary>A C++ entity class: its base, and what its own datadesc declares.</summary>
    public sealed record CppClass(
        string Name,
        string? Base,
        string BaseFrom,
        IReadOnlyCollection<string> BaseRoutes,
        IReadOnlyCollection<string> Inputs,
        IReadOnlyCollection<string> Outputs,
        IReadOnlyCollection<string> Keys,
        string DeclaredIn);

    /// <summary>Everything a map classname may be sent, with the chain it came from.</summary>
    public sealed record MapContract(
        string ClassName,
        string CppClass,
        IReadOnlyList<string> Chain,
        IReadOnlyList<string> Inputs,
        IReadOnlyList<string> Outputs,
        IReadOnlyList<string> Keys);

    private readonly Dictionary<string, CppClass> _classes;
    private readonly Dictionary<string, string> _linked;

    private StockContract(Dictionary<string, CppClass> classes, Dictionary<string, string> linked)
    {
        _classes = classes;
        _linked = linked;
    }

    /// <summary>Every C++ class this scan saw, by name.</summary>
    public IReadOnlyDictionary<string, CppClass> Classes => _classes;

    /// <summary>Every map classname <c>LINK_ENTITY_TO_CLASS</c> registers, to its C++ class.</summary>
    public IReadOnlyDictionary<string, string> Linked => _linked;

    //-------------------------------------------------------------------------
    // The scan.
    //-------------------------------------------------------------------------

    // BEGIN_DATADESC( CFoo ) ... END_DATADESC(). NO_BASE is the same block with
    // the inherited half deliberately dropped, and is recorded as such: a class
    // that says NO_BASE inherits nothing, so walking its base would ADD
    // declarations stock does not have.
    private static readonly Regex DataDescRe = new(
        @"\bBEGIN_DATADESC(?<nobase>_NO_BASE)?\s*\(\s*(?<cls>[A-Za-z_]\w*)\s*\)(?<body>.*?)\bEND_DATADESC\s*\(\s*\)",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LinkRe = new(
        @"\bLINK_ENTITY_TO_CLASS\s*\(\s*(?<map>[A-Za-z_]\w*)\s*,\s*(?<cls>[A-Za-z_]\w*)\s*\)",
        RegexOptions.Compiled);

    // DECLARE_CLASS( CFoo, CBar ). The _GAMEROOT and _NOFRIEND spellings carry
    // the same two arguments; _SIMPLE and _NOBASE deliberately do not name a
    // base and are therefore not matched here.
    private static readonly Regex DeclareClassRe = new(
        @"\bDECLARE_CLASS(?:_GAMEROOT|_NOFRIEND)?\s*\(\s*(?<cls>[A-Za-z_]\w*)\s*,\s*(?<base>[A-Za-z_][\w:]*)\s*\)",
        RegexOptions.Compiled);

    // class CFoo : public CBar — the fallback, and the one the prototype lacked.
    // The first base only: Source's multiple inheritance is mixins
    // (CDefaultPlayerPickupVPhysics, IMotionEvent), never a second entity base.
    private static readonly Regex InheritRe = new(
        @"\bclass\s+(?:\w+_DLL_EXPORT\s+|DLL_\w+\s+)?(?<cls>[A-Za-z_]\w*)\s*:\s*(?:public|protected|private)\s+(?<base>[A-Za-z_][\w:]*)",
        RegexOptions.Compiled);

    private static readonly Regex BaseClassRe = new(
        @"\bDEFINE_BASECLASS\s*\(\s*(?<base>[A-Za-z_][\w:]*)\s*\)", RegexOptions.Compiled);

    private static readonly Regex InputFuncRe = new(
        @"\bDEFINE_INPUTFUNC\s*\(\s*\w+\s*,\s*""(?<name>[^""]*)""", RegexOptions.Compiled);

    private static readonly Regex OutputRe = new(
        @"\bDEFINE_OUTPUT\s*\(\s*[^,]+,\s*""(?<name>[^""]*)""", RegexOptions.Compiled);

    // EVERY SPELLING OF A KEYFIELD, and the two that were missing are the point.
    //
    // DEFINE_GLOBAL_KEYFIELD and DEFINE_AUTO_ARRAY_KEYFIELD declare a map key
    // exactly as DEFINE_KEYFIELD does — same three arguments, same meaning to a
    // mapper — and neither was matched here, so the names they declare were
    // invisible to BOTH halves of the diff. That is the worst shape a scraper
    // can have: a name stock declares that the comparison never asks about
    // reads as a name with no difference. Three of the ten sites are on
    // CBaseEntity itself (globalname, model, modelindex), so the hole was on
    // the root class every entity in the game inherits from.
    //
    // Counted from the tree with
    //   grep -rhoP '\bDEFINE_[A-Z_]*KEYFIELD[A-Z_]*\b' src/game --include=*.cpp --include=*.h
    // -> DEFINE_KEYFIELD 1896, DEFINE_GLOBAL_KEYFIELD 7, DEFINE_AUTO_ARRAY_KEYFIELD 3,
    //    DEFINE_KEYFIELD_NOT_SAVED 1, DEFINE_KEYFIELD_NOTSAVED 1.
    // One of the seven GLOBAL sites is cbase.cpp:55, which is the macro list in
    // a COMMENT — CodeOnly strips it, and StockContractTests pins that it does.
    private static readonly Regex KeyFieldRe = new(
        @"\bDEFINE_(?:GLOBAL_|AUTO_ARRAY_)?KEYFIELD(?:_NOT_SAVED|_NOTSAVED)?\s*\(\s*[^,]+,\s*\w+\s*,\s*""(?<name>[^""]*)""",
        RegexOptions.Compiled);

    // DEFINE_INPUT is a key AND an input. `DEFINE_INPUT\s*\(` cannot match
    // DEFINE_INPUTFUNC( — there is a letter, not a paren, after INPUT.
    private static readonly Regex InputRe = new(
        @"\bDEFINE_INPUT\s*\(\s*[^,]+,\s*\w+\s*,\s*""(?<name>[^""]*)""", RegexOptions.Compiled);

    /// <summary>The three ways a base can be known, named rather than numbered.</summary>
    public const string RouteDefineBaseclass = "DEFINE_BASECLASS";

    /// <summary>The macro in the class body: <c>DECLARE_CLASS( CFoo, CBar )</c>.</summary>
    public const string RouteDeclareClass = "DECLARE_CLASS";

    /// <summary>The C++ the class is written in: <c>class CFoo : public CBar</c>.</summary>
    public const string RouteClassHeader = "class : public";

    private static string RouteName(int rank) => rank switch
    {
        0 => RouteDefineBaseclass,
        1 => RouteDeclareClass,
        _ => RouteClassHeader,
    };

    /// <summary>
    /// Reads <c>src/game/server</c> and <c>src/game/shared</c> — the two source
    /// sets the game DLL compiles — and returns everything both declare.
    /// </summary>
    public static StockContract Scrape(string gameDir)
    {
        var classes = new Dictionary<string, CppClass>(StringComparer.Ordinal);
        var linked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var inputs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var outputs = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var keys = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var bases = new Dictionary<string, (string Base, int Rank, string File)>(StringComparer.Ordinal);
        var noBase = new HashSet<string>(StringComparer.Ordinal);
        var seenIn = new Dictionary<string, string>(StringComparer.Ordinal);

        // EVERY route a base was found by, not only the winning one. The three
        // are largely redundant — a class with a DECLARE_CLASS almost always has
        // a `class X : public Y` as well — and that redundancy is exactly what
        // makes "drop one route" an unfalsifiable change unless the classes
        // reachable by ONE route only are counted. See
        // ContractDiffTests.SomeBasesAreKnownOnlyFrom*.
        var routes = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        // Rank 0 beats rank 1 beats rank 2: DEFINE_BASECLASS, then
        // DECLARE_CLASS, then the `: public` in the class header.
        void Base(string cls, string b, int rank, string file)
        {
            b = b.Contains("::") ? b[(b.LastIndexOf("::", StringComparison.Ordinal) + 2)..] : b;

            if (b == cls)
                return;

            if (!routes.TryGetValue(cls, out var seen))
                routes[cls] = seen = new SortedSet<string>(StringComparer.Ordinal);

            seen.Add(RouteName(rank));

            if (!bases.TryGetValue(cls, out var have) || rank < have.Rank)
                bases[cls] = (b, rank, file);
        }

        SortedSet<string> Bag(Dictionary<string, SortedSet<string>> d, string cls)
        {
            if (!d.TryGetValue(cls, out var set))
                d[cls] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            return set;
        }

        // The port's own headers are read for INHERITANCE ONLY, after src/game,
        // so a base edge stock also declares always wins the tie. See
        // BaseOnlyFiles for why they are read at all.
        var baseOnly = new HashSet<string>(BaseOnlyFiles(gameDir), StringComparer.Ordinal);

        foreach (string file in SourceFiles(gameDir).Concat(baseOnly))
        {
            string text = CodeOnly(File.ReadAllText(file));
            string shortName = Path.GetFileName(file);

            if (!baseOnly.Contains(file))
            {
                foreach (Match m in LinkRe.Matches(text))
                {
                    // First registration wins, and a second one for the same map
                    // name is not an error: the SDK registers a few names twice
                    // under #ifdef. Recording the first keeps the answer stable.
                    linked.TryAdd(m.Groups["map"].Value, m.Groups["cls"].Value);
                }
            }

            foreach (Match m in DeclareClassRe.Matches(text))
                Base(m.Groups["cls"].Value, m.Groups["base"].Value, 1, shortName);

            foreach (Match m in InheritRe.Matches(text))
                Base(m.Groups["cls"].Value, m.Groups["base"].Value, 2, shortName);

            // A base-only file contributes NOTHING to the contract itself. It is
            // read so a stock class whose base moved into the shim still has a
            // chain; letting it declare inputs, outputs, keys or map classnames
            // would put the PORT's declarations on stock's side of the diff,
            // which is the one thing this comparison must never do.
            if (baseOnly.Contains(file))
                continue;

            foreach (Match m in DataDescRe.Matches(text))
            {
                string cls = m.Groups["cls"].Value;
                string body = m.Groups["body"].Value;

                seenIn.TryAdd(cls, shortName);

                if (m.Groups["nobase"].Success)
                    noBase.Add(cls);

                var declared = BaseClassRe.Match(body);
                if (declared.Success)
                    Base(cls, declared.Groups["base"].Value, 0, shortName);

                foreach (Match d in InputFuncRe.Matches(body))
                    Bag(inputs, cls).Add(d.Groups["name"].Value);

                foreach (Match d in OutputRe.Matches(body))
                    Bag(outputs, cls).Add(d.Groups["name"].Value);

                foreach (Match d in KeyFieldRe.Matches(body))
                    Bag(keys, cls).Add(d.Groups["name"].Value);

                // The one that is both, and the one logic_timer's three random
                // bounds are declared with.
                foreach (Match d in InputRe.Matches(body))
                {
                    Bag(keys, cls).Add(d.Groups["name"].Value);
                    Bag(inputs, cls).Add(d.Groups["name"].Value);
                }
            }
        }

        foreach (string cls in inputs.Keys.Concat(outputs.Keys).Concat(keys.Keys)
                                     .Concat(bases.Keys).Distinct())
        {
            classes[cls] = new CppClass(
                cls,
                noBase.Contains(cls) ? null : bases.TryGetValue(cls, out var b) ? b.Base : null,
                noBase.Contains(cls) ? "NO_BASE"
                    : bases.TryGetValue(cls, out var r) ? RouteName(r.Rank) : "none",
                routes.TryGetValue(cls, out var rt) ? rt : Array.Empty<string>(),
                inputs.TryGetValue(cls, out var i) ? i : Array.Empty<string>(),
                outputs.TryGetValue(cls, out var o) ? o : Array.Empty<string>(),
                keys.TryGetValue(cls, out var k) ? k : Array.Empty<string>(),
                seenIn.TryGetValue(cls, out var f) ? f : bases.TryGetValue(cls, out var bf) ? bf.File : "");
        }

        return new StockContract(classes, linked);
    }

    /// <summary>
    /// Everything a map may send an entity spawned as <paramref name="mapClass"/>,
    /// accumulated up the whole inheritance chain. Null when no
    /// <c>LINK_ENTITY_TO_CLASS</c> names it — which is a real answer, not a
    /// failure: the managed build invents classnames stock has never had.
    /// </summary>
    public MapContract? Resolve(string mapClass)
        => _linked.TryGetValue(mapClass, out string? cpp) ? Accumulate(cpp, mapClass) : null;

    /// <summary>
    /// The same accumulation from a C++ class name rather than a map classname.
    /// Used for <c>CBaseEntity</c>, whose contract every entity in the game
    /// carries and which the managed side answers on its own base rather than
    /// class by class.
    /// </summary>
    public MapContract Accumulate(string cppClass, string? asMapClass = null)
    {
        string cpp = cppClass;
        var chain = new List<string>();
        var inputs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // A cycle here would hang the suite rather than fail it, and a
        // typedef'd base can produce one. Bounded and seen-checked.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (string? cur = cpp; cur is not null && seen.Add(cur);)
        {
            chain.Add(cur);

            if (!_classes.TryGetValue(cur, out var c))
                break;

            inputs.UnionWith(c.Inputs);
            outputs.UnionWith(c.Outputs);
            keys.UnionWith(c.Keys);

            cur = c.Base;
        }

        return new MapContract(asMapClass ?? cppClass, cpp, chain,
                               inputs.ToArray(), outputs.ToArray(), keys.ToArray());
    }

    //-------------------------------------------------------------------------
    // Where the sources are.
    //-------------------------------------------------------------------------

    /// <summary>
    /// What the HL2MP game DLL compiles of the game's two server source sets:
    /// <c>server/server_hl2mp.vpc</c>'s files (<see cref="VpcProject"/>).
    /// <c>src/game/client</c> is deliberately out: a client-side
    /// <c>LINK_ENTITY_TO_CLASS</c> registers a different entity for the same
    /// map name, and mixing the two would compare a managed server entity
    /// against a client one.
    ///
    /// <para>
    /// NOT EVERY FILE UNDER <c>server</c> AND <c>shared</c>, which is what this
    /// read until 2026-10-26. The single-player and episodic sources declare
    /// the same C++ classes as HL2MP's (<c>CWeaponCrossbow</c> in
    /// <c>server/hl2</c> and <c>shared/hl2mp</c>), and a class's bags are
    /// keyed by its C++ name, so the two datadescs were read as one and the
    /// first file found linked the map name: HL2MP's weapons were held to
    /// single-player members the HL2MP DLL never compiled.
    /// </para>
    /// </summary>
    public static IEnumerable<string> SourceFiles(string gameDir)
    {
        string project = Path.Combine(gameDir, "server", "server_hl2mp.vpc");
        string server = Path.Combine(gameDir, "server") + Path.DirectorySeparatorChar;
        string shared = Path.Combine(gameDir, "shared") + Path.DirectorySeparatorChar;

        var files = new SortedSet<string>(
            VpcProject.Files(project, Path.GetDirectoryName(gameDir)!)
                .Where(f => (f.StartsWith(server, StringComparison.Ordinal) || f.StartsWith(shared, StringComparison.Ordinal))
                            && Path.GetExtension(f) is ".cpp" or ".h"
                            && File.Exists(f)),
            StringComparer.Ordinal);

        // THE HEADERS ON ITS INCLUDE PATH TOO. A project lists its .cpp files
        // and only some of its headers, and a class declared in an unlisted one
        // (CNewWallHealth : CBaseAnimating, in hl2/item_healthkit.h) lost its
        // base and so its whole inherited contract: a truncated chain reports
        // FEWER gaps, never more. The directories are server_hl2mp.vpc's and
        // server_base.vpc's AdditionalIncludeDirectories.
        foreach (string dir in new[] { "server", "shared", "shared/econ", "server/NextBot",
                                       "server/hl2", "shared/hl2", "server/hl2mp", "shared/hl2mp" })
        {
            string at = Path.Combine(gameDir, dir.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(at))
                files.UnionWith(Directory.EnumerateFiles(at, "*.h", SearchOption.TopDirectoryOnly));
        }

        return files;
    }

    /// <summary>
    /// THE PORT'S OWN HEADERS, READ FOR INHERITANCE AND NOTHING ELSE.
    ///
    /// <para>
    /// <c>ab0335abd</c>, "Four helper bases leave baseentity.h before it dies",
    /// moved <c>CPointEntity</c>, <c>CServerOnlyEntity</c>,
    /// <c>CServerOnlyPointEntity</c> and <c>CLogicalEntity</c> out of
    /// <c>src/game/server/baseentity.h</c> and into
    /// <c>src/sourcesharp/game/server/entity_bases.h</c>. Every stock entity
    /// that derives from one of those four then had a chain that stopped dead
    /// before <c>CBaseEntity</c> — measured, <b>111 of 252</b> compared map
    /// classnames — and the diff went on comparing them against a contract
    /// missing the whole root class. Nothing failed, because a truncated stock
    /// side reports FEWER differences, not more.
    /// </para>
    ///
    /// <para>
    /// These files are read after <see cref="SourceFiles"/> and only for the two
    /// inheritance routes. They may not declare a map classname, an input, an
    /// output or a key: the port's own declarations belong on the MANAGED side
    /// of the comparison, and a scraper that took them for stock's would be
    /// comparing this build against itself.
    /// </para>
    /// </summary>
    public static IEnumerable<string> BaseOnlyFiles(string gameDir)
    {
        string root = Path.GetDirectoryName(Path.GetDirectoryName(gameDir)!)!;
        string f = Path.Combine(root, "src", "sourcesharp", "game", "server", "entity_bases.h");

        if (File.Exists(f))
            yield return f;
    }

    /// <summary>
    /// The scan, done once for the whole suite. It reads about 2,200 files, and
    /// every fact in the diff wants the same answer.
    ///
    /// <para>
    /// <b>FROM THE PRISTINE VALVE SDK</b> (<see cref="ValveSdk"/>), not the farm.
    /// The farm links only what the game compiles, and the stock datadescs this
    /// diff holds the managed entities against are, by definition, the ones the
    /// port stopped compiling. <see cref="BaseOnlyFiles"/> finds nothing there:
    /// upstream still declares the four helper bases in baseentity.h.
    /// </para>
    /// </summary>
    public static readonly Lazy<StockContract> Shared = new(() =>
        Scrape(Path.Combine(ValveSdk.Root, "src", "game")));


    /// <summary>
    /// C/C++ source with its comments removed, so that a scan matches code and
    /// only code.
    ///
    /// This is not tidiness. Every regex in this file used to run over raw file
    /// text, and a commented-out line counted as a live one:
    ///
    ///     // engineApi.ExplosionSetMagnitude = ...
    ///
    /// satisfied "every declared slot is wired" while leaving that slot null.
    /// The check could not fail for the single input it was built to reject,
    /// and the ABI-bump decision leans on it. The version #define and the
    /// header's function-pointer scan had the same exposure, so the strip
    /// happens once at read time and every later match inherits it.
    ///
    /// Newlines inside a block comment are preserved, so any line number in a
    /// failure message still lines up with the file on disk. String and
    /// character literals are copied whole, so a "//" inside a literal is not
    /// mistaken for the start of a comment.
    ///
    /// Precedent in the native tests: SlurpCodeFromSourceRoot / CodeOnly in
    /// sdk_player_cast_test.cpp, and the StripComments helpers throughout
    /// src/sourcesharp/test.
    /// </summary>
    internal static string CodeOnly(string source)
    {
        var code = new System.Text.StringBuilder(source.Length);

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            char next = i + 1 < source.Length ? source[i + 1] : '\0';

            // Line comment: the rest of THIS line goes, the newline stays.
            if (c == '/' && next == '/')
            {
                while (i < source.Length && source[i] != '\n')
                    i++;

                if (i < source.Length)
                    code.Append('\n');

                continue;
            }

            // Block comment: everything but the newlines goes.
            if (c == '/' && next == '*')
            {
                i += 2;

                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    if (source[i] == '\n')
                        code.Append('\n');

                    i++;
                }

                // Land on the '/' of "*/"; the loop's own i++ steps past it. An
                // unterminated comment runs to the end, which is what a
                // compiler would see too.
                i = Math.Min(i + 1, source.Length - 1);
                continue;
            }

            // A literal is copied whole, escapes included, so neither a "//"
            // inside it nor an escaped quote can confuse the scan.
            if (c == '"' || c == '\'')
            {
                char quote = c;
                code.Append(c);
                i++;

                while (i < source.Length && source[i] != quote)
                {
                    if (source[i] == '\\' && i + 1 < source.Length)
                    {
                        code.Append(source[i]);
                        i++;
                    }

                    code.Append(source[i]);
                    i++;
                }

                if (i < source.Length)
                    code.Append(source[i]);

                continue;
            }

            code.Append(c);
        }

        return code.ToString();
    }
}

