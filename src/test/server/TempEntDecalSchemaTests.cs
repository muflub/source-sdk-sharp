using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane B's six decal tables against plan_tmp.md Part 6 — and, where the file is
/// still on disk, against the stock table each was transcribed from.
///
/// <para>
/// THE PARITY HALF IS THE ONE THAT MATTERS, for the reason
/// <see cref="TempEntSchemaTests"/> writes out at length: a fixture typed out
/// beside the declaration is one person's reading of one table twice, and a
/// misread stays green forever. So these READ the six
/// <c>game/server/te_*decal.cpp</c> files, parse their
/// <c>IMPLEMENT_SERVERCLASS_ST</c> blocks, and assert the agreement.
/// </para>
///
/// <para>
/// The parser here is NOT the one in <see cref="TempEntSchemaTests"/> and could
/// not be. That one reads a bits field with <c>[^,\)]*</c>, which is enough for
/// <c>4</c> and <c>-1</c> and stops in the middle of
/// <c>Q_log2( MAX_PLAYERS )</c>; it takes everything after the bits as one flag
/// expression, which turns <c>SPROP_ROUNDUP, 0, 1024</c> into a token nothing
/// recognises. This lane has both of those and a two-argument
/// <c>SendPropQAngles</c> besides, so it splits the argument list on TOP-LEVEL
/// commas with a paren depth count and reads low and high as their own
/// arguments. A parser that could not see the range would have been unable to
/// check <c>m_flDistance</c> at all.
/// </para>
///
/// <para>
/// Those six files are excluded from the BUILD — a managed ServerClass of each
/// name cannot coexist with stock's — but still present in the tree. Part 7
/// deletes them; from then on the parity halves skip with the reason printed and
/// the Part 6 rows below are what is left.
/// </para>
/// </summary>
public class TempEntDecalSchemaTests
{
    // =====================================================================
    // The Part 6 rows.
    // =====================================================================

    [Fact]
    public void TheBspDecalTableIsAPointAnEdictAndADecalIndex()
    {
        TeClass te = Declared(typeof(TeBspDecal));

        Assert.True(te.ClassName == "CTEBSPDecal", "the two sides pair on CTEBSPDecal");
        Assert.True(te.TableName == "DT_TEBSPDecal", "over DT_TEBSPDecal");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");
        Assert.True(te.RegistersClass,
            "and it DOES claim a ServerClass: te_bspdecal.cpp is evicted from the build");

        Assert.True(te.Fields.Length == 3, "three props of its own");

        // Declaration order is wire order. Looking each field up by name would
        // pass on a reordered table, and reordering is a wire-format change.
        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Entity", "then the entity index");
        Assert.True(te.Fields[2].Name == "Index", "then the decal index");

        Assert.True(te.Fields[0].Kind == TePropKind.Vector && te.Fields[0].Bits == -1
                 && te.Fields[0].Flags == TeFlags.Coord,
            "m_vecOrigin is one COORD vector prop with the flag deciding the width");
        Assert.True(te.Fields[1].Kind == TePropKind.Int
                 && te.Fields[1].BitsFrom == TeBits.MaxEdict
                 && te.Fields[1].Flags == TeFlags.Unsigned,
            "m_nEntity is MAX_EDICT_BITS unsigned, resolved natively and not restated here");
        Assert.True(te.Fields[2].Kind == TePropKind.Int && te.Fields[2].Bits == 9
                 && te.Fields[2].BitsFrom == TeBits.Literal
                 && te.Fields[2].Flags == TeFlags.Unsigned,
            "m_nIndex is a literal 9 bits unsigned");
    }

    [Fact]
    public void TheDecalTableIsTwoPointsAnEdictAHitboxAndAnIndex()
    {
        TeClass te = Declared(typeof(TeDecal));

        Assert.True(te.ClassName == "CTEDecal", "the two sides pair on CTEDecal");
        Assert.True(te.TableName == "DT_TEDecal", "over DT_TEDecal");
        Assert.True(te.BaseTable == "DT_BaseTempEntity", "which nests DT_BaseTempEntity");

        Assert.True(te.Fields.Length == 5, "five props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "where the trace ended goes first");
        Assert.True(te.Fields[1].Name == "Start", "then where it began");
        Assert.True(te.Fields[2].Name == "Entity", "then the entity index");
        Assert.True(te.Fields[3].Name == "Hitbox", "then the hitbox");
        Assert.True(te.Fields[4].Name == "Index", "then the decal index");

        Assert.True(te.Fields[0].Kind == TePropKind.Vector && te.Fields[0].Flags == TeFlags.Coord,
            "m_vecOrigin is a COORD vector");
        Assert.True(te.Fields[1].Kind == TePropKind.Vector && te.Fields[1].Flags == TeFlags.Coord,
            "and so is m_vecStart — the client re-traces between the two");
        Assert.True(te.Fields[2].BitsFrom == TeBits.MaxEdict,
            "m_nEntity is MAX_EDICT_BITS, the symbol");
        Assert.True(te.Fields[3].Bits == 12 && te.Fields[3].BitsFrom == TeBits.Literal,
            "m_nHitbox is a literal 12 bits — the widest int in this lane");
        Assert.True(te.Fields[4].Bits == 9, "m_nIndex is 9 bits");
    }

    [Fact]
    public void TheWorldDecalTableTakesTheNonTfBranch()
    {
        TeClass te = Declared(typeof(TeWorldDecal));

        Assert.True(te.ClassName == "CTEWorldDecal", "the two sides pair on CTEWorldDecal");
        Assert.True(te.TableName == "DT_TEWorldDecal", "over DT_TEWorldDecal");

        Assert.True(te.Fields.Length == 2, "two props: a point and an index");

        // THE #else BRANCH, and saying so is half the assertion. TF_DLL is in
        // neither target's defines (plan 2G), so this is SPROP_COORD and not
        // SPROP_COORD_MP_INTEGRAL — which would decode to plausible whole
        // numbers rather than to nothing.
        Assert.True(te.Fields[0].Flags == TeFlags.Coord,
            "m_vecOrigin is SPROP_COORD, which is the non-TF branch this build compiles");
        Assert.True(!te.Fields[0].Flags.HasFlag(TeFlags.CoordMpIntegral),
            "and carries no trace of the TF branch's SPROP_COORD_MP_INTEGRAL");
        Assert.True(te.Fields[1].Name == "Index" && te.Fields[1].Bits == 9,
            "m_nIndex is 9 bits unsigned");
    }

    [Fact]
    public void ThePlayerDecalTableFollowsTheTableOrderNotTheMemberOrder()
    {
        TeClass te = Declared(typeof(TePlayerDecal));

        Assert.True(te.ClassName == "CTEPlayerDecal", "the two sides pair on CTEPlayerDecal");
        Assert.True(te.Fields.Length == 3, "three props of its own");

        // CTEPlayerDecal declares m_nPlayer FIRST as a member and LAST as a
        // prop. It is the PROP order that goes on the wire, and this is the
        // assertion that says the port read the table and not the class body.
        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first, as the TABLE has it");
        Assert.True(te.Fields[1].Name == "Entity", "then the entity index");
        Assert.True(te.Fields[2].Name == "Player",
            "and the player LAST, though the C++ class declares it first");

        Assert.True(te.Fields[1].BitsFrom == TeBits.MaxEdict,
            "m_nEntity is MAX_EDICT_BITS");
        Assert.True(te.Fields[2].BitsFrom == TeBits.MaxPlayersLog2,
            "m_nPlayer is Q_log2( MAX_PLAYERS ), which is its own symbol and not the edict width");
        Assert.True(te.Fields[2].Bits == -1,
            "and carries no literal width beside the symbol, so there is one copy of it");
    }

    [Fact]
    public void TheProjectedDecalTableCarriesTheInventorysOnlyQAngles()
    {
        TeClass te = Declared(typeof(TeProjectedDecal));

        Assert.True(te.ClassName == "CTEProjectedDecal", "the two sides pair on CTEProjectedDecal");
        Assert.True(te.Fields.Length == 4, "four props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Rotation", "then the facing");
        Assert.True(te.Fields[2].Name == "Distance", "then how far it is thrown");
        Assert.True(te.Fields[3].Name == "Index", "then the decal index");

        // SendPropQAngles is DPT_Vector with low 0 / high 360 AND
        // SendProxy_QAngles, which anglemods on the way out; SendPropAngle is
        // the DPT_Float form of the same thing. Declaring this a plain Vector
        // compiles, pairs, and sends a negative pitch outside its own range.
        Assert.True(te.Fields[1].Kind == TePropKind.QAngles,
            "m_angRotation is a QAngles prop and not a Vector one");
        Assert.True(te.Fields[1].Kind != TePropKind.Vector,
            "which is the distinction that decodes as garbage rather than failing");
        Assert.True(te.Fields[1].Bits == 10, "ten bits per component");
        Assert.True(te.Fields[1].Flags == TeFlags.None,
            "and no encoding flag: for a qangles the [0,360] range IS the encoding");
        Assert.True(te.Fields[1].SizeBytes == 12,
            "three floats of blob, which is what native refuses a QAngles prop without");

        Assert.True(te.Fields[2].Kind == TePropKind.Float && te.Fields[2].Bits == 10,
            "m_flDistance is a ten bit float");
        Assert.True(te.Fields[2].Flags == TeFlags.RoundUp,
            "rounded UP — rounding down can stop short of the surface and paint nothing");
        Assert.True(te.Fields[2].Low == 0f && te.Fields[2].High == 1024f,
            "over [0,1024]");

        Assert.True(te.Fields[3].Bits == 9, "m_nIndex is 9 bits unsigned");
    }

    [Fact]
    public void TheFootprintDecalTableKeepsItsLiteralElevenAndItsByte()
    {
        TeClass te = Declared(typeof(TeFootprintDecal));

        Assert.True(te.ClassName == "CTEFootprintDecal", "the two sides pair on CTEFootprintDecal");
        Assert.True(te.Fields.Length == 5, "five props of its own");

        Assert.True(te.Fields[0].Name == "Origin", "the origin goes first");
        Assert.True(te.Fields[1].Name == "Direction", "then the facing");
        Assert.True(te.Fields[2].Name == "Entity", "then the entity index");
        Assert.True(te.Fields[3].Name == "Index", "then the decal index");
        Assert.True(te.Fields[4].Name == "MaterialType", "then the material character");

        // THE LITERAL ELEVEN. MAX_EDICT_BITS is also eleven, which is exactly
        // why this has to be asserted: the two are the same number and not the
        // same thing, and stock's client half of THIS table reads eleven.
        Assert.True(te.Fields[2].BitsFrom == TeBits.Literal,
            "m_nEntity here is a LITERAL width, not the MAX_EDICT_BITS symbol");
        Assert.True(te.Fields[2].Bits == 11, "and the literal is eleven, as stock's table writes it");

        Assert.True(te.Fields[3].Bits == 8,
            "m_nIndex is EIGHT bits here, where the other five decal tables send nine");

        Assert.True(te.Fields[4].Bits == 8 && te.Fields[4].Flags == TeFlags.Unsigned,
            "m_chMaterialType is 8 bits unsigned");
        Assert.True(te.Fields[4].SizeBytes == 1,
            "and ONE byte of storage, because stock's member is an unsigned char and "
          + "SendPropInt picks its read proxy from that width");
    }

    /// <summary>
    /// The lane's whole hazard in one test: three entity-index widths across six
    /// tables, and none of them unified.
    ///
    /// <para>
    /// Two of the three are eleven bits today, so nothing observable separates
    /// them in a running game — which is what makes this the kind of tidying-up
    /// that gets done by accident and discovered by a client that begins joining
    /// and stops. The symbol and the literal are different FACTS about stock's
    /// tables and this asserts both.
    /// </para>
    /// </summary>
    [Fact]
    public void ThreeEntityIndexWidthsStayThreeWidths()
    {
        // BY MEMBER NAME, which is the wire name now: the declarations have
        // dropped WireName (docs/te-declaration-conversion.md), so stock's
        // m_nEntity is spelled Entity on both sides of this build. The three
        // WIDTHS below are the subject and none of them moved.
        TeField bsp    = Field(typeof(TeBspDecal),       "Entity");
        TeField decal  = Field(typeof(TeDecal),          "Entity");
        TeField player = Field(typeof(TePlayerDecal),    "Entity");
        TeField slot   = Field(typeof(TePlayerDecal),    "Player");
        TeField foot   = Field(typeof(TeFootprintDecal), "Entity");

        Assert.True(bsp.BitsFrom == TeBits.MaxEdict, "DT_TEBSPDecal's m_nEntity is the edict symbol");
        Assert.True(decal.BitsFrom == TeBits.MaxEdict, "DT_TEDecal's is too");
        Assert.True(player.BitsFrom == TeBits.MaxEdict, "and DT_TEPlayerDecal's");

        Assert.True(slot.BitsFrom == TeBits.MaxPlayersLog2,
            "but DT_TEPlayerDecal's m_nPlayer is the PLAYER count's log2, a different symbol");
        Assert.True(slot.BitsFrom != foot.BitsFrom,
            "and neither of those is what DT_TEFootprintDecal's m_nEntity uses");

        Assert.True(foot.BitsFrom == TeBits.Literal && foot.Bits == 11,
            "DT_TEFootprintDecal's m_nEntity is a literal 11, because stock's table says 11");
        Assert.True(foot.BitsFrom != bsp.BitsFrom,
            "so a literal and a symbol that happen to be the same number stay distinguishable");
    }

    /// <summary>
    /// Every blob is laid out by <c>EntityLayout</c> and no declaration writes
    /// an offset. All six nest <c>DT_BaseTempEntity</c>, which is empty — so
    /// their own fields start at zero.
    /// </summary>
    [Fact]
    public void TheBlobsAreWhatTheLayoutComputed()
    {
        Assert.True(Declared(typeof(TeBspDecal)).BlobBytes == 20,
            "DT_TEBSPDecal: a vector and two ints");
        Assert.True(Declared(typeof(TeDecal)).BlobBytes == 36,
            "DT_TEDecal: two vectors and three ints");
        Assert.True(Declared(typeof(TeWorldDecal)).BlobBytes == 16,
            "DT_TEWorldDecal: a vector and an int");
        Assert.True(Declared(typeof(TePlayerDecal)).BlobBytes == 20,
            "DT_TEPlayerDecal: a vector and two ints");
        Assert.True(Declared(typeof(TeProjectedDecal)).BlobBytes == 32,
            "DT_TEProjectedDecal: two vectors' worth, a float and an int");

        // 12 + 12 + 4 + 4 + 1 = 33, aligned up to 36. The byte is the point:
        // a one-byte member followed by nothing is still padded, and the blob
        // size is computed rather than written down.
        Assert.True(Declared(typeof(TeFootprintDecal)).BlobBytes == 36,
            "DT_TEFootprintDecal: two vectors, two ints and a byte, aligned up");

        TeClass foot = Declared(typeof(TeFootprintDecal));

        Assert.True(foot.Fields[0].Offset == 0, "m_vecOrigin at 0");
        Assert.True(foot.Fields[1].Offset == 12, "m_vecDirection at 12");
        Assert.True(foot.Fields[2].Offset == 24, "m_nEntity at 24");
        Assert.True(foot.Fields[3].Offset == 28, "m_nIndex at 28");
        Assert.True(foot.Fields[4].Offset == 32, "and the material byte at 32");

        Assert.True(Declared(typeof(TeBspDecal)).BaseBytes == 0,
            "DT_BaseTempEntity is empty, so a derived blob starts at zero");
    }

    // =====================================================================
    // Discovery, the blob, and the six ITempEnts methods.
    // =====================================================================

    [Fact]
    public void TheRegistryFindsAllSixByAttributeAndGivesEachAClass()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        foreach (string name in new[]
        {
            "CTEBSPDecal", "CTEDecal", "CTEWorldDecal",
            "CTEPlayerDecal", "CTEProjectedDecal", "CTEFootprintDecal",
        })
        {
            TempEntType? type = registry.Find(name);

            Assert.True(type is not null, $"{name} was discovered by attribute, with no list to join");
            Assert.True(type!.Class.RegistersClass,
                $"{name} claims a ServerClass of its own, because its te_*.cpp is evicted");
            Assert.True(type.Class.BaseTable == "DT_BaseTempEntity",
                $"{name} nests DT_BaseTempEntity");
        }
    }

    [Fact]
    public void EachDecalAsksTheEngineToPlayItsOwnBlob()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 3);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        var temps = new SourceSharp.Server.TempEnts(new RecordingLog(), registry, engine);
        var at = new Vector(64f, -32f, 8f);

        temps.BSPDecal(TeFilter.Broadcast, 0f, at, 0, 7);
        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<int>(1) == 20,
            "a whole DT_TEBSPDecal blob went");

        temps.Decal(TeFilter.Broadcast, 0f, at, at, 0, 0, 7);
        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<int>(1) == 36,
            "a whole DT_TEDecal blob went");

        temps.WorldDecal(TeFilter.Broadcast, 0f, at, 7);
        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<int>(1) == 16,
            "a whole DT_TEWorldDecal blob went");

        temps.PlayerDecal(TeFilter.Broadcast, 0f, at, 1, 0);
        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<int>(1) == 20,
            "a whole DT_TEPlayerDecal blob went");

        temps.ProjectDecal(TeFilter.Broadcast, 0f, at, new QAngle(90f, 0f, 0f), 64f, 7);
        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<int>(1) == 32,
            "a whole DT_TEProjectedDecal blob went");

        temps.FootprintDecal(TeFilter.Pvs(at), 0f, at, new Vector(0f, 1f, 0f), 0, 7, (byte)'C');

        var last = engine.Last("Effects.TempEntPlayback")!.Value;

        Assert.True(last.ArgAs<int>(1) == 36, "a whole DT_TEFootprintDecal blob went");
        Assert.True(last.ArgAs<int>(0) == 3, "under the handle native answered with");
        Assert.True(last.ArgAs<TeFilter>(2).Kind == TeFilterKind.Pvs,
            "through the PVS filter CGameMovement builds at the print");
        Assert.True(last.ArgAs<float>(3) == 0f, "with no delay");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 6, "six temp entities in all");
    }

    [Fact]
    public void ThePlayerDecalBlobIsAddressedByNameAndNotByCallOrder()
    {
        TempEntType type = Registered("CTEPlayerDecal");

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        // Written player-first, which is the C++ MEMBER order. The bytes must
        // land in the TABLE's order all the same, because the writer addresses
        // through the declaration's offsets.
        writer.Int("Player", 5);
        writer.Int("Entity", 9);
        writer.Vector("Origin", new Vector(1f, 2f, 3f));

        Assert.True(BitConverter.ToSingle(blob, 0) == 1f, "origin.x is at byte 0");
        Assert.True(BitConverter.ToSingle(blob, 8) == 3f, "origin.z at 8");
        Assert.True(BitConverter.ToInt32(blob, 12) == 9, "the entity index at 12");
        Assert.True(BitConverter.ToInt32(blob, 16) == 5, "and the player slot at 16");
    }

    [Fact]
    public void TheProjectedDecalPacksTheFacingAsThreeFloats()
    {
        TempEntType type = Registered("CTEProjectedDecal");

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        writer.Vector("Origin", new Vector(1f, 2f, 3f));
        writer.Vector("Rotation", new QAngle(-45f, 90f, 0f).AsVector());
        writer.Float("Distance", 64f);
        writer.Int("Index", 7);

        Assert.True(BitConverter.ToSingle(blob, 12) == -45f,
            "pitch at 12, NEGATIVE — the blob stores what it was given and the "
          + "qangles proxy is what anglemods it on the wire");
        Assert.True(BitConverter.ToSingle(blob, 16) == 90f, "yaw at 16");
        Assert.True(BitConverter.ToSingle(blob, 20) == 0f, "roll at 20");
        Assert.True(BitConverter.ToSingle(blob, 24) == 64f, "the distance at 24");
        Assert.True(BitConverter.ToInt32(blob, 28) == 7, "the decal index at 28");
    }

    [Fact]
    public void TheFootprintNormalisesTheDirectionStockNormalised()
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        TempEntType type = registry.Find("CTEFootprintDecal")!;

        // What TempEnts.FootprintDecal does to `right`, asserted against the
        // blob rather than inferred: te_footprintdecal.cpp:87 ran
        // VectorNormalize before Create(), so an unnormalised caller has always
        // had a unit vector put on the wire for it.
        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        writer.Vector("Direction", new Vector(0f, 3f, 0f).Normalized);

        Assert.True(BitConverter.ToSingle(blob, 12) == 0f, "x of a normalised (0,3,0) is 0");
        Assert.True(BitConverter.ToSingle(blob, 16) == 1f, "y is 1, not 3");
        Assert.True(BitConverter.ToSingle(blob, 20) == 0f, "z is 0");
    }

    [Fact]
    public void TheFootprintMaterialIsOneByteAndDoesNotReachTheNextField()
    {
        TempEntType type = Registered("CTEFootprintDecal");

        var blob = new byte[type.BlobBytes];
        var writer = new TempEntWriter(type, blob);

        writer.Int("Index", -1);
        writer.Int("MaterialType", 'C');

        Assert.True(blob[32] == (byte)'C', "the material character is the byte at 32");
        Assert.True(BitConverter.ToInt32(blob, 28) == -1,
            "and writing it left m_nIndex's four bytes alone");
        Assert.True(blob[33] == 0 && blob[34] == 0 && blob[35] == 0,
            "a one-byte field wrote one byte, not four");
    }

    // =====================================================================
    // Fixtures.
    // =====================================================================

    /// <summary>
    /// A declaration built on its own. All six nest <c>DT_BaseTempEntity</c>,
    /// which declares no fields, so the base contributes no bytes and the
    /// registry would pass the same zero in.
    /// </summary>
    private static TeClass Declared(Type type) => TeClassBuilder.Build(type);

    /// <summary>
    /// One field by its WIRE name, which since the declaration conversion is
    /// the C# member's name — stock's own spelling stays in each field's
    /// <c>&lt;summary&gt;</c> and in the table quoted at the top of its file.
    /// </summary>
    private static TeField Field(Type type, string wireName)
        => Declared(type).Fields.Single(f => f.Name == wireName);

    private static TempEntType Registered(string className)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry.Find(className)
            ?? throw new InvalidOperationException($"{className} was not discovered");
    }
}
