using SourceSharp.Abi;
using SourceSharp.Shared;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// prop_dynamic_ornament — the entity the missing parenting cost, and therefore
/// the one whose tests are about parenting rather than about decoration.
///
/// <para>
/// WHAT THESE CANNOT CHECK, said first so the green is read for what it is: not
/// where the ornament ends up. Composition is matrix arithmetic against a live
/// parent, and there is no parent and no matrix here — the double records that
/// SetParent was ASKED FOR and answers what the test tells it to. The placement
/// is checked live, in a map, by swinging a door and reading both origins back.
/// </para>
///
/// <para>
/// Nor can they check visibility. <c>SetVisible</c> and <c>SetSolidity</c> go
/// through static <c>Engine</c> calls rather than the injected
/// <c>IEngineEntities</c>, so the double never sees them. What stands in for
/// them is <see cref="OrnamentNetState.AttachedToIndex"/>, which is written on
/// the same side of every branch: both happen only after a link that succeeded.
/// </para>
///
/// <para>
/// WHAT THEY DO CHECK is the half that would be silently wrong: that spawn looks
/// up no name, that a refused link announces nothing, and that detaching lets go
/// rather than only hiding. props.cpp refused to port this entity precisely
/// because "porting it without parenting would silently produce an ornament that
/// never attaches" — and an ornament that announces an attachment it did not get
/// is that same failure with the parenting present.
/// </para>
/// </summary>
public class PropDynamicOrnamentTests
{
    private static readonly EntityHandle Ent = new(0x0301);

    //-------------------------------------------------------------------------
    // The stubs every one of these needs, named rather than defaulted.
    //
    // GetName is read only to build the warning text for a name nothing answers
    // to — not what any of these is about, but the double is strict and an
    // unstubbed read is an error rather than a silent zero.
    //-------------------------------------------------------------------------
    private static ScriptedEntityRegistry Registry(out RecordingEngine engine,
        params (string Key, string Value)[] keys)
    {
        var services = Services(out engine);
        engine.Ignores("Entities.GetName");

        // Spawn DETACHES, which asks the hierarchy to let go — so every one of
        // these needs an answer to SetParent before the entity exists, even the
        // tests that never attach. False here is the answer for the unparenting
        // spawn does; a test that attaches re-stubs it true afterwards.
        engine.Returns("Entities.SetParent", false);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(PropDynamicOrnament).Assembly);
        registry.Services = services;

        foreach (var (key, value) in keys)
            registry.KeyValue(Ent, EntityNames.PropDynamicOrnament, key, value);

        registry.Spawn(Ent, EntityNames.PropDynamicOrnament);

        return registry;
    }

    private static PropDynamicOrnament Ornament(ScriptedEntityRegistry registry)
        => (PropDynamicOrnament)registry.Get(Ent)!;

    // Which entity the last SetParent named as the parent.
    private static EntityHandle LastParent(RecordingEngine engine)
        => engine.Last("Entities.SetParent")!.Value.ArgAs<EntityHandle>(1);

    //-------------------------------------------------------------------------
    // THE SPAWN STATE. Stock's DetachFromOwner, and it is why an unattached
    // ornament is useless as a fixture: it adds EF_NODRAW, so one that has never
    // been attached cannot be seen at all.
    //-------------------------------------------------------------------------
    [Fact]
    public void AnUnattachedOrnamentRidesNothing()
    {
        var registry = Registry(out var engine, ("model", "models/props/chair.mdl"));

        Assert.True(engine.CountOf("Entities.SetParent") == 1,
            "spawning detaches, which is one ask of the hierarchy");

        Assert.True(LastParent(engine) == EntityHandle.None,
            "and what it asks for is no parent at all");

        Assert.True(Ornament(registry).State.AttachedToIndex == -1,
            "so the client is told there is nothing to merge its bones onto");
    }

    //-------------------------------------------------------------------------
    // NO LOOKUP AT SPAWN, EVER. Stock attaches in Activate() rather than
    // Spawn() because the entity named may not have been parsed yet — half a
    // map's entities do not exist when the first one spawns. This hierarchy has
    // no Activate, so the deferral is a one-shot think, and the assertion is
    // that spawn looks nothing up whatever InitialOwner says.
    //
    // Without the deferral this passes for an ornament placed LAST in a map and
    // fails for one placed first, which is the kind of bug that arrives as "it
    // works on my map".
    //-------------------------------------------------------------------------
    [Fact]
    public void SpawnDefersTheAttachRatherThanLookingUpANameNothingHasParsedYet()
    {
        Registry(out var engine, ("model", "models/props/chair.mdl"), ("InitialOwner", "tree"));

        Assert.True(engine.CountOf("Entities.FindByName") == 0,
            "spawn does not look up a name the map may not have reached yet");
    }

    //-------------------------------------------------------------------------
    // THE ATTACH. The parent is set, and the client is told which entindex to
    // merge onto — the two halves stock's FollowEntity did in one call.
    //-------------------------------------------------------------------------
    [Fact]
    public void AttachingParentsItAndTellsTheClientWhatToMergeOnto()
    {
        var registry = Registry(out var engine, ("model", "models/props/chair.mdl"));
        var ornament = Ornament(registry);

        var tree = new EntityHandle(0x0499);
        engine.Returns("Entities.FindByName", tree);
        engine.Returns("Entities.SetParent", true);
        engine.Returns("Entities.GetIndex", 73);

        registry.Input(Ent, "SetAttached", "tree", 0f, default);

        Assert.True(LastParent(engine) == tree,
            "attaching parents the ornament to what it was told to ride");

        Assert.True(ornament.State.AttachedToIndex == 73,
            "and the client is told which entindex to merge its bones onto");
    }

    //-------------------------------------------------------------------------
    // THE FAILURE props.cpp REFUSED TO SHIP, now that there is parenting to
    // fail. A link the hierarchy refuses — a cycle, an entity that has not
    // spawned — must leave the ornament exactly as it was.
    //
    // Proved by mutation rather than trusted: announcing the attachment before
    // the link succeeded leaves a chair hanging in mid-air where a parent was
    // going to be, which is the picture the original comment called "worse than
    // not answering the name".
    //-------------------------------------------------------------------------
    [Fact]
    public void ARefusedLinkLeavesTheOrnamentRidingNothing()
    {
        var registry = Registry(out var engine, ("model", "models/props/chair.mdl"));
        var ornament = Ornament(registry);

        engine.Returns("Entities.FindByName", new EntityHandle(0x0499));

        // The hierarchy refused it.
        engine.Returns("Entities.SetParent", false);

        registry.Input(Ent, "SetAttached", "tree", 0f, default);

        Assert.True(ornament.State.AttachedToIndex == -1,
            "a refused link tells the client nothing, so nothing is drawn on it");
    }

    //-------------------------------------------------------------------------
    // A name nothing answers to is the mapper's typo. It is reported, and it
    // does not reach the hierarchy at all.
    //-------------------------------------------------------------------------
    [Fact]
    public void AnUnknownNameAttachesToNothing()
    {
        var registry = Registry(out var engine, ("model", "models/props/chair.mdl"));
        var ornament = Ornament(registry);

        engine.Returns("Entities.FindByName", EntityHandle.None);

        int atSpawn = engine.CountOf("Entities.SetParent");

        registry.Input(Ent, "SetAttached", "nosuchthing", 0f, default);

        Assert.True(engine.CountOf("Entities.SetParent") == atSpawn,
            "nothing is parented to an entity that does not exist");

        Assert.True(ornament.State.AttachedToIndex == -1, "and it rides nothing");
    }

    //-------------------------------------------------------------------------
    // DETACHING lets go rather than only hiding. An ornament that was merely
    // made invisible would still be dragged around by its old parent for the
    // rest of the round, and would reappear in the wrong place if re-shown.
    //-------------------------------------------------------------------------
    [Fact]
    public void DetachingLetsGoRatherThanOnlyHiding()
    {
        var registry = Registry(out var engine, ("model", "models/props/chair.mdl"));
        var ornament = Ornament(registry);

        engine.Returns("Entities.FindByName", new EntityHandle(0x0499));
        engine.Returns("Entities.SetParent", true);
        engine.Returns("Entities.GetIndex", 73);

        registry.Input(Ent, "SetAttached", "tree", 0f, default);
        Assert.True(ornament.State.AttachedToIndex == 73, "it is riding the tree");

        registry.Input(Ent, "Detach", "", 0f, default);

        Assert.True(LastParent(engine) == EntityHandle.None,
            "detaching asks the hierarchy to let go");

        Assert.True(ornament.State.AttachedToIndex == -1,
            "and the client stops merging its bones onto the old parent");
    }
}
