using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

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
/// </summary>
public class TempEntGoreTests
{
    // ---------------------------------------------------------------------
    // The Part 6 rows.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheBloodSpriteTableIsNobaseAndCarriesItsOwnOrigin()
    {
        TeClass te = Build<TeBloodSprite>();

        Assert.True(te.ClassName == "CTEBloodSprite", "the two sides pair on CTEBloodSprite");
        Assert.True(te.TableName == "DT_TEBloodSprite", "over DT_TEBloodSprite");
        Assert.True(te.BaseTable is null,
            "it is NOBASE — CTEBloodSprite derives from CBaseTempEntity in C++, but its send "
          + "table nests nothing, as IMPLEMENT_SERVERCLASS_ST_NOBASE says");
        Assert.True(te.RegistersClass,
            "te_bloodsprite.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 9, "nine props");
        Assert.True(te.BaseBytes == 0, "with nothing in front of them");

        Assert.True(IsCoordVector(te.Fields[0], "Origin"),
            "Origin (stock's m_vecOrigin) first — its OWN, because NOBASE means there is no "
          + "DT_TEParticleSystem "
          + "underneath it to supply one");
        Assert.True(IsCoordVector(te.Fields[1], "Direction"),
            "then Direction, stock's m_vecDirection");

        Assert.True(IsInt(te.Fields[2], "ColorR", 8),
            "then R, 8 bits unsigned — stock's one-letter r, and the rename is a CASE change, "
          + "which the forwards' Q_strcmp is not forgiving about");
        Assert.True(IsInt(te.Fields[3], "ColorG", 8), "then G");
        Assert.True(IsInt(te.Fields[4], "ColorB", 8), "then B");
        Assert.True(IsInt(te.Fields[5], "ColorA", 8), "then A");

        Assert.True(IsModelIndex(te.Fields[6], "SprayModel"),
            "then SprayModel — a SIGNED SP_MODEL_INDEX_BITS int, because SendPropModelIndex "
          + "passes flags 0 and not SPROP_UNSIGNED");
        Assert.True(IsModelIndex(te.Fields[7], "DropModel"), "then DropModel");

        Assert.True(IsInt(te.Fields[8], "Size", 8), "and Size last, 8 bits unsigned");

        Assert.True(te.Fields[0].Offset == 0 && te.Fields[1].Offset == 12
                 && te.Fields[2].Offset == 24 && te.Fields[8].Offset == 36,
            "at 0, 12, 24 and 36 -- the colour is four bytes where its four ints were sixteen");
        Assert.True(te.BlobBytes == 40, "forty bytes in all -- twelve fewer than when the colour was four ints");
    }

    /// <summary>
    /// The lane's one vector that is neither COORD nor NORMAL nor NOSCALE, and
    /// the assertion that keeps it from being tidied into one.
    /// </summary>
    [Fact]
    public void TheBloodStreamDirectionIsElevenBitsOverAnExplicitRangeWithNoFlag()
    {
        TeClass te = Build<TeBloodStream>();

        Assert.True(te.ClassName == "CTEBloodStream" && te.TableName == "DT_TEBloodStream",
            "the two sides pair on CTEBloodStream over DT_TEBloodStream");
        Assert.True(te.BaseTable == "DT_TEParticleSystem",
            "and it is BASED, where DT_TEBloodSprite is NOBASE — the macro without _NOBASE "
          + "nests CTEParticleSystem's table, which is where m_vecOrigin comes from");
        Assert.True(te.RegistersClass, "te_bloodstream.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 6, "six props of its own");

        TeField dir = te.Fields[0];

        Assert.True(dir.Name == "Direction" && dir.Kind == TePropKind.Vector,
            "Direction — stock's m_vecDirection — is one SendPropVector, the kind INFERRED "
          + "from the Vector member");
        Assert.True(dir.Bits == 11 && dir.BitsFrom == TeBits.Literal,
            "eleven bits, a literal in stock's table and a literal here");
        Assert.True(dir.Flags == TeFlags.None,
            "and NO encoding flag at all — not COORD, not NORMAL, not NOSCALE. The flags "
          + "argument really is a zero and the range is the encoding");
        Assert.True(dir.Low == -10f && dir.High == 10f,
            "over [-10, 10], which is the slack UTIL_RandomBloodVector's roughly-unit vectors "
          + "need; SPROP_NORMAL would quantise to [-1,1] and send every spurt straight up");

        // ONE Color32 MEMBER, FOUR PROPS -- the declaration says the colour
        // once and TeClassBuilder expands it, so the wire still has stock's four
        // separate eight-bit components in stock's order.
        Assert.True(IsInt(te.Fields[1], "ColorR", 8), "then ColorR (stock: r), 8 bits unsigned");
        Assert.True(IsInt(te.Fields[2], "ColorG", 8), "then ColorG");
        Assert.True(IsInt(te.Fields[3], "ColorB", 8), "then ColorB");
        Assert.True(IsInt(te.Fields[4], "ColorA", 8), "then ColorA");
        Assert.True(IsInt(te.Fields[5], "Amount", 8),
            "and Amount (m_nAmount), 8 bits unsigned — which is why UTIL_BloodStream clamps with "
          + "MIN( amount, 255 ) before the wire ever sees it");

        Assert.True(te.BaseBytes == 12,
            "its own fields start where DT_TEParticleSystem's three COORD floats end");
        // TWELVE BYTES SHORTER THAN IT WAS. The colour is one Color32 member at
        // four bytes -- stock's color32 -- where it used to be four ints at
        // sixteen, so Amount moved from 40 to 28 and the blob from 44 to 32.
        // The engine's event cap is 192, so twelve bytes is not nothing.
        Assert.True(te.Fields[0].Offset == 12 && te.Fields[5].Offset == 28, "at 12 and 28");
        Assert.True(te.BlobBytes == 32, "thirty-two bytes in all");

        // The half that matters: the SAME QUANTITY on a lane-mate carries a
        // completely different encoding, and the two must not converge.
        //
        // THEY BOTH CALL IT Direction NOW, which neither lane could say alone.
        // Stock spelled both m_vecDirection; BloodStream's declaration converted
        // in one lane and BloodSprite's in another, so each lane's own copy of
        // this comment described the other's as unconverted and both were right
        // when written. The name was never the hazard anyway -- two identical
        // spellings carrying DIFFERENT ENCODINGS was -- and the parity tests read
        // both spellings off stock's own .cpp regardless. The shared MEMBER is
        // what is asserted, because that is what survives every lane landing.
        TeField sprite = Build<TeBloodSprite>().Fields[1];

        Assert.True(sprite.MemberName == dir.MemberName && dir.MemberName == "Direction",
            "both tables carry the same quantity off the same C# member — stock spelled both "
          + "m_vecDirection, and the encodings compared below are the reason that matters");
        Assert.True(sprite.Flags == TeFlags.Coord && sprite.Bits == -1,
            "and DT_TEBloodSprite sends it as a plain COORD vector with no range");
        Assert.True(sprite.Low != dir.Low || sprite.Flags != dir.Flags,
            "the two encodings are different, which is why saying so is worth an assertion");
    }

    /// <summary>
    /// The inventory's only table that is a concrete effect AND somebody else's
    /// base — and therefore the only row in Part 6 where "registers a class" and
    /// "is a base" are two independent bits.
    /// </summary>
    [Fact]
    public void MetalSparksIsBothAnEffectAndABaseAndClaimsItsServerClass()
    {
        TeClass te = Build<TeMetalSparks>();

        Assert.True(te.ClassName == "CTEMetalSparks" && te.TableName == "DT_TEMetalSparks",
            "the two sides pair on CTEMetalSparks over DT_TEMetalSparks");
        Assert.True(te.BaseTable is null, "it is NOBASE");

        // The decision this test exists for. TeBaseTempEntity passes
        // RegistersClass = false because it is PURELY a base whose native
        // ServerClass is still registered; neither half of that applies here.
        Assert.True(te.RegistersClass,
            "it DOES claim a ServerClass, unlike DT_BaseTempEntity: CTEMetalSparks is a real "
          + "effect with a live caller (EffectsServer.cpp:164), and te_armorricochet.cpp — "
          + "which holds it — is evicted, so the name is free");

        // DT_TEParticleSystem WAS the second half of this contrast and is not
        // any more: C-Phase 3's lane CP evicted its native halves on BOTH sides
        // and it claims its class now. That is a fact about a base being FREE,
        // not about it being a base — which is exactly what the surviving half
        // of the contrast says.
        Assert.True(!Build<TeBaseTempEntity>().RegistersClass,
            "the one base whose native class this build still registers does not, which is "
          + "the contrast that makes the line above a decision rather than a default");
        Assert.True(Build<TeParticleSystem>().RegistersClass,
            "and DT_TEParticleSystem, whose native halves are both gone, does — a base claims "
          + "its class the moment nothing native holds the name, on either side");

        Assert.True(te.Fields.Length == 2, "two props");
        Assert.True(IsCoordVector(te.Fields[0], "Position"),
            "Position — stock spelt it m_vecPos and not m_vecOrigin, and the two sides still "
          + "pair by name; the name is the member's now and both halves take it from here");
        Assert.True(IsCoordVector(te.Fields[1], "Direction"),
            "and Direction, stock's m_vecDir — which THREE other tables also send under that "
          + "name and which only this one and DT_TEArmorRicochet have renamed");

        Assert.True(te.Fields[0].Offset == 0 && te.Fields[1].Offset == 12, "at 0 and 12");
        Assert.True(te.BlobBytes == 24, "twenty-four bytes in all");
    }

    [Fact]
    public void ArmorRicochetDeclaresNoPropsAndNestsAConcreteEffect()
    {
        TeClass te = Build<TeArmorRicochet>();

        Assert.True(te.ClassName == "CTEArmorRicochet" && te.TableName == "DT_TEArmorRicochet",
            "the two sides pair on CTEArmorRicochet over DT_TEArmorRicochet");
        Assert.True(te.RegistersClass, "te_armorricochet.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 0,
            "no props of its own — IMPLEMENT_SERVERCLASS_ST is followed straight by "
          + "END_SEND_TABLE and that is the whole table");

        // THE CHAINED BASE. Every other based table in the inventory nests a
        // PURE base (DT_BaseTempEntity, DT_TEParticleSystem, DT_BaseBeam); this
        // one nests an effect with a ServerClass and callers of its own.
        Assert.True(te.BaseTable == "DT_TEMetalSparks",
            "it nests DT_TEMetalSparks, which is not a pure base but a concrete effect");
        Assert.True(Build<TeMetalSparks>().RegistersClass,
            "and that base registers a class of its own, which is what makes this chain the "
          + "only one of its shape in the inventory");

        Assert.True(te.BaseBytes == 24, "its own fields would start after the base's two vectors");
        Assert.True(te.BlobBytes == 24,
            "and there are none, so the blob is exactly the base's twenty-four bytes");

        // Prop for prop identical to DT_TEMetalSparks and deliberately NOT
        // merged with it: the difference is what the client does with it.
        Assert.True(te.TableName != Build<TeMetalSparks>().TableName,
            "it sends what DT_TEMetalSparks sends and is still its own table, because "
          + "c_te_armorricochet.cpp plays something else with it and the set of registered "
          + "names must be exactly stock's (plan 2E)");
    }

    [Fact]
    public void TheEnergySplashTableEndsInABoolThatIsOneBitAndOneByte()
    {
        TeClass te = Build<TeEnergySplash>();

        Assert.True(te.ClassName == "CTEEnergySplash" && te.TableName == "DT_TEEnergySplash",
            "the two sides pair on CTEEnergySplash over DT_TEEnergySplash");
        Assert.True(te.BaseTable is null, "it is NOBASE");
        Assert.True(te.RegistersClass, "te_energysplash.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 3, "three props");
        Assert.True(IsCoordVector(te.Fields[0], "Position"), "Position, stock's m_vecPos");
        Assert.True(IsCoordVector(te.Fields[1], "Direction"), "then Direction, stock's m_vecDir");

        TeField explosive = te.Fields[2];

        Assert.True(IsInt(explosive, "Explosive", 1),
            "and Explosive, stock's m_bExplosive, one bit unsigned");

        // The half a reader would get wrong. SendPropInt picks its read proxy
        // from the STORAGE width (dt_send.cpp:492); stock's member is a
        // CNetworkVar( bool ), so SENDINFO hands it a 1.
        Assert.True(explosive.SizeBytes == 1,
            "and ONE byte of storage, because stock's member is a bool — an int here would "
          + "describe a one-byte value as four and read three bytes past it");

        Assert.True(te.Fields[0].Offset == 0 && te.Fields[1].Offset == 12
                 && explosive.Offset == 24,
            "at 0, 12 and 24");
        Assert.True(te.BlobBytes == 28,
            "twenty-eight bytes: twenty-five rounded up to the layout's four");
    }

    [Fact]
    public void BubbleTrailIsBubblesWithOneNameChangedAndStillItsOwnTable()
    {
        TeClass te = Build<TeBubbleTrail>();
        TeClass bubbles = Build<TeBubbles>();

        Assert.True(te.ClassName == "CTEBubbleTrail" && te.TableName == "DT_TEBubbleTrail",
            "the two sides pair on CTEBubbleTrail over DT_TEBubbleTrail");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "nesting the empty DT_BaseTempEntity");
        Assert.True(te.RegistersClass, "te_bubbletrail.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 6, "six props");
        Assert.True(IsCoordVector(te.Fields[0], "Mins"), "Mins");
        Assert.True(IsCoordVector(te.Fields[1], "Maxs"), "Maxs");
        Assert.True(IsModelIndex(te.Fields[2], "ModelIndex"), "the model index");
        Assert.True(IsWorldFloat(te.Fields[3], "WaterZ"),
            "m_flWaterZ — fl, not f, because it is a world Z and not a distance");
        Assert.True(IsInt(te.Fields[4], "Count", 8), "m_nCount, 8 bits unsigned");
        Assert.True(IsWorldFloat(te.Fields[5], "Speed"), "and m_fSpeed");

        Assert.True(te.BlobBytes == bubbles.BlobBytes && te.BlobBytes == 40,
            "the same forty bytes DT_TEBubbles has");

        // ONE NAME APART, and not unified: the client pairs props BY NAME.
        string[] mine  = te.Fields.Select(f => f.Name).ToArray();
        string[] hers = bubbles.Fields.Select(f => f.Name).ToArray();

        Assert.True(mine[3] == "WaterZ" && hers[3] == "Height",
            "the two differ in exactly one prop name — stock's m_flWaterZ against its m_fHeight, "
          + "WaterZ against Height here — which is the whole difference between the tables");
        Assert.True(te.TableName != bubbles.TableName,
            "and they are still two tables, because renaming a prop the other side is looking "
          + "for is an unpaired prop (plan 2E)");
    }

    /// <summary>
    /// <c>DT_TEFizz</c> — BEHAVIOUR LEDGER ROW S1.
    /// </summary>
    [Fact]
    public void TheFizzTableIsAnEdictIndexAModelAndASignedCurrent()
    {
        TeClass te = Build<TeFizz>();

        Assert.True(te.ClassName == "CTEFizz" && te.TableName == "DT_TEFizz",
            "the two sides pair on CTEFizz over DT_TEFizz");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "nesting the empty DT_BaseTempEntity");
        Assert.True(te.RegistersClass, "te_fizz.cpp is evicted, so this claims the ServerClass");

        Assert.True(te.Fields.Length == 4, "four props");

        TeField entity = te.Fields[0];

        // The field rows S1 and C6 are both about.
        Assert.True(entity.Name == "Entity" && entity.Kind == TePropKind.Int,
            "m_nEntity first, an int");
        Assert.True(entity.BitsFrom == TeBits.MaxEdict && entity.Bits == -1,
            "MAX_EDICT_BITS wide, resolved natively and never typed here");
        Assert.True(entity.Flags == TeFlags.Unsigned, "and unsigned");

        Assert.True(IsModelIndex(te.Fields[1], "ModelIndex"),
            "then m_nModelIndex — sprites/bubble.vmt, signed, SP_MODEL_INDEX_BITS wide");
        Assert.True(IsInt(te.Fields[2], "Density", 8), "then m_nDensity, 8 bits unsigned");

        TeField current = te.Fields[3];

        // The one signed int in the lane, and the one a "tidy" would break.
        Assert.True(current.Name == "Current" && current.Kind == TePropKind.Int
                 && current.Bits == 16 && current.BitsFrom == TeBits.Literal,
            "and m_nCurrent, sixteen bits");
        Assert.True(current.Flags == TeFlags.None,
            "SIGNED — SendPropInt(SENDINFO(m_nCurrent), 16) passes no flags argument at all, so "
          + "a current running the other way along the axis is a negative number rather than a "
          + "very fast positive one");

        Assert.True(te.Fields[0].Offset == 0 && te.Fields[3].Offset == 12, "at 0 and 12");
        Assert.True(te.BlobBytes == 16, "sixteen bytes in all");
    }

    // ---------------------------------------------------------------------
    // Behaviour ledger row S1 — the shape of the restoration, not the table.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Row S1 is a SIGNATURE, and this is the assertion that pins it.
    ///
    /// <para>
    /// Every other method on <see cref="ITempEnts"/> is stock's
    /// <c>ITempEntsSystem</c> shape on purpose (plan Part 8), and
    /// <see cref="ITempEnts.Fizz"/> is the ONE deviation: an <c>int
    /// entIndex</c> where stock takes a <c>const CBaseEntity *</c>. If someone
    /// later "restores" the interface parameter to match stock's, the effect
    /// stops being reachable the moment <c>CBaseEntity</c> becomes an alias —
    /// which is exactly the state row S1 records this build already being in.
    /// </para>
    /// </summary>
    [Fact]
    public void FizzTakesAnEdictIndexAndNotAnEntityWhichIsTheWholeOfRowS1()
    {
        MethodInfo? fizz = typeof(ITempEnts).GetMethod("Fizz");

        Assert.True(fizz is not null, "ITempEnts declares Fizz — the effect has a managed route again");

        ParameterInfo[] p = fizz!.GetParameters();

        Assert.True(p.Length == 6, "six parameters: filter, delay, entity, model, density, current");
        Assert.True(p[0].ParameterType == typeof(TeFilter), "a TeFilter, as every method here takes");
        Assert.True(p[1].ParameterType == typeof(float), "a delay");

        Assert.True(p[2].ParameterType == typeof(int),
            "AN INT, not an entity of any kind. DT_TEFizz has only ever put an edict index on "
          + "the wire and TE_Fizz took its const IServerEntity * solely to run ENTINDEX on it, "
          + "so the interface was never needed — which is the whole of ledger row S1");
        Assert.True(p[2].Name == "entIndex", "and it is named for what it is");

        Assert.True(p[3].ParameterType == typeof(int) && p[4].ParameterType == typeof(int)
                 && p[5].ParameterType == typeof(int),
            "then the model index, the density and the current");

        // Nothing on this interface reaches for an entity, which is what makes
        // the whole of ITempEnts survive CBaseEntity becoming an alias.
        foreach (MethodInfo m in typeof(ITempEnts).GetMethods())
        {
            foreach (ParameterInfo one in m.GetParameters())
            {
                Assert.True(!one.ParameterType.Name.Contains("Entity", StringComparison.Ordinal),
                    $"ITempEnts.{m.Name} takes no entity type either — {one.Name} is a "
                  + $"{one.ParameterType.Name}");
            }
        }
    }

    /// <summary>
    /// <c>sprites/bubble.vmt</c> is still precached, which is the one thing this
    /// lane could have quietly dropped.
    ///
    /// <para>
    /// <c>CTEFizz::Precache</c> (<c>te_fizz.cpp:82</c>) took it, and
    /// <c>CBaseTempEntity::PrecacheTempEnts</c> reached it by walking the list of
    /// temp entity singletons. <c>CTEFizz</c> is no longer on that list, so
    /// without <see cref="SourceSharp.Server.ServerPrecache.PrecacheAll(IEnginePrecache, ILog, System.Reflection.Assembly[])"/> the model would
    /// stop being precached and NOTHING would say so: the symptom is a missing
    /// model at runtime, not a build error.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFizzPrecacheSurvivedTheEvictionOfItsNativeClass()
    {
        var engine = Fake();
        engine.Returns("Precache.PrecacheModel", 71);

        // THE SWEEP ITSELF, which is what LevelInit runs. TempEnts.Precache was
        // a one-line delegate to it, kept only so no caller was stranded while
        // the [Precache] attribute landed; calling the real thing here means
        // this test exercises the path the game does.
        SourceSharp.Server.ServerPrecache.PrecacheAll(engine, new RecordingLog(), typeof(SourceSharp.Shared.TeSparks).Assembly);

        var asked = engine.Calls
            .Where(c => c.Member == "Precache.PrecacheModel")
            .Select(c => c.ArgAs<string>(0))
            .ToList();

        Assert.True(asked.Contains("sprites/bubble.vmt"),
            "CTEFizz's model is precached from managed code now that its native class is gone "
          + $"— asked for [{string.Join(", ", asked)}]");
        Assert.True(asked.Contains("sprites/gunsmoke.vmt"),
            "and lane C's is still on the same list, which is the point of there being one list");
    }

    // ---------------------------------------------------------------------
    // Discovery, ordering, and the blob a writer reaches.
    // ---------------------------------------------------------------------

    [Fact]
    public void TheRegistryFindsAllEightAndPublishesMetalSparksBeforeArmorRicochet()
    {
        var registry = Registry();

        foreach (string name in GoreClassNames)
            Assert.True(registry.Find(name) is not null, $"{name} was discovered by attribute");

        var order = registry.All.Select(t => t.Class.TableName).ToList();

        int sparksAt   = order.IndexOf("DT_TEMetalSparks");
        int ricochetAt = order.IndexOf("DT_TEArmorRicochet");

        Assert.True(sparksAt >= 0 && ricochetAt >= 0, "both are in the published list");
        Assert.True(sparksAt < ricochetAt,
            "and DT_TEMetalSparks is published BEFORE DT_TEArmorRicochet, which nests it — the "
          + "chained base is the one place where the registry's base-first pass has to walk two "
          + "links and not one");

        int particleAt = order.IndexOf("DT_TEParticleSystem");

        Assert.True(particleAt >= 0 && particleAt < order.IndexOf("DT_TEBloodStream"),
            "and DT_TEParticleSystem before DT_TEBloodStream, which nests it");
    }

    /// <summary>
    /// A writer for <c>CTEArmorRicochet</c> reaches the two fields
    /// <c>DT_TEMetalSparks</c> declares, though its own table declares none —
    /// they are in the base's TABLE and in the derived's BLOB.
    /// </summary>
    [Fact]
    public void AWriterReachesTheMetalSparksFieldsFromArmorRicochet()
    {
        TempEntType ricochet = Registered("CTEArmorRicochet");

        Assert.True(ricochet.Fields.Count == 2,
            "two fields in all, and both of them inherited");
        Assert.True(ricochet.Fields.ContainsKey("Position"),
            "it can set Position, though DT_TEArmorRicochet declares nothing");
        Assert.True(ricochet.Fields.ContainsKey("Direction"), "and Direction");

        Assert.True(ricochet.Fields["Position"].Offset == 0
                 && ricochet.Fields["Direction"].Offset == 12,
            "at the base's own offsets, because the base is nested at datatable offset zero and "
          + "its props read out of the SAME blob");

        // And the same one link down, through a pure base.
        TempEntType stream = Registered("CTEBloodStream");

        Assert.True(stream.Fields.ContainsKey("Origin"),
            "CTEBloodStream can set Origin, which is DT_TEParticleSystem's");
        Assert.True(stream.Fields.Count == 7,
            "seven fields: one inherited and six of its own");
    }

    [Fact]
    public void TheWriterPutsAFizzAndABloodStreamWhereTheOffsetsSay()
    {
        TempEntType fizz = Registered("CTEFizz");

        var blob = new byte[fizz.BlobBytes];
        var writer = new TempEntWriter(fizz, blob);

        Assert.True(writer.Int("Entity", 4097), "the entity index is written");
        Assert.True(writer.Int("ModelIndex", 71), "the model index is written");
        Assert.True(writer.Int("Density", 200), "the density is written");
        Assert.True(writer.Int("Current", -100), "and a NEGATIVE current is written");

        Assert.True(BitConverter.ToInt32(blob, 0) == 4097, "the entity index at byte 0");
        Assert.True(BitConverter.ToInt32(blob, 4) == 71, "the model index at 4");
        Assert.True(BitConverter.ToInt32(blob, 8) == 200, "the density at 8");
        Assert.True(BitConverter.ToInt32(blob, 12) == -100,
            "and the current at 12, still negative — the field is a signed int and the prop is "
          + "sixteen signed bits");

        // The base-chain case, on the table whose own fields start twelve bytes
        // in. A writer that ignored BaseBytes would put the direction where the
        // origin reads.
        TempEntType stream = Registered("CTEBloodStream");

        var streamBlob = new byte[stream.BlobBytes];
        var streamWriter = new TempEntWriter(stream, streamBlob);

        Assert.True(streamWriter.Vector("Origin", new Vector(1f, 2f, 3f)), "the origin is written");
        Assert.True(streamWriter.Vector("Direction", new Vector(4f, 5f, 6f)), "and the direction");
        Assert.True(streamWriter.Int("Amount", 250), "and the amount");

        Assert.True(BitConverter.ToSingle(streamBlob, 0) == 1f, "the origin's x at byte 0");
        Assert.True(BitConverter.ToSingle(streamBlob, 12) == 4f,
            "and the direction's x at 12, after the base's three floats");
        Assert.True(BitConverter.ToInt32(streamBlob, 28) == 250, "the amount at 28");

        // The bool, whose one byte is the thing a four-byte store would trample.
        TempEntType splash = Registered("CTEEnergySplash");

        var splashBlob = new byte[splash.BlobBytes];
        var splashWriter = new TempEntWriter(splash, splashBlob);

        Assert.True(splashWriter.Bool("Explosive", true), "the bool is written");
        Assert.True(splashBlob[24] == 1, "as a 1 in the single byte at 24");
        Assert.True(splashBlob[25] == 0 && splashBlob[26] == 0 && splashBlob[27] == 0,
            "and the three bytes after it are untouched, which a four-byte store would not "
          + "have left true");
    }

    [Fact]
    public void EveryGoreDeclarationIsAnEntityLayoutToo()
    {
        var layouts = new EntityLayoutRegistry(new RecordingLog());
        layouts.DiscoverAll(TestServices.ServerGame);

        var names = layouts.Layouts.Select(l => l.ClassName).ToList();

        foreach (string name in new[] { "TeBloodSprite", "TeBloodStream", "TeMetalSparks",
                                        "TeEnergySplash", "TeBubbles", "TeBubbleTrail", "TeFizz" })
        {
            Assert.True(names.Contains(name), $"{name}'s blob is in the fingerprint both sides compare");
        }

        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeMetalSparks").Size == 24,
            "DT_TEMetalSparks' own layout is twenty-four bytes — two vectors");

        // THE EIGHTH IS IN THE FINGERPRINT NOW, AND THE INHERITANCE IS WHY.
        // TeArmorRicochet declares no fields of its own and used to be absent
        // for exactly that reason: DiscoverAll skips a type with no
        // [EntityField] anywhere on it, and a struct with an empty body has
        // none. As a CLASS deriving TeMetalSparks it INHERITS two, so it is
        // found, and EntityLayout.Build walks the whole chain and gives it the
        // twenty-four bytes its blob has always had.
        //
        // That is a change to the fingerprint and it is safe for the one reason
        // that matters: both sides compute it from the SAME shared assembly, so
        // they add the same layout at the same position and still agree. What
        // it is not is cosmetic — a layout in the list on one side only is the
        // divergence the fingerprint exists to catch.
        Assert.True(names.Contains("TeArmorRicochet"),
            "TeArmorRicochet IS in the fingerprint now: it declares no fields but INHERITS "
          + "TeMetalSparks', which is what makes DiscoverAll find it");
        Assert.True(layouts.Layouts.Single(l => l.ClassName == "TeArmorRicochet").Size == 24,
            "at twenty-four bytes, the base's two vectors — the same blob it always had, now "
          + "described in its own name as well as its base's");
        Assert.True(!names.Contains("TeBaseTempEntity"),
            "and TeBaseTempEntity is still absent, because it is still a struct with an empty "
          + "body and nothing derives from it in C# yet — which is what the line above looked "
          + "like before the conversion");
    }

    // ---------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------

    private static readonly string[] GoreClassNames =
    {
        "CTEBloodSprite", "CTEBloodStream", "CTEMetalSparks", "CTEArmorRicochet",
        "CTEEnergySplash", "CTEBubbles", "CTEBubbleTrail", "CTEFizz",
    };

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

    private static TempEntRegistry Registry()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry;
    }

    private static TempEntType Registered(string className)
        => Registry().Find(className)
           ?? throw new InvalidOperationException($"{className} was not discovered");

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
}

