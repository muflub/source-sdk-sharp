using SourceSharp.Abi;
using SourceSharp.MapGen;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// The coverage gate for temp entities: plan 9.2.
///
/// <para>
/// <see cref="SandboxTests"/> fails the build when a <c>[ScriptedEntity]</c>
/// classname has no example in the map. It could never have asked the same
/// question about a temp entity, because a temp entity has no classname and no
/// factory — it is a message. So the port landed forty-three of them and nothing
/// in the build could demonstrate that any of them still fires. These are the
/// same rule for the <c>[TempEntity]</c> declarations, and the difference they
/// make is the difference between "forty-three temp entities were ported" and
/// "forty-three temp entities are demonstrably still firing".
/// </para>
///
/// <para>
/// THERE IS NO EXEMPTION LIST. <c>SandboxPass.Exemptions</c> excuses entities a
/// mapper cannot place or that vbsp eats; neither can apply here, because firing
/// IS the whole of what a temp entity does. A temp entity that cannot be fired
/// from a station has already failed the port, so an absence has nothing to be
/// excused by and the failure names it.
/// </para>
///
/// <para>
/// The one declaration this does not ask about is derived rather than excused:
/// <c>RegistersClass = false</c> means the declaration is a TABLE for others to
/// nest and claims no ServerClass, so it has no handle and native has nothing to
/// play — <c>CBaseTempEntity</c> (zero-byte blob,
/// <c>Engine.TempEntPlayback</c> refuses it) and <c>CEffectData</c> (a nested
/// payload, not a temp entity).
/// </para>
///
/// <para>
/// THIS PARAGRAPH USED TO END "when <c>basetempentity.cpp</c> is evicted and the
/// flag flips, this rule starts asking for a tester for it, which is right".
/// Phase 7 step 7b evicted the last of it — <c>c_basetempentity.cpp</c> is out
/// of <c>client_csharp.vpc</c>, and <c>game/shared/basetempentity.cpp</c> had
/// been out of <c>server_csharp.vpc</c> for several phases — and the flag did
/// NOT flip, deliberately. <see cref="Shared.TeBaseTempEntity"/> carries the
/// argument: with neither side registering the name, flipping it would register
/// a ServerClass and a ClientClass for a table with no props, and getting one of
/// those two halves wrong is a join that stops with nothing in any log. So the
/// derivation below is unchanged and this class stays excused — not because
/// the eviction has not happened, but because it happened and the answer went
/// the other way. <see cref="TempEntBaseTableTests"/> pins both halves.
/// </para>
/// </summary>
public class SandboxTeTests
{
    /// <summary>
    /// Every temp entity this build can send, from the declarations themselves.
    /// </summary>
    private static IReadOnlyList<string> Sendable => SandboxPass.RegisteredTempEnts();

    /// <summary>
    /// THE GATE. A temp entity with no <c>ss_te_tester</c> placement is a red
    /// test, not a gap nobody notices — and it keeps working for whoever adds
    /// the forty-fourth.
    /// </summary>
    [Fact]
    public void EveryTempEntityTheBuildSendsIsFiredBySomeTesterInTheMap()
    {
        var map = Sandbox.Build();

        var fired = SandboxPass.DemonstratedTempEnts(SandboxPass.FromGenerator(map));

        var missing = Sendable.Where(c => !fired.Contains(c))
                              .OrderBy(c => c, StringComparer.Ordinal)
                              .ToArray();

        Assert.True(Sendable.Count > 40,
            $"the build declares temp entities to demonstrate ({Sendable.Count})");

        Assert.True(missing.Length == 0,
            "temp entities the build sends that no ss_te_tester in ss_sandbox fires: "
            + string.Join(", ", missing)
            + ". There is no exemption list — firing is the whole of what a temp entity does. "
            + "Add a row to TeDemos and the station places it.");
    }

    /// <summary>
    /// THE SAME GATE, AGAINST THE COMPILED MAP.
    ///
    /// <para>
    /// The one above scores the generator against the declarations, which is a
    /// check the generator can only fail by disagreeing with itself. Between it
    /// and the running game sits vbsp, and the <c>.bsp</c> is regenerated only by
    /// <c>make map</c> — so it lags, and every generator-side check stays green
    /// while it does. It sat 72 classnames behind once. A green
    /// <c>SandboxTeTests</c> against a stale map proves nothing about the map
    /// anybody loads.
    /// </para>
    ///
    /// <para>
    /// A tree that has never compiled the map SKIPS, visibly, with the reason
    /// printed — see <see cref="SandboxMapFactAttribute"/>. A worktree has no
    /// <c>ss_sandbox.bsp</c> on purpose and does not borrow a neighbour's.
    /// </para>
    /// </summary>
    [SandboxMapFact]
    public void TheCompiledMapFiresEveryTempEntityToo()
    {
        SandboxMapLookup lookup = SandboxArtefact.Locate(RepoLayout.SdkTree);

        Assert.True(lookup.State == SandboxMapState.Present, lookup.Explanation);

        var compiled = BspEntities.Read(lookup.Path!);
        var missing = SandboxPass.AuditTempEntCoverage(Sendable, compiled);

        int testers = compiled.Count(e =>
            string.Equals(e.ClassName, SandboxPass.TeTesterClass, StringComparison.OrdinalIgnoreCase));

        Assert.True(missing.Count == 0,
            $"MAP DEMONSTRATES FEWER TEMP ENTITIES THAN THE BUILD SENDS — {lookup.Path} holds "
            + $"{testers} ss_te_tester placement(s) and fires {Sendable.Count - missing.Count} of "
            + $"{Sendable.Count}. Missing: "
            + string.Join(", ", missing.Select(m => m.ClassName))
            + ". This map is STALE, not short; recompile it with `make map MAP=ss_sandbox`.");
    }

    /// <summary>
    /// The other direction: a tester whose <c>te</c> keyvalue names nothing.
    ///
    /// <para>
    /// It matters because such a tester is INVISIBLE to the rule above — the map
    /// would hold forty-three placements and fire forty-two effects, and the
    /// count would look right. The entity warns at spawn; this fails the build
    /// before anybody loads it.
    /// </para>
    /// </summary>
    [Fact]
    public void NoTesterInTheMapNamesATempEntityThatDoesNotExist()
    {
        var declared = SandboxPass.FromGenerator(Sandbox.Build());
        var sendable = Sendable.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var strays = SandboxPass.DemonstratedTempEnts(declared)
                                .Where(te => !sendable.Contains(te))
                                .OrderBy(c => c, StringComparer.Ordinal)
                                .ToArray();

        Assert.True(strays.Length == 0,
            "ss_te_tester placements naming a temp entity this build does not send: "
            + string.Join(", ", strays));
    }

    /// <summary>
    /// One tester per temp entity, and no more.
    ///
    /// <para>
    /// Two testers for one effect and none for its neighbour would satisfy the
    /// gate's count while demonstrating forty-two of forty-three, and the
    /// screenshot would show two of something at one spot rather than an empty
    /// one.
    /// </para>
    /// </summary>
    [Fact]
    public void EachTempEntityIsPlacedExactlyOnce()
    {
        var placements = Sandbox.Build().Entities
            .Where(e => string.Equals(e.ClassName, SandboxPass.TeTesterClass,
                                      StringComparison.OrdinalIgnoreCase))
            .Select(e => e.KeyValues.Where(kv => kv.Key == SandboxPass.TeTesterKey)
                                    .Select(kv => kv.Value)
                                    .LastOrDefault() ?? "")
            .ToList();

        var doubled = placements.GroupBy(te => te, StringComparer.OrdinalIgnoreCase)
                                .Where(g => g.Count() > 1)
                                .Select(g => $"{g.Key} x{g.Count()}")
                                .OrderBy(t => t, StringComparer.Ordinal)
                                .ToArray();

        Assert.True(placements.Count == Sendable.Count,
            $"one ss_te_tester per temp entity: {placements.Count} placed, {Sendable.Count} sendable");

        Assert.True(doubled.Length == 0,
            "temp entities with more than one ss_te_tester: " + string.Join(", ", doubled));
    }

    /// <summary>
    /// Every tester is wired to the station's reset relay, so the button fires
    /// all of them.
    ///
    /// <para>
    /// A placement nothing fires is a placement that demonstrates nothing, and
    /// it looks exactly like one that does until somebody presses the button.
    /// The generator's own rule that every wire lands on an entity that exists
    /// checks the names; this checks that the wires are there at all.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStationsResetRelayFiresEveryTester()
    {
        var map = Sandbox.Build();

        var relay = map.Entities.Single(e => e.Name == "tempents_reset");

        var testers = map.Entities
            .Where(e => string.Equals(e.ClassName, SandboxPass.TeTesterClass,
                                      StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Name)
            .ToList();

        var unwired = testers
            .Where(name => !relay.Connections.Any(c => c.Target == name && c.Input == "Fire"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.True(unwired.Length == 0,
            "ss_te_tester placements the station's reset relay does not fire: "
            + string.Join(", ", unwired));
    }

    //-------------------------------------------------------------------------
    // THE BOOK ITSELF.
    //
    // The gate above proves the MAP demonstrates every temp entity. It cannot
    // prove that a demonstration puts anything on screen, and the way that fails
    // is quiet: TempEntWriter warns on a wire name nothing declares and writes
    // nothing, so a misspelling is an effect at the world origin or no effect at
    // all, discovered by looking rather than by building. As data, every name in
    // TeDemos is a dictionary lookup a [Fact] can do with no game running.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every field name in the book is a field the writer can actually address.
    ///
    /// <para>
    /// ASKED OF THE REGISTRY, not derived from the declarations by walking the
    /// base chain here. <see cref="TempEntWriter"/> looks a name up in
    /// <see cref="TempEntType.Fields"/> and warns-and-skips when it is not
    /// there, so that dictionary IS the question — and a second traversal of the
    /// same tables would be a second reading that can agree with the book while
    /// both disagree with the writer. It did: the first version of this test
    /// built <c>m_EffectData.m_vOrigin</c> for the nested table, the book said
    /// the same, and both were wrong, because the registry flattens a nested
    /// table's props under their OWN names. The round trip below caught it.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryFieldTheBookWritesExistsInItsDeclaration()
    {
        TempEntRegistry registry = Registry(Fake());

        var wrong = new List<string>();

        foreach (TeDemo demo in TeDemos.All)
        {
            TempEntType? type = registry.Find(demo.ClassName);

            if (type is null)
            {
                wrong.Add($"{demo.ClassName} is not a declaration at all");
                continue;
            }

            foreach (TeArg arg in demo.Args)
            {
                if (!type.Fields.ContainsKey(arg.Field))
                    wrong.Add($"{demo.ClassName}.{arg.Field}");
            }
        }

        Assert.True(wrong.Count == 0,
            "TeDemos writes fields no declaration has, which TempEntWriter would warn about and "
            + "skip at runtime — an effect somewhere else, or no effect at all: "
            + string.Join(", ", wrong));
    }

    /// <summary>
    /// The book covers exactly what the build can send: no row without a
    /// declaration, and no declaration without a row.
    /// </summary>
    [Fact]
    public void TheBookAndTheDeclarationsAgree()
    {
        var book = TeDemos.All.Select(d => d.ClassName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sendable = Sendable.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unbooked = sendable.Where(c => !book.Contains(c)).OrderBy(c => c, StringComparer.Ordinal);
        var invented = book.Where(c => !sendable.Contains(c)).OrderBy(c => c, StringComparer.Ordinal);

        Assert.True(!unbooked.Any(),
            "temp entities the build sends with no TeDemos row: " + string.Join(", ", unbooked));

        Assert.True(!invented.Any(),
            "TeDemos rows for temp entities the build does not send: " + string.Join(", ", invented));
    }

    /// <summary>
    /// Short names are unique, because they are what a person types into
    /// <c>cs_te_fire</c> and what the station's entity names are built from —
    /// and <c>Site</c> throws on a duplicate entity name rather than resolving
    /// it.
    /// </summary>
    [Fact]
    public void EveryShortNameIsUnique()
    {
        var doubled = TeDemos.All.GroupBy(d => d.Slug, StringComparer.OrdinalIgnoreCase)
                                 .Where(g => g.Count() > 1)
                                 .Select(g => g.Key)
                                 .ToArray();

        Assert.True(doubled.Length == 0, "TeDemos slugs that collide: " + string.Join(", ", doubled));
    }

    /// <summary>
    /// The layout puts every demonstration inside the plot it is placed on.
    ///
    /// <para>
    /// A station's pad is 512 units square and its furniture — the pedestal, the
    /// accent plate and the sign — occupies the front of it. An effect fired
    /// outside the pad is an effect in the aisle or inside a neighbour, which on
    /// a screenshot reads as the wrong station demonstrating it.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDemonstrationFiresInsideItsOwnPlot()
    {
        var outside = new List<string>();

        for (int i = 0; i < TeDemos.All.Count; i++)
        {
            Vector at = TeDemos.Offset(i);

            // The pad's half width, and the front edge of the furniture the
            // station gets whether it asks or not (Sandbox.Furniture: the accent
            // plate reaches y = -190).
            if (at.X < -256f || at.X > 256f || at.Y < -190f || at.Y > 256f)
                outside.Add($"{TeDemos.All[i].Slug} at ({at.X:0} {at.Y:0})");
        }

        Assert.True(outside.Count == 0,
            "TeDemos.Offset puts a demonstration outside the station's pad: "
            + string.Join(", ", outside));
    }

    /// <summary>
    /// The one bug the superseded scaffolding is explicitly not to pass on:
    /// <c>cs_sv_te_fire_f muzzleflash</c> printed <c>type 0</c> while passing
    /// <c>MUZZLEFLASH_TYPE_DEFAULT</c>, which is <c>0x00000001</c>
    /// (<c>game/shared/shareddefs.h:335</c>).
    /// </summary>
    [Fact]
    public void TheMuzzleFlashDemonstrationUsesTheDefaultTypeAndNotZero()
    {
        Assert.True(TeDemos.MuzzleFlashDefault == 1,
            "MUZZLEFLASH_TYPE_DEFAULT is 0x00000001, not 0");

        TeDemo flash = TeDemos.ByClass("CTEMuzzleFlash")!;

        TeArg type = flash.Args.Single(a => a.Field == "Type");

        Assert.True(type.Number == 1,
            "the muzzle flash demonstration passes MUZZLEFLASH_TYPE_DEFAULT, which is 1");
    }

    //-------------------------------------------------------------------------
    // THE WRITER, END TO END, WITH NO GAME.
    //
    // The two tests above check the book against the declarations, which is
    // still two readings of the same table. This one packs every demonstration
    // through the real registry and the real TempEntWriter and reads each blob
    // back through TempEntReader -- the round trip this project's own note on
    // verifying a datatable insists on: a value written and CHECKED, not printed
    // and eyeballed.
    //-------------------------------------------------------------------------

    /// <summary>
    /// Every demonstration packs a whole blob and lands where it was fired.
    /// </summary>
    [Fact]
    public void EveryDemonstrationPacksItsOwnBlobAndFiresWhereItIsTold()
    {
        var engine = Fake();
        engine.Returns("Precache.PrecacheModel", 12);
        engine.Returns("Precache.PrecacheDecal", 7);
        engine.Returns("Effects.EffectDispatchIndex", 3);
        engine.Ignores("Effects.TempEntPlayback");

        TempEntRegistry registry = Registry(engine);

        // Somewhere that is not the origin, on every axis, and not a round
        // number: a blob written at (0 0 0) reads back correctly whether the
        // offsets were applied or not.
        var at = new Vector(-1234.5f, 678.25f, 42.75f);

        var wrong = new List<string>();

        foreach (TeDemo demo in TeDemos.All)
        {
            bool sent = new TeFire(new RecordingLog(), registry, engine, engine).Send(demo, at, TeFilter.Broadcast, 0f);

            if (!sent)
            {
                wrong.Add($"{demo.ClassName} was refused");
                continue;
            }

            var call = engine.Last("Effects.TempEntPlayback")!.Value;
            byte[] blob = call.ArgAs<byte[]>(4);
            TempEntType type = registry.Find(demo.ClassName)!;

            if (blob.Length != type.BlobBytes || call.ArgAs<int>(1) != blob.Length)
                wrong.Add($"{demo.ClassName} sent {blob.Length} of {type.BlobBytes} bytes");

            // Every position the book declares relative, read back. This is the
            // half that catches a misspelled wire name that happens to be a real
            // name on some OTHER table: the write would succeed and the value
            // would land in the wrong field.
            var reader = new TempEntReader(type, blob);

            foreach (TeArg arg in demo.Args.Where(a => a.Relative && a.Kind == TeArgKind.Vector))
            {
                Vector got = reader.Vector(arg.Field);
                Vector want = new(at.X + arg.Offset.X, at.Y + arg.Offset.Y, at.Z + arg.Offset.Z);

                if (got.X != want.X || got.Y != want.Y || got.Z != want.Z)
                    wrong.Add($"{demo.ClassName}.{arg.Field} read back {got} and not {want}");
            }
        }

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == TeDemos.All.Count,
            $"all {TeDemos.All.Count} demonstrations were sent");

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>
    /// The inventory's one array is written element by element and not once.
    ///
    /// <para>
    /// <c>Vector("Spline", v)</c> addresses a FIELD, and an array is one
    /// field — so it writes element zero and silently leaves the other fifteen,
    /// which is a spline whose far end is at the world origin. The book uses
    /// <see cref="TeArgKind.Element"/> for exactly this, and this reads the far
    /// end back to prove the stride was honoured.
    /// </para>
    /// </summary>
    [Fact]
    public void TheSplineDemonstrationWritesEveryPointItClaims()
    {
        var engine = Fake();
        engine.Ignores("Effects.TempEntPlayback");

        TempEntRegistry registry = Registry(engine);

        var at = new Vector(64f, -32f, 16f);
        TeDemo spline = TeDemos.ByClass("CTEBeamSpline")!;

        Assert.True(new TeFire(new RecordingLog(), registry, engine, engine).Send(spline, at, TeFilter.Broadcast, 0f),
            "the spline went");

        var call = engine.Last("Effects.TempEntPlayback")!.Value;
        TempEntType type = registry.Find("CTEBeamSpline")!;
        var reader = new TempEntReader(type, call.ArgAs<byte[]>(4));

        TeArg last = spline.Args.Last(a => a.Kind == TeArgKind.Element);

        Vector got = reader.VectorAt("Spline", last.Index);

        Assert.True(got.X == at.X + last.Offset.X && got.Z == at.Z + last.Offset.Z,
            $"point {last.Index} reads back at {got}, not at the far end of the run — a stride "
          + "error shows up here and nowhere earlier");

        // AND the tail is zero rather than the previous spline's values, which
        // is the half of TE_BeamSpline nothing else would notice.
        Assert.True(reader.VectorAt("Spline", 15).X == 0f,
            "the ten unwritten elements are zeroed, not left holding whatever was there");
    }

    /// <summary>
    /// Every temp entity declaration, registered against a recording engine —
    /// the same registry the server builds, minus a game.
    /// </summary>
    private static TempEntRegistry Registry(RecordingEngine engine)
    {
        engine.Returns("Effects.TempEntRegister", 0);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        return registry;
    }

    /// <summary>
    /// THE MAP GENERATOR'S LIST AND THE REGISTRY'S ARE THE SAME LIST, which they
    /// have to be checked into being because they are computed twice.
    ///
    /// <para>
    /// <c>SandboxPass</c> reads <see cref="System.Reflection.CustomAttributeData"/>
    /// and derives a name it does not find, rather than naming
    /// <c>TempEntityAttribute</c> — doing that would enrol the build tool as a
    /// mod assembly. So the rule "a declaration with no names is CTE plus its
    /// class name" exists in two places.
    /// </para>
    ///
    /// <para>
    /// IT ALREADY DRIFTED ONCE, in the commit that let the names be derived: the
    /// generator's copy read the constructor arguments, found none, and returned
    /// NOTHING. Every sandbox coverage test went red at once, which is the good
    /// outcome — but they went red saying "TeDemos rows for temp entities the
    /// build does not send", which points at the book rather than at the reader.
    /// This one names the actual disagreement.
    /// </para>
    /// </summary>
    [Fact]
    public void TheMapGeneratorAndTheRegistryNameTheSameTempEntities()
    {
        var engine = TestServices.Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        // SHARED ONLY, because that is the assembly the generator scans. The
        // registry also picks up the declarations this test project defines for
        // its own golden-bytes fixtures -- CTEShatterSurfaceBefore and friends,
        // which exist to pin what a declaration packed BEFORE a conversion and
        // are not effects anything sends. Comparing those against the generator
        // would be comparing two different questions.
        var shared = TestServices.ServerGame;

        var fromRegistry = registry.All.Where(t => t.Class.RegistersClass
                                                && t.Class.Declaring.Assembly == shared)
                                       .Select(t => t.Class.ClassName)
                                       .OrderBy(c => c, StringComparer.Ordinal)
                                       .ToList();

        var fromGenerator = Sendable.OrderBy(c => c, StringComparer.Ordinal).ToList();

        var onlyRegistry  = fromRegistry.Except(fromGenerator, StringComparer.Ordinal).ToList();
        var onlyGenerator = fromGenerator.Except(fromRegistry, StringComparer.Ordinal).ToList();

        Assert.True(onlyRegistry.Count == 0,
            $"the registry builds {onlyRegistry.Count} temp entity/ies the map generator cannot "
          + $"see, so nothing would ever place a tester for them: {string.Join(", ", onlyRegistry)}");

        Assert.True(onlyGenerator.Count == 0,
            $"and the generator names {onlyGenerator.Count} the registry does not build: "
          + string.Join(", ", onlyGenerator));

        Assert.True(fromRegistry.Count > 40,
            $"and there are {fromRegistry.Count} of them, so neither list is empty — which is how "
          + "this pair failed the first time, with both halves agreeing on nothing");
    }

}
