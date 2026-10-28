using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Lane CD's client halves: the three decal temp entities whose whole
/// <c>PostDataUpdate</c> now lives in C#.
///
/// <para>
/// THESE ARE NOT THE SCHEMA TESTS. <see cref="TempEntDecalSchemaTests"/> checks
/// the six declarations against the stock <c>te_*decal.cpp</c> tables read off
/// disk, which is the only check that can catch a wrong-but-consistent width
/// (docs/te-lane-contract.md's first "thing a green dump cannot tell you").
/// What is asserted here is the OTHER half — that a blob carrying known numbers
/// reaches the drawing primitive as those numbers, through the reader, the
/// router and the player.
/// </para>
///
/// <para>
/// EVERY ONE ASSERTS THE ARGUMENT AND NOT ONLY THE CALL. A player that called
/// <c>ClientDecalShoot</c> with three zeros would satisfy "one decal was shot",
/// and a decal at the world origin is a plausible-looking value rather than an
/// obviously broken one — which is precisely the failure 2E-bis records from a
/// live join, where a spark fired at (-2932, 3218, 60) decoded to (0,0,0) and
/// the counters said "1 played, 0 dropped" throughout.
/// </para>
///
/// <para>
/// THE THREE THAT ARE NOT HERE — <c>CTEDecal</c>, <c>CTEProjectedDecal</c> and
/// <c>CTEWorldDecal</c> — are in <see cref="TempEntClientDecalCxTests"/>. This
/// lane reported them rather than stubbing them, because each wanted a
/// capability the C-Phase 2 backends did not carry:
/// <c>staticpropmgr-&gt;AddDecalToStaticProp</c> for the first two, a client
/// trace whose <c>tr.hitbox</c> survives for the second, and
/// <c>UTIL_IsLowViolence</c> plus a decal index-to-name lookup for the third.
/// Lane CX added those four slots and then ported the three. The tripwire that
/// stood here — <c>TheThreeBlockedOnABackendHaveNoManagedPlayerYet</c> — was
/// DELETED when they landed, which is what its own note asked for; ledger rows
/// C2 and C3 are still open and are now said out loud once per level by the
/// managed players. See docs/te-ledger.md.
/// </para>
/// </summary>
public class TempEntClientDecalTests
{
    /// <summary>
    /// Component-wise and EXACT. Nothing on this path encodes — the COORD
    /// rounding is the engine's and no test here reaches it — so a byte that
    /// moved is a byte that moved.
    /// </summary>
    private static bool Same(Vector a, Vector b)
        => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>
    /// A registry over the real shared declarations, native handing out a
    /// DISTINCT handle per registration as it does.
    /// </summary>
    private static TempEntRegistry Registered(RecordingEngine engine, int handle = 5)
    {
        int next = handle;

        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ClientGame, engine);

        return registry;
    }

    // =====================================================================
    // Discovery.
    // =====================================================================

    /// <summary>
    /// The three players are found by attribute, under the names the two sides
    /// pair on.
    ///
    /// <para>
    /// BY THE WIRE NAME AND NOT THE C# ONE. <c>ClientTeBspDecal</c> plays
    /// <c>"CTEBSPDecal"</c> — different capitalisation, and a fourth spelling
    /// anywhere would be a player nothing ever calls.
    /// </para>
    /// </summary>
    [Fact]
    public void TheThreeDecalPlayersAreFoundByAttributeUnderTheirWireNames()
    {
        var engine = Fake();
        var players = new ClientTempEnts(new RecordingLog(), Registered(engine));

        players.RegisterAll(typeof(ClientTeBspDecal).Assembly, Services().WithDecalsOn());

        Assert.True(players.PlayerNames.Contains("CTEBSPDecal"),
            "CTEBSPDecal has a managed player");
        Assert.True(players.PlayerNames.Contains("CTEFootprintDecal"),
            "and so does CTEFootprintDecal, whose file is c_te_footprint.cpp");
        Assert.True(players.PlayerNames.Contains("CTEPlayerDecal"),
            "and so does CTEPlayerDecal");
    }

    // =====================================================================
    // CTEBSPDecal.
    // =====================================================================

    /// <summary>
    /// A BSP decal reaches <c>ClientDecalShoot</c> with the point, the edict and
    /// the decal index the blob carried — and with stock's
    /// <c>FDECAL_PERMANENT</c>.
    /// </summary>
    [Fact]
    public void TheBspDecalReachesTheEngineWithTheNumbersTheBlobCarried()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEBSPDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-3079f, 4657f, 130f));
        writer.Int("Entity", 17);
        writer.Int("Index", 42);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEBSPDecal", new ClientTeBspDecal(engine, DecalSettings.On()));

        players.Dispatch(te.Handle, blob);

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1, "one decal was shot");

        var call = engine.Last("Effects.ClientDecalShoot")!.Value;

        Assert.True(call.ArgAs<int>(0) == 42, "with the decal index off the wire");
        Assert.True(call.ArgAs<int>(1) == 17, "onto the edict off the wire");
        Assert.True(Same(call.ArgAs<Vector>(2), new Vector(-3079f, 4657f, 130f)),
            "at the point off the wire, and not at the origin");
        Assert.True(call.Arg(3) is null,
            "with NO s axis: a BSP decal lets the engine pick one, as stock passes 0");
    }

    /// <summary>
    /// <c>FDECAL_PERMANENT</c>, which is the one thing separating a BSP decal
    /// from a world decal at the engine call.
    ///
    /// <para>
    /// ASSERTED AS ITS VALUE AND NOT AS "non-zero". The flags word goes
    /// straight into <c>effects-&gt;DecalShoot</c>, where every other bit means
    /// something else to the engine; a player that had ORed in a second bit
    /// would pass a "not zero" check and paint a different kind of decal.
    /// </para>
    /// </summary>
    [Fact]
    public void TheBspDecalIsPermanentAndTheFootprintIsNot()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType bsp = registry.Find("CTEBSPDecal")!;
        var bspBlob = new byte[bsp.BlobBytes];
        new TempEntWriter(bsp, bspBlob).Int("Index", 1);

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEBSPDecal", new ClientTeBspDecal(engine, DecalSettings.On()));
        players.Add("CTEFootprintDecal", new ClientTeFootprintDecal(engine, DecalSettings.On()));

        players.Dispatch(bsp.Handle, bspBlob);

        Assert.True(engine.Last("Effects.ClientDecalShoot")!.Value.ArgAs<int>(4) == 0x01,
            "a BSP decal is FDECAL_PERMANENT, which is what c_te_bspdecal.cpp "
            + "#defines at the top of itself and passes as the last argument");

        TempEntType foot = registry.Find("CTEFootprintDecal")!;
        var footBlob = new byte[foot.BlobBytes];
        new TempEntWriter(foot, footBlob).Int("Index", 1);

        players.Dispatch(foot.Handle, footBlob);

        Assert.True(engine.Last("Effects.ClientDecalShoot")!.Value.ArgAs<int>(4) == 0,
            "and a footprint is not: it ages out with every other decal");
    }

    // =====================================================================
    // CTEFootprintDecal.
    // =====================================================================

    /// <summary>
    /// A footprint reaches <c>ClientDecalShoot</c> with its DIRECTION as the
    /// decal's s axis — the only one of the six that passes one.
    /// </summary>
    [Fact]
    public void TheFootprintCarriesItsFacingAsTheDecalSAxis()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEFootprintDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(128f, -64f, 8f));
        writer.Vector("Direction", new Vector(0f, 1f, 0f));
        writer.Int("Entity", 3);
        writer.Int("Index", 7);
        writer.Int("MaterialType", 'C');

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEFootprintDecal", new ClientTeFootprintDecal(engine, DecalSettings.On()));

        players.Dispatch(te.Handle, blob);

        var call = engine.Last("Effects.ClientDecalShoot")!.Value;

        Assert.True(call.ArgAs<int>(0) == 7, "the print's decal index");
        Assert.True(call.ArgAs<int>(1) == 3, "the edict stepped on");
        Assert.True(Same(call.ArgAs<Vector>(2), new Vector(128f, -64f, 8f)),
            "where the foot landed");
        Assert.True(call.Arg(3) is not null,
            "and an s axis is supplied rather than left to the engine");
        Assert.True(Same(call.ArgAs<Vector>(3), new Vector(0f, 1f, 0f)),
            "which is the direction off the wire -- swapping it with the origin "
            + "would point every print at the world origin");
    }

    /// <summary>
    /// <c>m_chMaterialType</c> is ONE BYTE of blob and is decoded but unused —
    /// stock's own state, kept deliberately.
    ///
    /// <para>
    /// The width is the thing that matters: <c>RecvPropInt</c> picks its write
    /// proxy from the declared storage width (<c>dt_recv.cpp:202</c>), so a byte
    /// field described as four writes three bytes of whatever follows. This
    /// asserts the declaration, and that a footprint whose material byte differs
    /// still draws identically — which is what "decoded and not used" means and
    /// is the claim a later reader will want to check before finishing Valve's
    /// FIXME.
    /// </para>
    /// </summary>
    [Fact]
    public void TheFootprintMaterialByteIsOneByteAndChangesNothingYet()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEFootprintDecal")!;
        TeField material = te.Fields["MaterialType"];

        Assert.True(material.SizeBytes == 1,
            "m_chMaterialType is one byte of blob, as stock's unsigned char is");
        Assert.True(material.Bits == 8, "and eight bits on the wire");

        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEFootprintDecal", new ClientTeFootprintDecal(engine, DecalSettings.On()));

        foreach (char kind in new[] { 'C', 'M', 'W' })
        {
            var blob = new byte[te.BlobBytes];
            var writer = new TempEntWriter(te, blob);
            writer.Vector("Origin", new Vector(1f, 2f, 3f));
            writer.Int("Index", 9);
            writer.Int("MaterialType", kind);

            players.Dispatch(te.Handle, blob);

            var call = engine.Last("Effects.ClientDecalShoot")!.Value;

            Assert.True(call.ArgAs<int>(0) == 9,
                $"material '{kind}' does not change which decal is shot -- stock's "
                + "own FIXME says it should one day and today it does not");
        }
    }

    // =====================================================================
    // CTEPlayerDecal.
    // =====================================================================

    /// <summary>
    /// A spray reaches <c>ClientPlayerDecalShoot</c> with the point, the player
    /// SLOT and the edict.
    ///
    /// <para>
    /// THE PLAYER AND THE ENTITY ARE DIFFERENT NUMBERS AND ARE ASSERTED AS
    /// SUCH. They are both small unsigned indices of different widths —
    /// <c>Q_log2( MAX_PLAYERS )</c> and <c>MAX_EDICT_BITS</c> — and stock's C++
    /// class declares them in a different order from its own send table, so a
    /// port that followed the members rather than the table would have swapped
    /// exactly these two. Distinct values here are what catches that.
    /// </para>
    /// </summary>
    [Fact]
    public void ThePlayerDecalCarriesTheSlotAndTheEdictWithoutSwappingThem()
    {
        var engine = Fake();
        var registry = Registered(engine);

        TempEntType te = registry.Find("CTEPlayerDecal")!;
        var blob = new byte[te.BlobBytes];
        var writer = new TempEntWriter(te, blob);

        writer.Vector("Origin", new Vector(-16f, 512f, 72f));
        writer.Int("Entity", 21);
        writer.Int("Player", 4);

        var sprays = new RecordingSprays();
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEPlayerDecal", new ClientTePlayerDecal(sprays));

        players.Dispatch(te.Handle, blob);

        Assert.True(sprays.Calls.Count == 1, "one spray was asked for");

        var (position, player, entity) = sprays.Calls[0];

        Assert.True(Same(position, new Vector(-16f, 512f, 72f)), "where the spray landed");
        Assert.True(player == 4, "whose logo -- the player SLOT");
        Assert.True(entity == 21, "and the edict sprayed on, not the slot again");
    }

    /// <summary>
    /// A spray does NOT go through <c>ClientDecalShoot</c>, and a BSP decal does
    /// not go through <c>ClientPlayerDecalShoot</c>.
    ///
    /// <para>
    /// The two backends take almost the same arguments and do entirely
    /// different things — one paints a decal index, the other a material
    /// resolved from a player's downloaded logo. Each of the tests above would
    /// pass with the calls crossed, because each only looks at the backend it
    /// expects.
    /// </para>
    /// </summary>
    [Fact]
    public void EachDecalGoesThroughItsOwnBackendAndNotTheOther()
    {
        var engine = Fake();
        var registry = Registered(engine);

        var sprays = new RecordingSprays();
        var players = new ClientTempEnts(new RecordingLog(), registry);
        players.Add("CTEPlayerDecal", new ClientTePlayerDecal(sprays));
        players.Add("CTEBSPDecal", new ClientTeBspDecal(engine, DecalSettings.On()));

        TempEntType spray = registry.Find("CTEPlayerDecal")!;
        players.Dispatch(spray.Handle, new byte[spray.BlobBytes]);

        Assert.True(sprays.Calls.Count == 1, "the spray went to the player sprays");
        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 0,
            "and not to DecalShoot, which would paint a decal index instead of a logo");

        TempEntType bsp = registry.Find("CTEBSPDecal")!;
        players.Dispatch(bsp.Handle, new byte[bsp.BlobBytes]);

        Assert.True(engine.CountOf("Effects.ClientDecalShoot") == 1,
            "the BSP decal went to DecalShoot");
        Assert.True(sprays.Calls.Count == 1, "and did not also reach the player sprays");
    }


    // =====================================================================
    // The NATIVE forwards, read off disk.
    // =====================================================================

    // =====================================================================
    // RETIRED BY plan_shim.md PHASE 1f: EveryFieldTheNativeForwardsWriteIsOneTheDeclarationDeclares
    //
    // WHAT IT CLAIMED. shell_te_fwd_cd.cpp wrote this lane's three decal blobs
    // by string and this fact checked every one of those strings against the
    // declaration.
    //
    // WHY IT IS NO LONGER A CLAIM ABOUT THIS BUILD. There is no client-side
    // native forward for these classes any more. All 52 client TE_* entry
    // points were reached only through c_te.cpp's C_TempEntsSystem vtable, and
    // the surviving `te->` call sites on the client link line are three —
    // te->Bubbles and te->Smoke in cdll_util.cpp and te->DispatchEffect in
    // c_te_effect_dispatch.cpp. c_te.cpp is out of client_csharp.vpc and the
    // nine shell_te_fwd_c*.cpp are deleted; what replaced them,
    // shell_te_forwards_client.cpp, carries TE_Bubbles and TE_Smoke and nothing
    // else. A fact that reads a file that is not there does not fail — it takes
    // its not-found arm and passes forever (SourceFileNamesExistTests exists
    // because of exactly that), so retiring it beats repointing it at a file
    // that cannot contain what it looks for.
    //
    // WHAT STILL CHECKS THE SURVIVORS. TempEntShimKeyTests and
    // TempEntDeclarationWriterTests sweep BOTH sides' forwarder files for the
    // same field-name agreement, and TeForwardSignatureTests pins each
    // surviving forward's packing against the arm that unpacks it. Nothing
    // about the managed declarations, the players or the send path changed, and
    // the facts in this file that test those are untouched.
    // =====================================================================

    // =====================================================================
    // RETIRED BY plan_shim.md PHASE 1f: TheForwardsAndThePlayersAreTheSameThreeClasses
    //
    // WHAT IT CLAIMED. shell_te_fwd_cd.cpp forwarded exactly the three classes
    // this lane declares players for.
    //
    // WHY IT IS NO LONGER A CLAIM ABOUT THIS BUILD. There is no client-side
    // native forward for these classes any more. All 52 client TE_* entry
    // points were reached only through c_te.cpp's C_TempEntsSystem vtable, and
    // the surviving `te->` call sites on the client link line are three —
    // te->Bubbles and te->Smoke in cdll_util.cpp and te->DispatchEffect in
    // c_te_effect_dispatch.cpp. c_te.cpp is out of client_csharp.vpc and the
    // nine shell_te_fwd_c*.cpp are deleted; what replaced them,
    // shell_te_forwards_client.cpp, carries TE_Bubbles and TE_Smoke and nothing
    // else. A fact that reads a file that is not there does not fail — it takes
    // its not-found arm and passes forever (SourceFileNamesExistTests exists
    // because of exactly that), so retiring it beats repointing it at a file
    // that cannot contain what it looks for.
    //
    // WHAT STILL CHECKS THE SURVIVORS. TempEntShimKeyTests and
    // TempEntDeclarationWriterTests sweep BOTH sides' forwarder files for the
    // same field-name agreement, and TeForwardSignatureTests pins each
    // surviving forward's packing against the arm that unpacks it. Nothing
    // about the managed declarations, the players or the send path changed, and
    // the facts in this file that test those are untouched.
    // =====================================================================


    // =====================================================================
    // The base the whole lane turned on.
    // =====================================================================

    /// <summary>
    /// All three declarations nest <c>DT_BaseTempEntity</c>, and it is EMPTY.
    ///
    /// <para>
    /// THIS IS THE SHAPE THE NESTING QUESTION WAS ABOUT. The client resolves a
    /// nested table by name and cannot hold two of one (2E-bis), so a table
    /// moves only when every native user of ITS OWN name is evicted — but a
    /// base is a different question, and the answer is that the managed
    /// concrete table's <c>"baseclass"</c> prop may point at the RecvTable stock
    /// still owns. There is one <c>DT_BaseTempEntity</c> in the process either
    /// way; porting these three gave it three more parents.
    /// </para>
    ///
    /// <para>
    /// The base being EMPTY is asserted because it is what makes the
    /// <c>"baseclass"</c> prop's offset-zero exemption safe here: a base with no
    /// props collapses onto the object base and writes nothing at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheThreePortedDecalsAllNestTheEmptyBaseNativeStillOwns()
    {
        var engine = Fake();
        var registry = Registered(engine);

        foreach (string name in new[] { "CTEBSPDecal", "CTEFootprintDecal", "CTEPlayerDecal" })
        {
            TempEntType te = registry.Find(name)!;

            Assert.True(te.Class.BaseTable == "DT_BaseTempEntity",
                $"{name} nests DT_BaseTempEntity, which twenty other native classes "
                + "still nest");
        }

        TeClass baseTe = registry.Find("CBaseTempEntity")!.Class;

        Assert.True(baseTe.TableName == "DT_BaseTempEntity", "the base table's name");
        Assert.True(baseTe.Fields.Length == 0,
            "and it is EMPTY, as BEGIN_RECV_TABLE_NOBASE(C_BaseTempEntity, "
            + "DT_BaseTempEntity) with no props is");
        Assert.False(baseTe.RegistersClass,
            "and it claims no ClientClass of its own -- it is a table, not an effect");
    }

    /// <summary>The player sprays a spray temp entity asks, recorded.</summary>
    private sealed class RecordingSprays : IPlayerSprays
    {
        public readonly List<(Vector Position, int Player, int Entity)> Calls = [];

        public SprayOutcome Spray(Vector position, int player, int entity)
        {
            Calls.Add((position, player, entity));
            return SprayOutcome.Sprayed;
        }
    }
}
