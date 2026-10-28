using SourceSharp.Abi;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// Carrying an entity's state across a hot reload.
///
/// <para>
/// THE WHOLE POINT, and it is worth stating before any of the detail: a reload
/// keeps every entity and destroys every managed object, so without this an
/// entity comes back holding its CLASS's declared defaults rather than the
/// MAP's values — a <c>func_door</c> with stock's speed instead of the
/// mapper's, thinking and moving and looking entirely correct. That is worse
/// than an entity that stopped working, because nothing about it looks wrong.
/// </para>
///
/// <para>
/// These tests drive the entity directly rather than through the registry
/// wherever they can, per <c>docs/writing-tests.md</c>: what is under test is
/// what <c>SaveState</c> and <c>LoadState</c> do, and a round trip through the
/// registry would be testing the wiring as well and could not say which broke.
/// The two that DO go through the registry say so and are about the wiring.
/// </para>
/// </summary>
public class ReloadStateTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);

    /// <summary>Builds the fixture the way the registry does, without one.</summary>
    private static ReloadFixtureEntity Fixture()
    {
        var entity = new ReloadFixtureEntity { ClassName = "reload_fixture" };
        entity.BindAreas(TestServices.Services());

        entity.VarSets = new ScriptedVarSet[] { entity.Vars };
        entity.Outputs = new[] { entity.OnFirst, entity.OnSecond };

        entity.OnFirst.Bind(entity, "OnFirst", 0);
        entity.OnSecond.Bind(entity, "OnSecond", 1);

        return entity;
    }

    /// <summary>
    /// THE HEADLINE: a value the map wrote comes back, and a value nobody wrote
    /// does not pretend to have been written.
    /// </summary>
    [Fact]
    public void AMapsValueSurvivesTheReloadAndADefaultDoesNotPretendTo()
    {
        var before = Fixture();

        // What a map does. Speed and Label are written; Where is not.
        Assert.True(before.Vars.OnKeyDataUpdate("speed", "512.5"), "the map writes speed");
        Assert.True(before.Vars.OnKeyDataUpdate("label", "the mapper's"), "the map writes label");

        var carried = new SavedEntity(7u, "reload_fixture", "door_01");
        before.SaveState(carried);

        // The reload: the old object is gone and a brand new one of a brand new
        // build takes its place, holding its class's declared initialisers.
        var after = Fixture();

        Assert.True(after.Vars.Speed == 100f && after.Vars.Label == "stock",
                    "a fresh instance starts at the class's defaults, which is the problem");

        after.LoadState(carried);

        Assert.True(after.Vars.Speed == 512.5f, "the map's speed came back, not the class's");
        Assert.True(after.Vars.Label == "the mapper's", "and so did the map's label");
        Assert.True(after.Vars.SpeedIsSet && after.Vars.LabelIsSet, "and both still report that the map set them");

        // The other half, and the one that is easy to get wrong by carrying
        // everything: a key the map never touched must come back reporting that
        // nobody touched it.
        Assert.True(after.Vars.Where.Equals(new Vector(1f, 1f, 1f)), "an untouched key holds its initialiser");
        Assert.True(!after.Vars.WhereIsSet, "and does not claim the map wrote it");
        Assert.True(after.Vars.WhereLastSetAt == ScriptedVarSet.Never, "with no stamp");
    }

    /// <summary>
    /// The stamp survives, which the value coming back does not prove.
    ///
    /// <para>
    /// Restoring replays the key through the ordinary <c>OnKeyDataUpdate</c>, and
    /// that stamps it with the time of the REPLAY — so without the descriptor's
    /// <c>Stamp</c> a var the map wrote at 12.5 would come back claiming it was
    /// written when the reload happened. A lie about the entity's own history,
    /// told quietly, in the one field whose entire job is to answer "when".
    /// </para>
    ///
    /// <para>
    /// The clock reads zero with no engine, so the stamp is forced to something
    /// a replay could not produce. Without that this test passes whether or not
    /// anything restores anything.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStampSurvivesAndIsNotTheTimeOfTheReplay()
    {
        var before = Fixture();

        before.Vars.OnKeyDataUpdate("speed", "512.5");

        foreach (var key in before.Vars.Descriptors)
        {
            if (key.Name == "Speed")
                key.Stamp(before.Vars, 12.5f);
        }

        Assert.True(before.Vars.SpeedLastSetAt == 12.5f, "the fixture really is stamped at 12.5");

        var carried = new SavedEntity(7u, "reload_fixture", "door_01");
        before.SaveState(carried);

        var after = Fixture();
        after.LoadState(carried);

        Assert.True(after.Vars.Speed == 512.5f, "the value came back");
        Assert.True(after.Vars.SpeedLastSetAt == 12.5f,
                    "and so did the time the map wrote it, rather than the time of the reload");
    }

    /// <summary>
    /// AN ENUM ROUND TRIPS, INCLUDING A FLAGS VALUE, and this is the trap that
    /// was there before the descriptor grew a second reader.
    ///
    /// <para>
    /// The descriptor's <c>Read</c> is for a person and formats an enum by name:
    /// <c>Cutting|Burning</c>, or a bare number marked <c>(unnamed)</c> for a bit
    /// nothing names. <c>ScriptedVarParse.Enum</c> reads a number or ONE member
    /// name, and neither of those. So a carrier built on <c>Read</c> would have
    /// restored a single-member enum and silently dropped every combination —
    /// leaving the declared default in place, with a warning that reads like a
    /// bad map value.
    /// </para>
    /// </summary>
    [Fact]
    public void AnEnumSurvivesAndSoDoesACombinationOfFlags()
    {
        var before = Fixture();

        before.Vars.OnKeyDataUpdate("mood", "2");
        before.Vars.OnKeyDataUpdate("hazard", "3");

        Assert.True(before.Vars.Mood == ReloadMood.Livid, "the map set a plain enum");
        Assert.True(before.Vars.Hazard == (ReloadHazard.Cutting | ReloadHazard.Burning),
                    "and a flags enum with two bits, which is the case that breaks a name");

        var carried = new SavedEntity(7u, "reload_fixture", "door_01");
        before.SaveState(carried);

        var after = Fixture();
        after.LoadState(carried);

        Assert.True(after.Vars.Mood == ReloadMood.Livid, "the plain enum came back");
        Assert.True(after.Vars.Hazard == (ReloadHazard.Cutting | ReloadHazard.Burning),
                    "and so did both bits of the flags one");
    }

    /// <summary>
    /// Save and Read are two functions on purpose, and the difference is only
    /// visible on an enum. Pinned here so that folding them back together
    /// breaks something.
    /// </summary>
    [Fact]
    public void SavingAnEnumIsNotTheSameAsShowingOne()
    {
        var value = ReloadHazard.Cutting | ReloadHazard.Burning;

        Assert.True(ScriptedVarParse.Save(value) == "3", "an enum saves as the number a map would write");
        Assert.True(ScriptedVarParse.Format(value).Contains("Cutting"), "and shows as the names a person reads");

        Assert.True(ScriptedVarParse.Enum<ReloadHazard>(ScriptedVarParse.Save(value), out var back) && back == value,
                    "so the saved spelling reads back and the shown one is not asked to");

        // Every other type has ONE spelling, which is why this is a difference
        // rather than a second format.
        Assert.True(ScriptedVarParse.Save(1.5f) == ScriptedVarParse.Format(1.5f), "a float saves as it shows");
        Assert.True(ScriptedVarParse.Save(new Vector(1f, 2f, 3f)) == ScriptedVarParse.Format(new Vector(1f, 2f, 3f)),
                    "and so does a vector");
    }

    /// <summary>
    /// A key the new build no longer declares is dropped and said out loud,
    /// rather than throwing or being written somewhere else.
    /// </summary>
    [Fact]
    public void AKeyTheNewBuildNoLongerDeclaresIsDroppedAndNamed()
    {
        var carried = new SavedEntity(7u, "reload_fixture", "door_01");

        // What a build that HAD the key saved.
        carried.SaveVar("Speed", "512.5", 12.5f);
        carried.SaveVar("RemovedSinceThatBuild", "9", 3f);

        var after = Fixture();
        after.LoadState(carried);

        Assert.True(after.Vars.Speed == 512.5f, "the key that still exists is restored");

        // And the one that does not exists nowhere: not thrown, not guessed at,
        // not handed to OnKeyValue as if the map had written it.
        Assert.True(!after.Vars.Descriptors.Any(d => d.Name == "RemovedSinceThatBuild"),
                    "the key that went away is not declared and is not invented");
    }

    /// <summary>
    /// A value the new build cannot read is refused by the same path a bad map
    /// value takes — never coerced into something plausible.
    /// </summary>
    [Fact]
    public void AValueThatNoLongerParsesLeavesTheDefaultRatherThanBeingCoerced()
    {
        var carried = new SavedEntity(7u, "reload_fixture", "door_01");

        // A build where "Where" was a string, restored into one where it is a
        // Vector. Same name, same key, and nothing about the text says three
        // numbers.
        carried.SaveVar("Where", "over there", 4f);

        var after = Fixture();
        after.LoadState(carried);

        Assert.True(after.Vars.Where.Equals(new Vector(1f, 1f, 1f)), "the declared default is left in place");
        Assert.True(!after.Vars.WhereIsSet, "and the key does not claim to have been set");
    }

    /// <summary>
    /// An output's tally comes back. Its WIRING was never in danger, and the
    /// test says so as well as saying what was.
    /// </summary>
    [Fact]
    public void AnOutputsTallySurvivesAndItsWiringWasNeverManaged()
    {
        var before = Fixture();

        // Fired without an engine, which records the tally and makes an ABI
        // call that goes nowhere. That is exactly the half being tested.
        before.OnFirst.Fire("first value");
        before.OnFirst.Fire("second value");

        Assert.True(before.OnFirst.FireCount == 2, "the fixture really did fire twice");

        var carried = new SavedEntity(7u, "reload_fixture", "door_01");
        before.SaveState(carried);

        var after = Fixture();

        Assert.True(after.OnFirst.FireCount == 0, "a fresh instance starts at zero");

        after.LoadState(carried);

        Assert.True(after.OnFirst.FireCount == 2, "the count of what already happened came back");
        Assert.True(after.OnFirst.LastValue == "second value", "and what it last carried");
        Assert.True(after.OnSecond.FireCount == 0, "an output that never fired still reports zero");

        // Said here rather than only in a comment: there is nothing in the
        // carrier that could restore a connection, because a connection is a
        // native CEventAction the reload never touched.
        Assert.True(carried.Outputs.Count == 2, "an output carries its tally");
        Assert.True(!carried.Local.ContainsKey("OnFirst"), "and nothing else");
    }

    /// <summary>
    /// Local state — neither a map key nor networked — is the half a default
    /// cannot discover, and the reason the method is virtual at all.
    /// </summary>
    [Fact]
    public void LocalStateCrossesThroughTheOverride()
    {
        var before = Fixture();
        before.TravelledLegs = 4;

        var carried = new SavedEntity(7u, "reload_fixture", "door_01");
        before.SaveState(carried);

        var after = Fixture();
        after.LoadState(carried);

        Assert.True(after.TravelledLegs == 4, "a plain field crosses because the class said to carry it");
    }

    /// <summary>
    /// AN OVERRIDE THAT FORGETS <c>base</c> IS CAUGHT, which is the difference
    /// between a mechanism and a trap.
    ///
    /// <para>
    /// Without this, one class writing an override without chaining loses every
    /// map key it declares — and its entities come back looking perfectly
    /// ordinary with the class's defaults in them, which is the exact failure
    /// this whole mechanism exists to prevent, reintroduced by the mechanism.
    /// </para>
    /// </summary>
    [Fact]
    public void AnOverrideThatSkipsBaseIsNoticedRatherThanTrusted()
    {
        var before = new ReloadForgetfulEntity { ClassName = "reload_forgetful" };
        before.VarSets = new ScriptedVarSet[] { before.Vars };
        before.Vars.OnKeyDataUpdate("speed", "512.5");
        before.Counter = 9;

        var carried = new SavedEntity(7u, "reload_forgetful", "thing");
        before.SaveState(carried);

        // The flag is what the registry reads, and it is false because the
        // override never reached the base body that sets it.
        Assert.True(!carried.DefaultRan, "the skipped base is visible, rather than being a silent loss");
        Assert.True(carried.Vars.Count == 0, "and nothing declared was saved by the override alone");

        // What the registry then does for it.
        before.SaveDeclaredState(carried);

        Assert.True(carried.DefaultRan && carried.Vars.ContainsKey("Speed"),
                    "running the default afterwards recovers what the override dropped");
    }

    /// <summary>
    /// A map key is stamped with the time of the registry that wrote it
    /// (plan_abi_hotreload D8): each registry's own clock, not the static
    /// engine's, so two instances in one process stamp their own game time.
    /// </summary>
    [Fact]
    public void EachRegistryStampsAKeyWithItsOwnClock()
    {
        var handle = new EntityHandle(0x0004_0011u);

        float Stamped(float now)
        {
            var registry = new ScriptedEntityRegistry(TestEngines.Server());
            registry.RegisterAll(typeof(ReloadStateTests).Assembly);
            registry.Services = Services(out var engine);
            engine.Returns("Clock.Now", now);

            Assert.True(registry.KeyValue(handle, "reload_fixture", "speed", "1"), "the map writes a key");
            return ((ReloadFixtureEntity)registry.Get(handle)!).Vars.SpeedLastSetAt;
        }

        Assert.Equal(3f, Stamped(3f));
        Assert.Equal(7f, Stamped(7f));
    }

    /// <summary>
    /// The round trip through the registry, which is the wiring rather than the
    /// rule: an entity that existed before a reload exists after one, holding
    /// what it held.
    /// </summary>
    [Fact]
    public void TheRegistryRebuildsAnEntityAndRefillsIt()
    {
        _reload.Clear();

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ReloadStateTests).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(0x0004_0011u);

        // A map's keyvalue is what makes the instance exist, which is the same
        // path the reload's restore takes.
        Assert.True(registry.KeyValue(handle, "reload_fixture", "speed", "512.5"), "the map writes a key");

        var live = (ReloadFixtureEntity)registry.Get(handle)!;
        live.TravelledLegs = 3;
        live.OnFirst.Fire("gone?");

        Assert.True(registry.SaveForReload(_reload) == 1, "one entity's state is put aside");
        Assert.True(ReloadCarrier.Count(_reload) == 1, "and it is waiting in the ABI, not in the registry");

        // The reload: a brand new registry, as a brand new assembly's would be.
        var reloaded = new ScriptedEntityRegistry(TestEngines.Server());
        reloaded.RegisterAll(typeof(ReloadStateTests).Assembly);
        reloaded.Services = Services();

        Assert.True(reloaded.Get(handle) is null, "the new registry starts with nothing, which is the problem");

        Assert.True(reloaded.RestoreAfterReload(_reload) == 1, "and one entity is rebuilt");
        Assert.True(ReloadCarrier.Count(_reload) == 0, "taking the store empties it, so a second restore replays nothing");

        var back = (ReloadFixtureEntity)reloaded.Get(handle)!;

        Assert.True(!ReferenceEquals(back, live), "it really is a different object");
        Assert.True(back.Vars.Speed == 512.5f, "holding the map's value");
        Assert.True(back.TravelledLegs == 3, "its own local state");
        Assert.True(back.OnFirst.FireCount == 1, "and the tally of what it had already done");
    }

    /// <summary>
    /// And the entity is not merely present: it answers, which is the whole
    /// reason any of this exists.
    /// </summary>
    [Fact]
    public void ARestoredEntityTakesInputsAgain()
    {
        _reload.Clear();

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ReloadStateTests).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(0x0004_0012u);
        registry.KeyValue(handle, "reload_fixture", "speed", "1");

        registry.SaveForReload(_reload);

        var reloaded = new ScriptedEntityRegistry(TestEngines.Server());
        reloaded.RegisterAll(typeof(ReloadStateTests).Assembly);
        reloaded.Services = Services();

        // BEFORE the restore, which is what a reload used to leave behind: the
        // entity is in the world, replicating, and nothing reaches it.
        Assert.True(!reloaded.Input(handle, "Nudge", "", 0f, EntityHandle.None),
                    "an entity with no managed half drops every input, silently");

        reloaded.RestoreAfterReload(_reload);

        Assert.True(reloaded.Input(handle, "Nudge", "", 0f, EntityHandle.None),
                    "and a restored one takes them again");

        // Answered rather than merely accepted. A registry that returned true
        // and dispatched nowhere would pass the line above and would be the
        // same entity doing nothing.
        Assert.True(((ReloadFixtureEntity)reloaded.Get(handle)!).Nudges == 1,
                    "and the input reached the method the map is wired to");
    }

    //-------------------------------------------------------------------------
    // THE RESTORE TALLY IS MEASURED FROM WHAT CAME BACK, NOT FROM WHAT WAS
    // OFFERED.
    //
    // Two live runs reloaded a game whose console said
    //
    //   reload: player '' (281477122) could not be rebuilt (PlayerNetState
    //     needs 692 byte(s) and entity 2 ('player') offers 0)
    //   reload: 480 of 481 entit(ies) restored; 1 FAILED to rebuild, first
    //     'player'
    //
    // and tools/live-gate's hot-reload step PASSED on both, including its
    // "every carried entity was put back" assertion. That assertion compared
    // status.reload.restored.entities with status.reload.saved.entities, and
    // RestoreAfterReload set LastRestored to the list ReloadStore.Take handed
    // over -- one line ABOVE the walk that rebuilds them. The two numbers were
    // the same number, so the check could not fail.
    //
    // The save side already carried the identical fix and a comment saying why
    // ("counting the offer rather than the deposit"). These are the restore
    // side's.
    //-------------------------------------------------------------------------

    /// <summary>
    /// A record whose class the new build does not have is left out of
    /// <see cref="ScriptedEntityRegistry.LastRestored"/>.
    /// </summary>
    [Fact]
    public void ARecordThatCouldNotBeRebuiltIsNotCountedAsRestored()
    {
        var reloaded = TwoRecordsOneOfWhichCannotBeRebuilt(0x0004_0021u);

        Assert.Single(reloaded.LastRestored);
        Assert.Equal("reload_fixture", reloaded.LastRestored[0].ClassName);
    }

    /// <summary>
    /// And the count that a gate reads is the count of records that came back,
    /// not of records that were offered — which is the equality that could not
    /// fail.
    /// </summary>
    [Fact]
    public void TheRestoredTallyIsSmallerThanTheSavedTallyWhenOneEntityIsDropped()
    {
        var reloaded = TwoRecordsOneOfWhichCannotBeRebuilt(0x0004_0022u);

        var restored = SavedEntityCodec.Measure(reloaded.LastRestored, null, null, 0);

        Assert.Equal(1, restored.Entities);
        Assert.NotEqual(2, restored.Entities);
    }

    /// <summary>The failure is counted…</summary>
    [Fact]
    public void ARestoreCountsTheRecordsItCouldNotRebuild()
    {
        var reloaded = TwoRecordsOneOfWhichCannotBeRebuilt(0x0004_0023u);

        Assert.Equal(1, reloaded.LastRestoreFailures);
    }

    /// <summary>…and named, because "one failed" sends nobody anywhere.</summary>
    [Fact]
    public void ARestoreNamesTheFirstRecordItCouldNotRebuild()
    {
        var reloaded = TwoRecordsOneOfWhichCannotBeRebuilt(0x0004_0024u);

        Assert.Equal("reload_absent_class", reloaded.LastRestoreFirstFailure);
    }

    /// <summary>
    /// And it reaches <see cref="ReloadStore"/>, which is the only object that
    /// outlives the module the restore ran in — so the dev API can still answer
    /// after the assembly that knew is gone.
    /// </summary>
    [Fact]
    public void TheFailureReachesTheStoreWhereAGateCanReadIt()
    {
        TwoRecordsOneOfWhichCannotBeRebuilt(0x0004_0025u);

        var (count, first) = _reload.RestoreFailures;

        Assert.Equal(1, count);
        Assert.Equal("reload_absent_class", first);
    }

    /// <summary>
    /// A reload that rebuilt everything reports no failures — so the number
    /// above is a signal and not a constant.
    /// </summary>
    [Fact]
    public void ARestoreThatRebuiltEverythingReportsNoFailures()
    {
        _reload.Clear();
        _reload.NoteRestoreFailures(99, "stale");

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ReloadStateTests).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(0x0004_0026u);
        registry.KeyValue(handle, "reload_fixture", "speed", "7");
        registry.SaveForReload(_reload);

        var reloaded = new ScriptedEntityRegistry(TestEngines.Server());
        reloaded.RegisterAll(typeof(ReloadStateTests).Assembly);
        reloaded.Services = Services();

        Assert.Equal(1, reloaded.RestoreAfterReload(_reload));
        Assert.Equal(0, reloaded.LastRestoreFailures);
        Assert.Null(reloaded.LastRestoreFirstFailure);
        Assert.Equal((0, null), _reload.RestoreFailures);
    }

    /// <summary>
    /// One record's failure is not the reload's: the other one still comes
    /// back, and answers.
    /// </summary>
    [Fact]
    public void TheEntityThatCouldBeRebuiltStillAnswersAfterOneCouldNot()
    {
        var handle = new EntityHandle(0x0004_0027u);
        var reloaded = TwoRecordsOneOfWhichCannotBeRebuilt(handle.Value);

        Assert.True(reloaded.Input(handle, "Nudge", "", 0f, EntityHandle.None));
        Assert.Equal(1, ((ReloadFixtureEntity)reloaded.Get(handle)!).Nudges);
    }

    /// <summary>
    /// Saves one real entity and one record naming a class this build does not
    /// register, then reloads. The second record takes
    /// <c>EnsureInstance</c>'s "no ScriptedEntity class" arm, which is the
    /// SAME OUTCOME as the live failure — an entity standing in the world with
    /// no managed half — reached without needing an engine to refuse a blob.
    /// </summary>
    private ScriptedEntityRegistry TwoRecordsOneOfWhichCannotBeRebuilt(uint handleValue)
    {
        _reload.Clear();
        _reload.NoteRestoreFailures(0, null);

        var registry = new ScriptedEntityRegistry(TestEngines.Server());
        registry.RegisterAll(typeof(ReloadStateTests).Assembly);
        registry.Services = Services();

        var handle = new EntityHandle(handleValue);
        registry.KeyValue(handle, "reload_fixture", "speed", "512.5");

        Assert.Equal(1, registry.SaveForReload(_reload));

        // The second record, put in by hand. It has to go in AFTER the real
        // one so that "first" names it only if the walk really reached it.
        ReloadCarrier.Put(_reload,
                        new SavedEntity(handleValue + 1u, "reload_absent_class", ""));

        Assert.Equal(2, ReloadCarrier.Count(_reload));

        var reloaded = new ScriptedEntityRegistry(TestEngines.Server());
        reloaded.RegisterAll(typeof(ReloadStateTests).Assembly);
        reloaded.Services = Services();

        Assert.Equal(1, reloaded.RestoreAfterReload(_reload));

        return reloaded;
    }
}
