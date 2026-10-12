using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.OracleTests;

/// <summary>
/// Lane D — the eight gore and fluid tables — against plan_tmp.md Part 6, and
/// against the stock tables they were transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> writes out at length and lane A proved:
/// a fixture typed beside the declaration is one person's reading of one table
/// twice, and <c>cs_sv_te_dump</c> cannot catch a wrong-but-consistent width
/// because both its columns come from the same C# declaration. So every one of
/// these reads its <c>game/server/te_*.cpp</c> off disk, parses the
/// <c>IMPLEMENT_SERVERCLASS_ST</c> block, and asserts the agreement prop for
/// prop — name, kind, width, flags and range.
/// </para>
///
/// <para>
/// THE PARSER HERE IS A SUPERSET OF LANE A's AND LANE C's, and deliberately a
/// separate copy — six lanes editing one parser is the merge conflict this tree
/// has already paid for. Three things are new and each is one of this lane's
/// hazards:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>A SYMBOLIC RANGE.</b> <c>DT_TEBubbles</c> and <c>DT_TEBubbleTrail</c>
/// declare <c>[MIN_COORD_INTEGER, MAX_COORD_INTEGER]</c>, not numbers. The
/// literals in the declarations are checked against
/// <c>public/worldsize.h</c> read off disk, so the -16384/16384 typed there is
/// checked against its definition rather than against a memory of it.
/// </description></item>
/// <item><description>
/// <b>A FILE-LOCAL SYMBOLIC WIDTH.</b> <c>DT_TEBubbleTrail</c>'s
/// <c>m_nCount</c> is <c>BUBBLE_TRAIL_COUNT_BITS</c>, an enum in the same .cpp.
/// It is resolved by reading that enum, not by knowing it is 8.
/// </description></item>
/// <item><description>
/// <b>NOBASE VERSUS BASED.</b> This lane has both, and the macro spelling is
/// the only place the difference is written down. The parser reports which
/// macro it found and every parity check asserts it against
/// <c>TeClass.BaseTable</c>.
/// </description></item>
/// </list>
///
/// <para>
/// Three facts about this lane are load-bearing and each has an assertion of its
/// own below. <c>DT_TEMetalSparks</c> is the inventory's ONLY table that is both
/// a concrete effect and somebody else's base. <c>DT_TEBloodStream</c>'s
/// direction is eleven bits over an explicit [-10, 10] with NO encoding flag,
/// where its lane-mate <c>DT_TEBloodSprite</c> sends the same-named prop as a
/// plain COORD. And <c>DT_TEFizz</c> is BEHAVIOUR LEDGER ROW S1: the one place
/// in this port where the managed API is deliberately not shaped like stock's
/// <c>ITempEntsSystem</c>.
/// </para>
/// <para>
/// <b>AN ORACLE, MOVED OUT OF SourceSharp.Tests.</b> The stock files these facts
/// read are no longer compiled by either DLL, so the farm no longer links them;
/// the facts that compare the port against them live here and read the pristine
/// upstream clone through <see cref="ValveSdk"/>, where a missing file is a
/// failure rather than a skip. The rest of TempEntGoreTests stays where it was.
/// </para>
/// </summary>
public class TempEntGoreOracleTests
{

    /// <summary>
    /// Declaration order is wire order, and this table is where that stops being
    /// a slogan for this lane: <c>te_bubbles.cpp:38-43</c> declares
    /// <c>m_fHeight</c> BEFORE <c>m_nModelIndex</c> and the send table sends the
    /// model index first.
    /// </summary>
    [Fact]
    public void BubblesSendsTheModelIndexBeforeTheHeightAndTheHeaderDoesNot()
    {
        TeClass te = Build<TeBubbles>();

        Assert.True(te.ClassName == "CTEBubbles" && te.TableName == "DT_TEBubbles",
            "the two sides pair on CTEBubbles over DT_TEBubbles");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "nesting the empty DT_BaseTempEntity");
        Assert.True(te.RegistersClass, "te_bubbles.cpp is evicted, so this claims the ServerClass");

        string[] wire = te.Fields.Select(f => f.Name).ToArray();

        Assert.True(wire.Length == 6, "six props");
        Assert.True(wire[2] == "ModelIndex", "the third prop on the wire is m_nModelIndex");
        Assert.True(wire[3] == "Height", "and the fourth is m_fHeight");

        // The other half of the claim, read out of the .cpp rather than
        // asserted from memory: the class really does have them the other way
        // round, so this is a genuine divergence and not a misremembered one.
        string text = ValveSdk.Read("src/game/server/te_bubbles.cpp");

        int atHeight = text.IndexOf("CNetworkVar( float, m_fHeight )", StringComparison.Ordinal);
        int atModel  = text.IndexOf("CNetworkVar( int, m_nModelIndex )", StringComparison.Ordinal);

        Assert.True(atHeight > 0 && atModel > 0, "te_bubbles.cpp declares both members");
        Assert.True(atHeight < atModel,
            "and declares m_fHeight BEFORE m_nModelIndex — the opposite of the table, which "
          + "is why this file copies the table: a class-order transcription would decode a "
          + "height as a model index and draw the wrong sprite with no error anywhere");

        Assert.True(IsCoordVector(te.Fields[0], "Mins"), "m_vecMins first");
        Assert.True(IsCoordVector(te.Fields[1], "Maxs"), "then m_vecMaxs");
        Assert.True(IsModelIndex(te.Fields[2], "ModelIndex"), "then the model index");
        Assert.True(IsWorldFloat(te.Fields[3], "Height"),
            "then m_fHeight, 17 bits over [MIN_COORD_INTEGER, MAX_COORD_INTEGER] with no flag");
        Assert.True(IsInt(te.Fields[4], "Count", 8), "then m_nCount, 8 bits unsigned");
        Assert.True(IsWorldFloat(te.Fields[5], "Speed"),
            "and m_fSpeed on the same world-sized range — which is the giveaway that stock "
          + "reached for a range wide enough for anything rather than one fitted to a speed");

        Assert.True(te.BaseBytes == 0, "DT_BaseTempEntity is empty, so its fields start at zero");
        Assert.True(te.BlobBytes == 40, "forty bytes in all");
    }

    // ---------------------------------------------------------------------
    // Against stock's own tables, read off disk.
    // ---------------------------------------------------------------------

    [Fact]
    public void BloodSpriteMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeBloodSprite>("te_bloodsprite.cpp", "CTEBloodSprite", "DT_TEBloodSprite",
            expected: ["Origin", "Direction", "ColorR", "ColorG", "ColorB", "ColorA",
                       "SprayModel", "DropModel", "Size"]);

    [Fact]
    public void BloodStreamMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeBloodStream>("te_bloodstream.cpp", "CTEBloodStream", "DT_TEBloodStream",
                                       ParticleSystemBytes,
                                       ["Direction", "ColorR", "ColorG", "ColorB", "ColorA", "Amount"]);

    /// <summary>
    /// Both of these read <c>te_armorricochet.cpp</c>, which holds TWO classes —
    /// the file is named for its second one. That is the other reason a lane
    /// scanning by filename ports 43 of 44 and the count still looks right.
    /// </summary>
    [Fact]
    public void MetalSparksMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeMetalSparks>("te_armorricochet.cpp", "CTEMetalSparks", "DT_TEMetalSparks",
            expected: ["Position", "Direction"]);

    /// <summary>
    /// Zero props on both sides, so <c>expected</c> is empty and says so: this
    /// table declares nothing and everything on its wire is the base's.
    /// </summary>
    [Fact]
    public void ArmorRicochetMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeArmorRicochet>("te_armorricochet.cpp", "CTEArmorRicochet",
                                         "DT_TEArmorRicochet", MetalSparksBytes,
                                         expected: []);

    [Fact]
    public void EnergySplashMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeEnergySplash>("te_energysplash.cpp", "CTEEnergySplash", "DT_TEEnergySplash",
            expected: ["Position", "Direction", "Explosive"]);

    [Fact]
    public void BubblesMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeBubbles>("te_bubbles.cpp", "CTEBubbles", "DT_TEBubbles",
                                   expected: ["Mins", "Maxs", "ModelIndex", "Height", "Count",
                                              "Speed"]);

    [Fact]
    public void BubbleTrailMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeBubbleTrail>("te_bubbletrail.cpp", "CTEBubbleTrail", "DT_TEBubbleTrail",
                                       expected: ["Mins", "Maxs", "ModelIndex", "WaterZ", "Count",
                                                  "Speed"]);

    [Fact]
    public void FizzMatchesTheTableItWasTranscribedFrom()
        => AssertParity<TeFizz>("te_fizz.cpp", "CTEFizz", "DT_TEFizz",
                                expected: ["Entity", "ModelIndex", "Density", "Current"]);

    /// <summary>
    /// The parser can see the two differences it exists to see.
    ///
    /// <para>
    /// <see cref="AssertParity{T}"/> is only worth something if a stock table
    /// that disagreed with the declaration would fail it — and every one of the
    /// eight above would compare empty tables if the parser quietly read
    /// nothing (a missing file or table throws). So this asserts the parse HAPPENED and that the parser reports
    /// this lane's two hazards differently.
    /// </para>
    /// </summary>
    [Fact]
    public void TheParserReadSomethingAndTellsARangedVectorFromACoordOne()
    {
        var stream = StockTable("te_bloodstream.cpp", "CTEBloodStream", "DT_TEBloodStream");
        var sprite = StockTable("te_bloodsprite.cpp", "CTEBloodSprite", "DT_TEBloodSprite");

        Assert.True(stream.Props.Count == 6, "it read six props out of DT_TEBloodStream");
        Assert.True(sprite.Props.Count == 9, "and nine out of DT_TEBloodSprite");

        StockProp ranged = stream.Props[0];
        StockProp coord  = sprite.Props[1];

        Assert.True(ranged.Member == coord.Member && ranged.Member == "m_vecDirection",
            "both tables send a prop called m_vecDirection");
        Assert.True(ranged.Function == "Vector" && coord.Function == "Vector",
            "and both send it as a SendPropVector");

        Assert.True(ranged.Bits == "11" && coord.Bits == "-1",
            "the parser reports eleven bits for one and -1 for the other");
        Assert.True(ranged.Low == "-10.0" && ranged.High == "10.0",
            "and reads the RANGE off the ranged one — the two arguments lane A's parser never "
          + "had a table that used");
        Assert.True(string.IsNullOrEmpty(coord.Low) && string.IsNullOrEmpty(coord.High),
            "while the COORD one has no range arguments at all");
        Assert.True(FlagsOf(ranged.Flags) != FlagsOf(coord.Flags),
            "and the flags differ, which is what makes the eight parity assertions above "
          + "capable of failing");
    }

    /// <summary>
    /// The parser tells a NOBASE macro from a based one — this lane's other
    /// hazard, and the one no width check would catch.
    /// </summary>
    [Fact]
    public void TheParserTellsANobaseTableFromABasedOne()
    {
        var sprite = StockTable("te_bloodsprite.cpp", "CTEBloodSprite", "DT_TEBloodSprite");
        var stream = StockTable("te_bloodstream.cpp", "CTEBloodStream", "DT_TEBloodStream");

        Assert.True(sprite.NoBase, "stock declares DT_TEBloodSprite with _NOBASE");
        Assert.True(!stream.NoBase, "and DT_TEBloodStream without it");
        Assert.True(sprite.NoBase != stream.NoBase,
            "the parser reports the two macros differently, which is what makes the base half "
          + "of every parity assertion above capable of failing");

        // And the same distinction inside ONE file, which is the case that would
        // defeat a per-file assumption.
        var sparks   = StockTable("te_armorricochet.cpp", "CTEMetalSparks", "DT_TEMetalSparks");
        var ricochet = StockTable("te_armorricochet.cpp", "CTEArmorRicochet", "DT_TEArmorRicochet");

        Assert.True(sparks is not null && ricochet is not null,
            "te_armorricochet.cpp holds both classes and the parser found each by name");
        Assert.True(sparks!.NoBase && !ricochet!.NoBase,
            "and reports the NOBASE one and the based one differently from the SAME file — "
          + "which is the chained base this lane exists to get right");
    }

    /// <summary>
    /// The two symbols this lane's declarations spell out as numbers, checked
    /// against the headers that define them.
    /// </summary>
    [Fact]
    public void TheWorldRangeAndTheTrailCountWidthAreWhatTheirDefinitionsSay()
    {
        // MIN_COORD_INTEGER / MAX_COORD_INTEGER, out of public/worldsize.h.
        (float low, float high) world = CoordIntegerRange();

        Assert.True(world.low == -16384f && world.high == 16384f,
            $"worldsize.h defines the coord integer range as "
          + $"[{world.low}, {world.high}]");

        foreach (TeField f in Build<TeBubbles>().Fields.Where(f => f.Kind == TePropKind.Float)
                     .Concat(Build<TeBubbleTrail>().Fields.Where(f => f.Kind == TePropKind.Float)))
        {
            Assert.True(f.Low == world.low && f.High == world.high,
                $"{f.Name}'s range is worldsize.h's own, not a number remembered here");
        }

        // BUBBLE_TRAIL_COUNT_BITS, out of te_bubbletrail.cpp's own enum.
        int? trailBits = EnumValueIn("te_bubbletrail.cpp", "BUBBLE_TRAIL_COUNT_BITS");

        Assert.True(trailBits == 8, $"BUBBLE_TRAIL_COUNT_BITS is {trailBits}");
        Assert.True(Build<TeBubbleTrail>().Fields[4].Bits == trailBits,
            "and DT_TEBubbleTrail's m_nCount is exactly that many bits — the literal 8 in the "
          + "declaration is checked against the enum rather than against a memory of it");
    }

    /// <summary>Where a DT_TEParticleSystem derivative's own fields start.</summary>
    private static int ParticleSystemBytes => TeClassBuilder.Build(typeof(TeParticleSystem)).BlobBytes;

    /// <summary>Where DT_TEArmorRicochet's own fields would start. Computed, never typed.</summary>
    private static int MetalSparksBytes => TeClassBuilder.Build(typeof(TeMetalSparks)).BlobBytes;

    /// <summary>
    /// One declaration, built with its base chain's size in front of it.
    ///
    /// <para>
    /// The base bytes are LOOKED UP from the base's own declaration rather than
    /// typed, so a field added to <see cref="TeMetalSparks"/> moves
    /// <see cref="TeArmorRicochet"/>'s offsets here exactly as it moves them in
    /// the registry.
    /// </para>
    /// </summary>
    private static TeClass Build<T>()
    {
        // THE SAME RESOLUTION THE REGISTRY DOES, and not attr.BaseTable alone.
        // A converted declaration takes its base from the C# class it derives
        // from and leaves BaseTable off the attribute entirely, so reading the
        // attribute answers null — the same answer a genuine NOBASE table gives,
        // and the same zero, until the day a base WITH FIELDS is the one
        // omitted. That is the bug lane RP found in RegisterAll; this helper had
        // it too.
        var attr = typeof(T).GetCustomAttribute<TempEntityAttribute>()!;

        // THE ATTRIBUTE ALONE NO LONGER ANSWERS THIS. A converted declaration
        // drops BaseTable and says its base by INHERITING it, so reading the
        // attribute by itself gives null — which this switch would take for
        // NOBASE and lay the blob out from offset zero, putting every one of the
        // class's own fields exactly where the base's props read. The same rule
        // TeClassBuilder.Build and TempEntRegistry apply, applied here too.
        string? baseTable = attr.BaseTable ?? TeClassBuilder.BaseTableOf(typeof(T));

        int baseBytes = baseTable switch
        {
            null                  => 0,
            "DT_BaseTempEntity"   => TeClassBuilder.Build(typeof(TeBaseTempEntity)).BlobBytes,
            "DT_TEParticleSystem" => ParticleSystemBytes,
            "DT_TEMetalSparks"    => MetalSparksBytes,
            _ => throw new InvalidOperationException(
                     $"{typeof(T).Name} nests {baseTable}, which this lane does not own"),
        };

        return TeClassBuilder.Build(typeof(T), baseBytes);
    }

    private static bool IsInt(TeField f, string name, int bits)
        => f.Name == name && f.Kind == TePropKind.Int && f.Bits == bits
        && f.BitsFrom == TeBits.Literal && f.Flags == TeFlags.Unsigned;

    private static bool IsCoordVector(TeField f, string name)
        => f.Name == name && f.Kind == TePropKind.Vector && f.Bits == -1
        && f.BitsFrom == TeBits.Literal && f.Flags == TeFlags.Coord
        && f.SizeBytes == 12;

    private static bool IsModelIndex(TeField f, string name)
        => f.Name == name && f.Kind == TePropKind.Int && f.BitsFrom == TeBits.ModelIndex
        && f.Bits == -1 && f.Flags == TeFlags.None;

    /// <summary>Seventeen bits over the whole world, with no encoding flag.</summary>
    private static bool IsWorldFloat(TeField f, string name)
        => f.Name == name && f.Kind == TePropKind.Float && f.Bits == 17
        && f.BitsFrom == TeBits.Literal && f.Flags == TeFlags.None
        && f.Low == -16384f && f.High == 16384f;

    // ---------------------------------------------------------------------
    // Stock's tables, parsed out of the .cpp they live in.
    // ---------------------------------------------------------------------

    /// <summary>One prop line out of a stock IMPLEMENT_SERVERCLASS_ST block.</summary>
    private readonly record struct StockProp(
        string Function, string Member, string? Bits, string? Flags, string? Low, string? High);

    /// <summary>
    /// One whole stock table: its props AND which macro declared it. The second
    /// half is this lane's own addition — a NOBASE table and a based one are
    /// spelled differently and nothing else says which is which.
    /// </summary>
    private sealed record StockTableInfo(List<StockProp> Props, bool NoBase);

    /// <summary>
    /// The whole parity check for one class: read stock's table off disk and
    /// assert the managed declaration agrees prop for prop — name, kind, width,
    /// flags, range — AND that the base matches the macro.
    /// <para>
    /// <paramref name="expected"/> IS WHAT A CONVERTED DECLARATION NEEDS AND
    /// WHY THIS TEST DID NOT HAVE TO BE DELETED. A declaration that has dropped
    /// its <c>WireName</c> sends each prop under its C# MEMBER's name, so the
    /// one assertion that compared the two spellings can no longer hold — and
    /// the other three, which are what actually catch a transcription slip,
    /// are untouched. Passing the expected names keeps the mapping written down
    /// and checked: stock's spelling stays in the failure message, so prop 6 of
    /// nine is still readable as the one stock called <c>m_nSprayModel</c>.
    /// Null means "not converted yet", and those classes still assert the two
    /// spellings are identical.
    /// </para>
    /// </summary>
    /// <param name="expected">
    /// What THIS declaration calls each prop, in wire order, for a class whose
    /// wire names are its C# members'. Null keeps stock's own spelling as the
    /// expectation, which is right for every declaration not yet converted —
    /// so this file's converted and unconverted classes are checked by the same
    /// helper without either forcing the other's state.
    /// </param>
    private static void AssertParity<T>(string fileName, string className, string tableName,
                                        int baseBytes = 0, string[]? expected = null)
    {
        StockTableInfo stock = StockTable(fileName, className, tableName);

        TeClass mine = TeClassBuilder.Build(typeof(T), baseBytes);

        // THE BASE, which for this lane is half the risk: NOBASE and based
        // tables sit side by side here and two of them are in one file.
        Assert.True(stock.NoBase == (mine.BaseTable is null),
            $"{tableName} is declared with {(stock.NoBase ? "IMPLEMENT_SERVERCLASS_ST_NOBASE" : "IMPLEMENT_SERVERCLASS_ST")} "
          + $"and the managed declaration {(mine.BaseTable is null ? "nests nothing" : $"nests {mine.BaseTable}")}");

        Assert.True(stock.Props.Count == mine.Fields.Length,
            $"stock's {tableName} declares {stock.Props.Count} prop(s) and the managed table has "
          + $"{mine.Fields.Length}");

        Assert.True(expected is null || expected.Length == stock.Props.Count,
            $"{tableName}: {expected?.Length} expected name(s) for stock's {stock.Props.Count} "
          + "prop(s) — the list is per-position, so a missing entry would silently stop checking "
          + "a prop");

        for (int i = 0; i < stock.Props.Count; i++)
        {
            StockProp s = stock.Props[i];
            TeField   m = mine.Fields[i];

            // MATCHED BY POSITION, which is what order-is-wire-order always
            // meant; only the NAME expectation moves when a class converts, and
            // stock's own spelling stays in the message so the mapping is still
            // readable from the failure.
            string want = expected is not null ? expected[i] : s.Member;

            Assert.True(m.Name == want,
                expected is not null
                    ? $"{tableName} prop {i} is '{want}' on the wire, where stock called it {s.Member}"
                    : $"{tableName} prop {i} is called {s.Member} on both sides");
            Assert.True(KindOf(s.Function) == m.Kind,
                $"{tableName}.{s.Member} is a SendProp{s.Function} on both sides");

            if (s.Function == "ModelIndex")
            {
                // SendPropModelIndex takes no width and no flags of its own:
                // dt_send.h:750 supplies SP_MODEL_INDEX_BITS and 0.
                Assert.True(m.BitsFrom == TeBits.ModelIndex,
                    $"{tableName}.{s.Member} takes its width from SP_MODEL_INDEX_BITS on both sides");
                Assert.True(m.Flags == TeFlags.None,
                    $"{tableName}.{s.Member} is SIGNED on both sides — SendPropModelIndex passes flags 0");
                continue;
            }

            // The width, in whichever of the three forms stock used: a symbol
            // from a shared header travels as a TeBits and native resolves it; a
            // symbol local to this .cpp is resolved from its own enum; a literal
            // stays a literal.
            if (BitsSymbols.TryGetValue(s.Bits ?? "", out TeBits symbol))
            {
                Assert.True(m.BitsFrom == symbol,
                    $"{tableName}.{s.Member} is {s.Bits} wide on both sides, as a symbol and not a number");
            }
            else
            {
                int? local = LocalEnums.TryGetValue(s.Bits ?? "", out int value) ? value : null;

                Assert.True(m.BitsFrom == TeBits.Literal,
                    $"{tableName}.{s.Member}'s width is not a shared symbol in stock's table, so it "
                  + "is a literal here");
                Assert.True(m.Bits.ToString(CultureInfo.InvariantCulture)
                            == (local?.ToString(CultureInfo.InvariantCulture) ?? s.Bits),
                    $"{tableName}.{s.Member} is {s.Bits} bits on both sides");
            }

            Assert.True(FlagsOf(s.Flags) == m.Flags,
                $"{tableName}.{s.Member} carries {(string.IsNullOrEmpty(s.Flags) ? "0" : s.Flags)} on both sides");

            Assert.True(NumberOf(s.Low) == m.Low && NumberOf(s.High) == m.High,
                $"{tableName}.{s.Member} spans [{NumberOf(s.Low)}, {NumberOf(s.High)}] on both sides");
        }
    }

    private static readonly Dictionary<string, TeBits> BitsSymbols = new(StringComparer.Ordinal)
    {
        ["MAX_EDICT_BITS"]                  = TeBits.MaxEdict,
        ["SP_MODEL_INDEX_BITS"]             = TeBits.ModelIndex,
        ["EF_MAX_BITS"]                     = TeBits.EfMax,
        ["ANIMATION_SKIN_BITS"]             = TeBits.AnimationSkin,
        ["NUM_NETWORKED_EHANDLE_BITS"]      = TeBits.NetworkedEHandle,
        ["MAX_EFFECT_FLAG_BITS"]            = TeBits.EffectFlag,
        ["MAX_EFFECT_DISPATCH_STRING_BITS"] = TeBits.EffectDispatchString,
    };

    /// <summary>
    /// Widths that are symbols in stock's table but are LOCAL to the .cpp that
    /// uses them — so they belong in the declaration as literals, and are
    /// resolved here by reading the enum that defines them.
    /// </summary>
    private static readonly Dictionary<string, int> LocalEnums = BuildLocalEnums();

    private static Dictionary<string, int> BuildLocalEnums()
    {
        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        int? trail = EnumValueIn("te_bubbletrail.cpp", "BUBBLE_TRAIL_COUNT_BITS");

        if (trail is not null)
            found["BUBBLE_TRAIL_COUNT_BITS"] = trail.Value;

        return found;
    }

    /// <summary>An <c>enum { NAME = 8, };</c> member, read out of a source file.</summary>
    private static int? EnumValueIn(string fileName, string symbol)
    {
        string path = ValveSdk.Server(fileName);

        var m = Regex.Match(File.ReadAllText(path),
            Regex.Escape(symbol) + @"\s*=\s*(-?\d+)");

        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// <c>[MIN_COORD_INTEGER, MAX_COORD_INTEGER]</c>, read out of
    /// <c>public/worldsize.h</c> — the definition the two 17-bit floats in this
    /// lane are declared against.
    /// </summary>
    private static (float Low, float High) CoordIntegerRange()
    {
        string path = ValveSdk.Find("src/public/worldsize.h");

        var m = Regex.Match(File.ReadAllText(path),
            @"#define\s+MAX_COORD_INTEGER\s*\(?\s*(\d+)\s*\)?");

        if (!m.Success)
            throw new InvalidOperationException("worldsize.h has no MAX_COORD_INTEGER");

        float high = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);

        // MIN_COORD_INTEGER is defined as (-MAX_COORD_INTEGER), which is worth
        // asserting rather than assuming: a build that split the two would give
        // an asymmetric range and every coordinate would decode offset.
        Assert.True(Regex.IsMatch(File.ReadAllText(path),
                @"#define\s+MIN_COORD_INTEGER\s*\(\s*-\s*MAX_COORD_INTEGER\s*\)"),
            "worldsize.h defines MIN_COORD_INTEGER as the negation of MAX_COORD_INTEGER");

        return (-high, high);
    }

    private static TePropKind KindOf(string sendPropFunction) => sendPropFunction switch
    {
        "Int"        => TePropKind.Int,
        "ModelIndex" => TePropKind.Int,      // SendPropInt( x, SP_MODEL_INDEX_BITS, 0 )
        "Float"      => TePropKind.Float,
        "Vector"     => TePropKind.Vector,
        "Angle"      => TePropKind.Angle,
        "QAngles"    => TePropKind.QAngles,
        _            => throw new InvalidOperationException(
                            $"SendProp{sendPropFunction} is not a kind this knows"),
    };

    private static TeFlags FlagsOf(string? spropExpression)
    {
        TeFlags flags = TeFlags.None;

        foreach (string token in (spropExpression ?? "").Split('|', StringSplitOptions.TrimEntries))
        {
            flags |= token switch
            {
                "0" or ""                     => TeFlags.None,
                "SPROP_UNSIGNED"              => TeFlags.Unsigned,
                "SPROP_COORD"                 => TeFlags.Coord,
                "SPROP_NOSCALE"               => TeFlags.NoScale,
                "SPROP_ROUNDDOWN"             => TeFlags.RoundDown,
                "SPROP_ROUNDUP"               => TeFlags.RoundUp,
                "SPROP_NORMAL"                => TeFlags.Normal,
                "SPROP_COORD_MP"              => TeFlags.CoordMp,
                "SPROP_COORD_MP_LOWPRECISION" => TeFlags.CoordMpLowPrecision,
                "SPROP_COORD_MP_INTEGRAL"     => TeFlags.CoordMpIntegral,
                _ => throw new InvalidOperationException($"'{token}' is not an SPROP_ this knows"),
            };
        }

        return flags;
    }

    /// <summary>
    /// A stock range bound: a literal like <c>-10.0</c> or <c>25.6f</c>, a
    /// SYMBOL like <c>MAX_COORD_INTEGER</c>, or absent.
    ///
    /// <para>
    /// THE SYMBOLS ARE RESOLVED FROM THE HEADER, not from a number written here
    /// — that is the whole point of reading the table off disk. When
    /// <c>worldsize.h</c> is not beside the binary the fallback is the value the
    /// declarations use, which degrades this one check to a tautology and is
    /// said out loud rather than hidden.
    /// </para>
    /// </summary>
    private static float NumberOf(string? literal)
    {
        string text = (literal ?? "").Trim();

        if (text.Length == 0)
            return 0f;

        if (text is "MAX_COORD_INTEGER" or "MIN_COORD_INTEGER")
        {
            (float Low, float High)? range = CoordIntegerRange();
            float high = range?.High ?? 16384f;

            return text[1] == 'A' ? high : -high;
        }

        return float.Parse(text.TrimEnd('f', 'F'), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Stock's own table, parsed out of its .cpp in the pristine Valve SDK. A
    /// missing file or table throws: an oracle never passes by reading nothing.
    ///
    /// <para>
    /// The arguments are split on TOP-LEVEL commas rather than matched by one
    /// regex, because this lane's tables have five argument shapes —
    /// <c>SendPropModelIndex( SENDINFO(x) )</c> with no width at all,
    /// <c>SendPropInt( SENDINFO(x), 16 )</c> with no flags,
    /// <c>SendPropInt( SENDINFO(x), MAX_EDICT_BITS, SPROP_UNSIGNED )</c>,
    /// <c>SendPropVector( SENDINFO(x), 11, 0, -10.0, 10.0 )</c> and
    /// <c>SendPropFloat( SENDINFO(x), 17, 0, MIN_COORD_INTEGER, MAX_COORD_INTEGER )</c>
    /// — and a regex covering all five is a regex nobody can check by reading.
    /// </para>
    /// </summary>
    private static StockTableInfo StockTable(string fileName, string className, string tableName)
    {
        string path = ValveSdk.Server(fileName);

        var block = Regex.Match(File.ReadAllText(path),
            @"IMPLEMENT_SERVERCLASS_ST(_NOBASE)?\s*\(\s*" + Regex.Escape(className) + @"\s*,\s*"
            + Regex.Escape(tableName) + @"\s*\)(.*?)END_SEND_TABLE", RegexOptions.Singleline);

        if (!block.Success)
            throw new InvalidOperationException($"{fileName} has no {className} / {tableName} send table");

        bool noBase = block.Groups[1].Success;
        string body = block.Groups[2].Value;

        var props = new List<StockProp>();

        foreach (Match call in Regex.Matches(body, @"SendProp(\w+)\s*\("))
        {
            string function = call.Groups[1].Value;
            var    args     = SplitTopLevel(ArgumentsOf(body, call.Index + call.Length));

            var sendinfo = Regex.Match(args[0], @"SENDINFO(?:_VECTORELEM|_ARRAYELEM)?\s*\(\s*(\w+)");

            if (!sendinfo.Success)
                throw new InvalidOperationException($"{tableName}: '{args[0]}' is not a SENDINFO");

            props.Add(new StockProp(
                Function: function,
                Member:   sendinfo.Groups[1].Value,
                Bits:     args.Count > 1 ? args[1] : null,
                Flags:    args.Count > 2 ? args[2] : null,
                Low:      args.Count > 3 ? args[3] : null,
                High:     args.Count > 4 ? args[4] : null));
        }

        return new StockTableInfo(props, noBase);
    }

    /// <summary>The text between an already-opened paren and its match.</summary>
    private static string ArgumentsOf(string text, int afterOpenParen)
    {
        int depth = 1;

        for (int i = afterOpenParen; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0)
                return text[afterOpenParen..i];
        }

        throw new InvalidOperationException("unbalanced parentheses in a send table");
    }

    private static List<string> SplitTopLevel(string arguments)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;

        for (int i = 0; i < arguments.Length; i++)
        {
            if (arguments[i] == '(') depth++;
            else if (arguments[i] == ')') depth--;
            else if (arguments[i] == ',' && depth == 0)
            {
                parts.Add(arguments[start..i].Trim());
                start = i + 1;
            }
        }

        string last = arguments[start..].Trim();

        if (last.Length > 0)
            parts.Add(last);

        return parts;
    }
}

