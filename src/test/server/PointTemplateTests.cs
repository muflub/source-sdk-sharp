using System.Text;
using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// point_template, the entity.
///
/// <para>
/// What is checked here is everything the port MOVED: which keys it reads,
/// which names it claims, whether it eats them, whether the copies get unique
/// names, and — the one a mapper would notice — where each copy ends up. The
/// arithmetic underneath that last one has its own agreement with mathlib in
/// <see cref="TemplateTransformTests"/>; what these add is that this class asks
/// for the right transform with the right arguments.
/// </para>
///
/// <para>
/// Driven through the registry rather than by <c>new</c>, because half the
/// claims are about the ORDER Source works in — keys, then spawn, then inputs —
/// and an entity built by hand has no order to get wrong. The engine underneath
/// is still the strict double, so an unstubbed read is an error naming it.
/// </para>
///
/// <para>
/// That double answers one value per member, so a test needing two DIFFERENT
/// captures cannot have them. That shapes the tests rather than weakening them:
/// a template with two keys claiming one source each exercises the same loop,
/// the same accumulation and the same ordering as one with two different
/// answers would.
/// </para>
/// </summary>
public class PointTemplateTests
{
    private static readonly EntityHandle Ent = new(1);

    /// <summary>
    /// A template standing somewhere, facing somewhere, so that "relative to the
    /// template" is a claim with numbers in it. At the origin facing +X, every
    /// relative transform equals the absolute one and nothing below could tell
    /// the two apart.
    /// </summary>
    private static readonly Vector TemplateOrigin = new(100f, 200f, 8f);
    private static readonly Vector TemplateAngles = new(0f, 90f, 0f);

    /// <summary>A live template, its registry, its store, and the engine.</summary>
    private sealed record Placed(ScriptedEntityRegistry Registry, PointTemplate Template,
                                 RecordingEngine Engine, TemplateStore Store);

    private static Placed Place(int spawnFlags = 0,
                                Vector? origin = null, Vector? angles = null,
                                List<TemplatePlacement>? captures = null,
                                List<EntityHandle>? spawned = null,
                                TemplateStore? store = null,
                                params string[] sourceNames)
    {
        // THIS TEST'S OWN STORE, never a shared one. The store is where the
        // group's entity I/O gets rewritten and where the deferred precaches
        // accumulate, so one left over from another fact would make both
        // readable and neither trustworthy.
        store ??= SeededStore();

        var services = Services(out var engine, store);

        engine.Returns("Entities.GetOrigin", origin ?? TemplateOrigin)
              .Returns("Entities.GetAngles", angles ?? TemplateAngles)
              .Returns("Entities.GetSpawnFlags", spawnFlags);

        if (captures is not null)
            engine.Returns("Scripted.TemplateCapture", captures);

        if (spawned is not null)
            engine.Returns("Scripted.TemplateSpawnKeyed", spawned);

        var registry = new ScriptedEntityRegistry();
        registry.RegisterAll(typeof(PointTemplate).Assembly);
        registry.Services = services;

        for (int i = 0; i < sourceNames.Length; i++)
        {
            registry.KeyValue(Ent, EntityNames.PointTemplate,
                              $"Template{i + 1:D2}", sourceNames[i]);
        }

        registry.Spawn(Ent, EntityNames.PointTemplate);

        return new Placed(registry, (PointTemplate)registry.Get(Ent)!, engine, store);
    }

    /// <summary>
    /// A block of map text for one entity, as the BSP's entity lump spells one:
    /// quoted key/value pairs terminated by the closing brace the parser stops
    /// at.
    /// </summary>
    private static byte[] Block(params (string Key, string Value)[] pairs)
    {
        var text = new StringBuilder();

        foreach (var (key, value) in pairs)
            text.Append('"').Append(key).Append("\" \"").Append(value).Append("\"\n");

        return Encoding.UTF8.GetBytes(text.Append("}\n").ToString());
    }

    /// <summary>One source, at a place the test chose, with a template index.</summary>
    private static List<TemplatePlacement> Source(int index, Vector origin, QAngle angles)
        => new() { new TemplatePlacement(index, origin, angles) };

    private static IReadOnlyList<KeyedSpawn> LastPlacements(RecordingEngine engine)
        => Arg<IReadOnlyList<KeyedSpawn>>(engine, "Scripted.TemplateSpawnKeyed", 0);

    /// <summary>
    /// The pairs a spawn crossed with, as key=value joined by ';'. Since
    /// plan_server_novalve.md P5 the template is parsed managed-side, so this is
    /// what native is handed instead of the block's text.
    /// </summary>
    private static string KeysOf(KeyedSpawn spawn)
        => string.Join(';', spawn.Keys.Select(kv => kv.Key + "=" + kv.Value));

    /// <summary>
    /// A store holding sixteen plain templates, src0..src15, so that every
    /// Source(index) a fact names is a real block with a classname. The parse
    /// moved managed-side (P5) and an index into nothing now refuses before it
    /// crosses, where it used to cross empty and fail in the native parse.
    /// </summary>
    private static TemplateStore SeededStore()
    {
        var store = new TemplateStore(new RecordingLog());
        for (int i = 0; i < 16; i++)
            store.Add($"src{i}", "logic_relay",
                      Block(("classname", "logic_relay"), ("targetname", $"src{i}")));
        return store;
    }

    /// <summary>
    /// One argument of the last call to a member, typed.
    ///
    /// Asserts the call HAPPENED rather than dereferencing a null: the whole
    /// point of most of these tests is which call was made, and a test that
    /// threw a NullReferenceException instead of saying "nothing ever called
    /// this" reads as a broken test rather than a broken entity.
    /// </summary>
    private static T Arg<T>(RecordingEngine engine, string member, int index)
    {
        var call = engine.Last(member);
        Assert.True(call is not null, $"'{member}' was called at all\n{engine.Describe()}");
        return call!.Value.ArgAs<T>(index);
    }

    //-------------------------------------------------------------------------
    // The keys
    //-------------------------------------------------------------------------

    [Fact]
    public void TheSixteenTemplateKeysAreTheOnesItClaims()
    {
        var placed = Place(captures: new List<TemplatePlacement>());

        Assert.True(placed.Template.OnKeyValue("Template01", "crate"), "the first slot is a template key");
        Assert.True(placed.Template.OnKeyValue("Template16", "barrel"), "and so is the sixteenth");
        Assert.True(placed.Template.OnKeyValue("template09", "drum"),
            "matching is case-insensitive, as the map loader's own key matching is");
    }

    /// <summary>
    /// The red half. A prefix match alone would swallow "Template17" — which no
    /// map writes and which would be dropped into slot 17 of a sixteen-slot
    /// array — and a length check alone would swallow "Templatexx".
    /// </summary>
    [Fact]
    public void ANearMissIsNotATemplateKey()
    {
        var template = Place(captures: new List<TemplatePlacement>()).Template;

        Assert.False(template.OnKeyValue("Template17", "crate"), "there is no seventeenth slot");
        Assert.False(template.OnKeyValue("Template00", "crate"), "and no zeroth");
        Assert.False(template.OnKeyValue("Templatexx", "crate"), "a key of the right length is not a number");
        Assert.False(template.OnKeyValue("Template1", "crate"), "nor is the unpadded spelling stock never writes");
        Assert.False(template.OnKeyValue("targetname", "spawner"), "and a key belonging to something else is left alone");
    }

    //-------------------------------------------------------------------------
    // Capture
    //-------------------------------------------------------------------------

    [Fact]
    public void EachTemplateKeyClaimsItsNameOnce()
    {
        var placed = Place(captures: Source(3, new Vector(164f, 200f, 8f), QAngle.Zero),
                           sourceNames: new[] { "crate", "barrel" });

        Assert.True(placed.Engine.CountOf("Scripted.TemplateCapture") == 2,
            "the two keys that were set are claimed and the fourteen that were not are skipped");

        var claimed = placed.Engine.AllOf("Scripted.TemplateCapture")
                            .Select(c => c.ArgAs<string>(0)).ToList();

        Assert.True(claimed[0] == "crate" && claimed[1] == "barrel",
            "in slot order, which is the order a group is claimed in");

        Assert.True(placed.Template.TemplateCount == 2, "and both sources became templates");
    }

    /// <summary>
    /// The default, and the one every stock map relies on: a template source is
    /// a drawing of what to spawn, not a thing left standing in the world.
    /// </summary>
    [Fact]
    public void ATemplateEatsItsSourcesUnlessToldNotTo()
    {
        var placed = Place(captures: Source(0, TemplateOrigin, QAngle.Zero),
                           sourceNames: new[] { "crate" });

        Assert.True(placed.Template.RemovesItsSources, "with no spawnflag it consumes them");
        Assert.True(Arg<bool>(placed.Engine, "Scripted.TemplateCapture", 1),
            "and that is what the capture is told");
    }

    [Fact]
    public void TheKeepSourcesSpawnflagLeavesThemInTheMap()
    {
        var placed = Place(spawnFlags: 1,
                           captures: Source(0, TemplateOrigin, QAngle.Zero),
                           sourceNames: new[] { "crate" });

        Assert.False(placed.Template.RemovesItsSources, "the spawnflag says to keep them");
        Assert.False(Arg<bool>(placed.Engine, "Scripted.TemplateCapture", 1),
            "and that is what the capture is told");
    }

    /// <summary>
    /// A key that named nothing is the mapmaker mistake stock warns about, and
    /// it must leave no template behind — an empty one would be instanced later
    /// and parse nothing.
    /// </summary>
    [Fact]
    public void AKeyThatNamesNothingLeavesNoTemplate()
    {
        var placed = Place(captures: new List<TemplatePlacement>(),
                           sourceNames: new[] { "nothing_is_called_this" });

        Assert.True(placed.Engine.CountOf("Scripted.TemplateCapture") == 1, "it did look");
        Assert.True(placed.Template.TemplateCount == 0, "and found nothing");

        Assert.True(placed.Store.PendingPrecacheCount == 0,
            "a group of nothing has nothing to precache");
    }

    //-------------------------------------------------------------------------
    // What happens once the group is built
    //-------------------------------------------------------------------------

    [Fact]
    public void EveryTemplateInTheGroupIsScheduledForPrecache()
    {
        var placed = Place(captures: Source(11, TemplateOrigin, QAngle.Zero),
                           sourceNames: new[] { "crate", "barrel" });

        Assert.True(placed.Store.PendingPrecacheCount == 2,
            "the precache is scheduled for the WHOLE group and not once per key — "
            + "a template whose contents were never precached loads its models mid-game");
    }

    /// <summary>
    /// The reconnect, checked by its EFFECT rather than by the fact that a call
    /// was made.
    ///
    /// <para>
    /// Two templates wired to each other: <c>alpha</c> has an output naming
    /// <c>beta</c>. After the pass both must be marked as needing the instance
    /// stamp — the one holding the connection because its text now carries a
    /// marker, and the one pointed at because its targetname is about to.
    /// Without this, a group spawns copies still wired to the ORIGINALS, which
    /// have just been eaten, so the copies do nothing.
    /// </para>
    ///
    /// <para>
    /// This is the claim that could not be made while the store was native: the
    /// old fact could only assert that the crossing happened with two indices in
    /// it, and would have passed against a body that did nothing.
    /// </para>
    /// </summary>
    [Fact]
    public void AGroupWiredToItselfIsMarkedForTheInstanceStampAtBothEnds()
    {
        var store = new TemplateStore(new RecordingLog());

        int alpha = store.Add("alpha", "logic_relay",
                              Block(("classname", "logic_relay"),
                                    ("targetname", "alpha"),
                                    ("OnTrigger", "beta,Kill,,0,-1")));

        int beta = store.Add("beta", "logic_relay",
                             Block(("classname", "logic_relay"),
                                   ("targetname", "beta")));

        Place(captures: new List<TemplatePlacement>
              {
                  new(alpha, TemplateOrigin, QAngle.Zero),
                  new(beta, TemplateOrigin, QAngle.Zero),
              },
              store: store,
              sourceNames: new[] { "wired" });

        Assert.True(store.RequiresEntityIoFixup(alpha),
            "the template holding the connection carries a marker to stamp");
        Assert.True(store.RequiresEntityIoFixup(beta),
            "and so does the one it points at, whose targetname is renamed to match");
    }

    /// <summary>
    /// And the red half: a group whose members name nothing in the group is left
    /// alone. Marking everything would cost every template an instance-stamped
    /// copy it does not need, and would make the fact above pass on a pass that
    /// simply set the flag on all of them.
    /// </summary>
    [Fact]
    public void AGroupWiredToNothingIsLeftAlone()
    {
        var store = new TemplateStore(new RecordingLog());

        int alpha = store.Add("alpha", "logic_relay",
                              Block(("classname", "logic_relay"),
                                    ("targetname", "alpha"),
                                    ("OnTrigger", "somebody_else,Kill,,0,-1")));

        int beta = store.Add("beta", "logic_relay",
                             Block(("classname", "logic_relay"),
                                   ("targetname", "beta")));

        Place(captures: new List<TemplatePlacement>
              {
                  new(alpha, TemplateOrigin, QAngle.Zero),
                  new(beta, TemplateOrigin, QAngle.Zero),
              },
              store: store,
              sourceNames: new[] { "wired" });

        Assert.False(store.RequiresEntityIoFixup(alpha),
            "an output naming something outside the group is not a group connection");
        Assert.False(store.RequiresEntityIoFixup(beta), "and nothing points at beta");
    }

    /// <summary>
    /// Name fixup decides three separate things — whether the group's internal
    /// I/O is rewritten, which copy of the map data is precached, and which copy
    /// is parsed at spawn. They have to AGREE, so this checks the same answer
    /// reaches all three rather than that each one is separately plausible.
    /// </summary>
    /// <summary>
    /// A group wired to itself, with the mapper's PreserveNames spawnflag set.
    ///
    /// <para>
    /// Nothing may be renamed. A mapper only ticks that flag having promised
    /// that one instance exists at a time; the price of ignoring it is that
    /// every copy carries a unique name the map's own outputs no longer match.
    /// </para>
    /// </summary>
    [Fact]
    public void PreserveNamesLeavesTheGroupsWiringAlone()
    {
        var store = WiredPair(out int alpha, out int beta);

        var placed = Place(spawnFlags: 2,
                           captures: new List<TemplatePlacement>
                           {
                               new(alpha, TemplateOrigin, QAngle.Zero),
                               new(beta, TemplateOrigin, QAngle.Zero),
                           },
                           spawned: new List<EntityHandle> { new(12) },
                           store: store,
                           sourceNames: new[] { "wired" });

        Assert.False(placed.Template.AllowNameFixup, "the PreserveNames spawnflag turns it off");
        Assert.False(store.RequiresEntityIoFixup(alpha), "so nothing is marked for the stamp");
        Assert.False(store.RequiresEntityIoFixup(beta), "at either end");
    }

    /// <summary>
    /// The three things name fixup decides — whether the group's internal I/O is
    /// rewritten, which copy of the map data is spawned, and whether the spawned
    /// entity purges the suffix afterwards — have to AGREE. So this follows one
    /// decision all the way through rather than checking each is separately
    /// plausible.
    /// </summary>
    [Fact]
    public void WithoutTheSpawnflagTheStampedCopyIsWhatGetsSpawned()
    {
        var store = WiredPair(out int alpha, out int beta);

        // BOTH have to be in the group. The reconnect only rewrites a value that
        // names another template in the SAME group, so a capture holding alpha
        // alone would leave its output pointing at a beta nobody claimed -- which
        // is the case AGroupWiredToNothingIsLeftAlone covers.
        var placed = Place(captures: new List<TemplatePlacement>
                           {
                               new(alpha, TemplateOrigin, QAngle.Zero),
                               new(beta, TemplateOrigin, QAngle.Zero),
                           },
                           spawned: new List<EntityHandle> { new(12) },
                           store: store,
                           sourceNames: new[] { "wired" });

        placed.Template.CreateInstance(TemplateOrigin, QAngle.FromVector(TemplateAngles));

        Assert.True(placed.Template.AllowNameFixup, "the default is unique names");

        string text = KeysOf(LastPlacements(placed.Engine)[0]);

        // alpha's OUTPUT is what carries the marker, not alpha's own name:
        // nothing in the group points at alpha, so alpha is never renamed. What
        // the reconnect rewrote is the thing alpha points AT.
        Assert.True(text.Contains("beta&0001"),
            "the copy that goes down to the parser points at THIS instance's beta -- it "
            + "carries the instance number, not the unstamped marker the store holds");
        // The purge flag NO LONGER CROSSES (plan_server_novalve.md P5): native
        // never read it -- MarkNeedsNamePurge left with the SDK hierarchy -- so
        // the keyed spawn does not carry it.
    }

    /// <summary>
    /// Two more instances, two more numbers.
    ///
    /// <para>
    /// This is what stops an output inside the second copy of a group from
    /// reaching the first copy's entities, and it is the one property of the
    /// fixup that a single-instance test cannot see at all.
    /// </para>
    /// </summary>
    [Fact]
    public void EachInstanceOfAGroupGetsItsOwnNumber()
    {
        var store = WiredPair(out int alpha, out int beta);

        // BOTH have to be in the group. The reconnect only rewrites a value that
        // names another template in the SAME group, so a capture holding alpha
        // alone would leave its output pointing at a beta nobody claimed -- which
        // is the case AGroupWiredToNothingIsLeftAlone covers.
        var placed = Place(captures: new List<TemplatePlacement>
                           {
                               new(alpha, TemplateOrigin, QAngle.Zero),
                               new(beta, TemplateOrigin, QAngle.Zero),
                           },
                           spawned: new List<EntityHandle> { new(12) },
                           store: store,
                           sourceNames: new[] { "wired" });

        placed.Template.CreateInstance(TemplateOrigin, QAngle.FromVector(TemplateAngles));
        string first = KeysOf(LastPlacements(placed.Engine)[0]);

        placed.Template.CreateInstance(TemplateOrigin, QAngle.FromVector(TemplateAngles));
        string second = KeysOf(LastPlacements(placed.Engine)[0]);

        Assert.True(first.Contains("beta&0001"), "the first instance wires up to its own beta");
        Assert.True(second.Contains("beta&0002"), "and the second to a different one");
    }

    /// <summary>Two templates in a store, one wired to the other.</summary>
    private static TemplateStore WiredPair(out int alpha, out int beta)
    {
        var store = new TemplateStore(new RecordingLog());

        alpha = store.Add("alpha", "logic_relay",
                          Block(("classname", "logic_relay"),
                                ("targetname", "alpha"),
                                ("OnTrigger", "beta,Kill,,0,-1")));

        beta = store.Add("beta", "logic_relay",
                         Block(("classname", "logic_relay"),
                               ("targetname", "beta")));

        return store;
    }

    //-------------------------------------------------------------------------
    // Instancing — the part a mapper sees
    //-------------------------------------------------------------------------

    /// <summary>
    /// The claim the whole class exists for. A source stands 64 units in FRONT
    /// of a template facing +Y; instanced somewhere else facing +X, the copy has
    /// to stand 64 units in front of THAT.
    ///
    /// <para>
    /// The template is deliberately rotated. With both facings at zero the
    /// relative transform is a plain subtraction, and an implementation that
    /// ignored rotation entirely would pass.
    /// </para>
    /// </summary>
    [Fact]
    public void ACopyKeepsItsPlaceWithinTheGroupWhenTheGroupMoves()
    {
        // The template faces +Y (yaw 90), so "64 in front of it" is +64 on Y.
        var sourceOrigin = new Vector(TemplateOrigin.X, TemplateOrigin.Y + 64f, TemplateOrigin.Z);

        var placed = Place(captures: Source(4, sourceOrigin, QAngle.Zero),
                           spawned: new List<EntityHandle> { new(12) },
                           sourceNames: new[] { "crate" });

        // Instanced at the world origin facing +X, so "64 in front" is now +64 on X.
        placed.Template.CreateInstance(Vector.Zero, QAngle.Zero);

        var placements = LastPlacements(placed.Engine);

        Assert.True(placements.Count == 1, "one source, one placement");
        Assert.True(KeysOf(placements[0]) == "classname=logic_relay;targetname=src4",
            "and carrying that index's template, parsed: template 4's pairs, classname first, "
            + $"got: {KeysOf(placements[0])}");

        Assert.True(MathF.Abs(placements[0].Origin.X - 64f) < 0.01f,
            "the copy is 64 in front of where the group was put");
        Assert.True(MathF.Abs(placements[0].Origin.Y) < 0.01f, "and nowhere off to the side");
        Assert.True(MathF.Abs(placements[0].Origin.Z) < 0.01f, "and at the group's height");
    }

    /// <summary>
    /// And the identity that says the stored transform is relative at all:
    /// instancing where the template stands puts the source back exactly where
    /// it was drawn, rotation and all.
    /// </summary>
    [Fact]
    public void InstancingWhereTheTemplateStandsPutsASourceBackWhereItWas()
    {
        var sourceOrigin = new Vector(40f, 60f, 80f);
        var sourceAngles = new QAngle(12f, 34f, 56f);

        var placed = Place(captures: Source(0, sourceOrigin, sourceAngles),
                           spawned: new List<EntityHandle> { new(12) },
                           sourceNames: new[] { "crate" });

        placed.Template.CreateInstance(TemplateOrigin, QAngle.FromVector(TemplateAngles));

        var back = LastPlacements(placed.Engine)[0];

        Assert.True(MathF.Abs(back.Origin.X - sourceOrigin.X) < 0.01f, "back at its own x");
        Assert.True(MathF.Abs(back.Origin.Y - sourceOrigin.Y) < 0.01f, "its own y");
        Assert.True(MathF.Abs(back.Origin.Z - sourceOrigin.Z) < 0.01f, "its own z");

        Assert.True(MathF.Abs(back.Angles.Pitch - sourceAngles.Pitch) < 0.01f, "and its own pitch");
        Assert.True(MathF.Abs(back.Angles.Yaw - sourceAngles.Yaw) < 0.01f, "its own yaw");
        Assert.True(MathF.Abs(back.Angles.Roll - sourceAngles.Roll) < 0.01f, "its own roll");
    }

    [Fact]
    public void ATemplateHoldingNothingSpawnsNothingAndAsksForNothing()
    {
        var placed = Place(captures: new List<TemplatePlacement>());

        var made = placed.Template.CreateInstance(Vector.Zero, QAngle.Zero);

        Assert.True(made.Count == 0, "nothing came out");
        Assert.True(placed.Engine.CountOf("Scripted.TemplateSpawnKeyed") == 0,
            "and the engine was never asked to parse anything");
    }

    //-------------------------------------------------------------------------
    // The input
    //-------------------------------------------------------------------------

    [Fact]
    public void ForceSpawnMakesACopyAtTheTemplatesOwnPlaceAndSaysSo()
    {
        var sourceOrigin = new Vector(40f, 60f, 80f);

        var placed = Place(captures: Source(0, sourceOrigin, QAngle.Zero),
                           spawned: new List<EntityHandle> { new(12) },
                           sourceNames: new[] { "crate" });

        placed.Engine.Clear();

        Assert.True(placed.Registry.Input(Ent, "ForceSpawn", string.Empty, 0f, EntityHandle.None),
            "the input the map fires is bound to something");

        var back = LastPlacements(placed.Engine)[0];

        Assert.True(MathF.Abs(back.Origin.X - sourceOrigin.X) < 0.01f,
            "a forced spawn puts the group back exactly where it was drawn");

        Assert.True(placed.Template.OnEntitySpawned.FireCount == 1,
            "and the output says it happened");
    }

    /// <summary>
    /// The other half, and the one that would rot unnoticed: a template that
    /// made nothing must not fire the output. A map counting spawns off it would
    /// otherwise count spawns that never happened.
    /// </summary>
    [Fact]
    public void ForceSpawnOnATemplateThatMadeNothingFiresNothing()
    {
        var placed = Place(captures: Source(0, TemplateOrigin, QAngle.Zero),
                           spawned: new List<EntityHandle>(),
                           sourceNames: new[] { "crate" });

        placed.Registry.Input(Ent, "ForceSpawn", string.Empty, 0f, EntityHandle.None);

        Assert.True(placed.Engine.CountOf("Scripted.TemplateSpawnKeyed") == 1, "it did try");
        Assert.True(placed.Template.OnEntitySpawned.FireCount == 0,
            "and did not claim to have succeeded");
    }

    /// <summary>
    /// plan_server_novalve.md P5: the template is parsed HERE now, so a block
    /// with no classname is refused here -- stock's all-or-nothing answer --
    /// rather than crossing for native to fail on.
    /// </summary>
    [Fact]
    public void ATemplateWithNoClassnameIsRefusedBeforeItCrosses()
    {
        var store = new TemplateStore(new RecordingLog());
        int bare = store.Add("bare", "logic_relay", Block(("targetname", "bare")));

        var placed = Place(captures: Source(bare, TemplateOrigin, QAngle.Zero),
                           spawned: new List<EntityHandle> { new(12) },
                           store: store,
                           sourceNames: new[] { "bare" });

        var made = placed.Template.CreateInstance(TemplateOrigin, QAngle.Zero);

        // Positive arm: the capture reached the template, so the absence below is
        // about the refusal and not about a group that held nothing.
        Assert.True(placed.Engine.CountOf("Scripted.TemplateCapture") >= 1, "the group was captured");
        Assert.True(placed.Engine.CountOf("Scripted.TemplateSpawnKeyed") == 0,
            "an unreadable template must not cross");
        Assert.Empty(made);
    }
}
