using System;
using System.Collections.Generic;

using SourceSharp.Abi;

using Xunit;

namespace SourceSharp.Tests;

//=============================================================================
// THE THREE TIERS OF SAVE AND RESTORE.
//
//   AUTOMATIC  what the class already declares -- [ScriptVars] map keys,
//              [NetField] networked values, outputs, and the directory's I/O.
//              Nothing to write, and a new class cannot forget.
//   MARKED     a plain field or property carrying [SaveField].
//   CUSTOM     ISaveCustom, for state that is not a field at all.
//
// One fact per behaviour. The automatic tier's own round trip is
// ReloadStateTests' subject and is not restated here; what these cover is the
// two tiers that did not exist, and the boundary between them.
//=============================================================================

/// <summary>A plain class handed over whole by the CUSTOM tier.</summary>
public sealed class TierBag
{
    public int Count;
    public string Label = "stock";
    public Vector Where;
    public EntityHandle Who = EntityHandle.None;
    public List<int> Legs = new();
}

/// <summary>What that class looks like in a build that dropped and added a member.</summary>
public sealed class TierBagReshaped
{
    public int Count;

    /// <summary>New in this build. Never saved, so it keeps its initialiser.</summary>
    public string Added = "fresh";
}

/// <summary>A class the graph writer must refuse: no parameterless constructor.</summary>
public sealed class TierNoDefaultCtor
{
    public TierNoDefaultCtor(int n) => Count = n;

    public int Count;
}

/// <summary>A class the graph writer must refuse: it holds a delegate.</summary>
public sealed class TierHoldsADelegate
{
    public Action? Do;
}

/// <summary>A class the graph writer must refuse: it can point at itself.</summary>
public sealed class TierCycles
{
    public int Count;
    public TierCycles? Next;
}

/// <summary>
/// An entity exercising all three tiers at once, so a test can show one tier
/// working while another is untouched.
/// </summary>
[ScriptedEntity("tier_fixture")]
public sealed class TierFixtureEntity : ScriptedEntity, ISaveCustom
{
    /// <summary>MARKED, on a FIELD.</summary>
    [SaveField] public int Legs;

    /// <summary>MARKED, on a PROPERTY — the attribute targets both.</summary>
    [SaveField] public string Note { get; set; } = "stock";

    /// <summary>MARKED, under a key that is not the member's name.</summary>
    [SaveField("mood")] public TierMood Feeling = TierMood.Calm;

    /// <summary>MARKED, and get-only: saved for a reader, never written back.</summary>
    [SaveField] public int Doubled => Legs * 2;

    /// <summary>Not marked at all. The control: it must NOT come back.</summary>
    public int Unmarked;

    /// <summary>CUSTOM: an object the declarations cannot describe.</summary>
    public TierBag Bag = new();

    public void OnSave(ISaveWriter writer)
    {
        writer.WriteObject("bag", Bag);
        writer.AddRow().Set("n", 1).Set("s", "one");
        writer.AddRow().Set("n", 2).Set("s", "two");
    }

    public void OnRestore(ISaveReader reader)
    {
        Bag = reader.ReadObject<TierBag>("bag") ?? new TierBag();
        RestoredRows = reader.Rows.Count;
        FirstRow = reader.Rows.Count > 0 ? reader.Rows[0].GetString("s") : string.Empty;
    }

    public int RestoredRows;
    public string FirstRow = string.Empty;
}

public enum TierMood
{
    Calm = 0,
    Cross = 1,
}

/// <summary>A class that marks a member the automatic tier already carries.</summary>
[ScriptedEntity("tier_double_marked")]
public sealed class TierDoubleMarkedEntity : ScriptedEntity
{
    /// <summary>
    /// Marked twice over: a networked field is already carried by the automatic
    /// tier, so this is the combination the catalog refuses. Saved and restored
    /// twice in an order nobody chose, and the second write wins.
    /// </summary>
    [NetField][SaveField] public int Ticks;
}

/// <summary>A class whose two marked members claim one key.</summary>
[ScriptedEntity("tier_key_clash")]
public sealed class TierKeyClashEntity : ScriptedEntity
{
    [SaveField("same")] public int First;
    [SaveField("same")] public int Second;
}

public class SaveTierTests
{
    // This class's own side state (statics to zero): xUnit builds one
    // instance per fact, so no fact sees another's.
    private readonly ReloadStore _reload = new(Side.Server);
    private readonly ReloadStore _clientReload = new(Side.Client);

    private static TierFixtureEntity Fixture()
    {
        var entity = new TierFixtureEntity { ClassName = "tier_fixture" };
        entity.BindAreas(TestServices.Services());
        entity.VarSets = Array.Empty<ScriptedVarSet>();
        entity.Outputs = Array.Empty<Output>();
        return entity;
    }

    private static SavedEntity RoundTrip(ScriptedEntity from, ScriptedEntity into)
    {
        var record = new SavedEntity(7u, from.ClassName, "fixture");
        from.SaveState(record);
        into.LoadState(record);
        return record;
    }

    //-- the MARKED tier ------------------------------------------------------

    [Fact]
    public void AMarkedFieldSurvivesTheReload()
    {
        var before = Fixture();
        before.Legs = 41;

        var after = Fixture();
        RoundTrip(before, after);

        Assert.Equal(41, after.Legs);
    }

    [Fact]
    public void AMarkedPropertySurvivesTheReload()
    {
        var before = Fixture();
        before.Note = "the mapper's";

        var after = Fixture();
        RoundTrip(before, after);

        Assert.Equal("the mapper's", after.Note);
    }

    /// <summary>
    /// THE CONTROL, and without it every fact above could be passing because
    /// the fixture is being copied wholesale rather than because the attribute
    /// does anything.
    /// </summary>
    [Fact]
    public void AnUnmarkedFieldDoesNotSurviveTheReload()
    {
        var before = Fixture();
        before.Unmarked = 41;

        var after = Fixture();
        RoundTrip(before, after);

        Assert.Equal(0, after.Unmarked);
    }

    [Fact]
    public void AMarkedMemberIsCarriedUnderItsDeclaredKey()
    {
        var before = Fixture();
        before.Feeling = TierMood.Cross;

        var after = Fixture();
        SavedEntity record = RoundTrip(before, after);

        Assert.True(record.Has("mood"));
        Assert.False(record.Has(nameof(TierFixtureEntity.Feeling)));
        Assert.Equal(TierMood.Cross, after.Feeling);
    }

    /// <summary>
    /// A get-only marked member is SAVED — a computed value is worth having in
    /// a record for a reader — and is not written back, because it cannot be.
    /// </summary>
    [Fact]
    public void AGetOnlyMarkedMemberIsSavedAndNotRestored()
    {
        var before = Fixture();
        before.Legs = 21;

        var record = new SavedEntity(7u, "tier_fixture", "fixture");
        before.SaveState(record);

        Assert.Equal("42", record.GetString("Doubled"));

        var after = Fixture();
        after.LoadState(record);

        // Legs came back, so Doubled recomputes to the same thing -- but it was
        // never written, which is what CanWrite decides.
        Assert.Equal(42, after.Doubled);
        Assert.False(SaveFieldOf(typeof(TierFixtureEntity), "Doubled").CanWrite);
    }

    [Fact]
    public void TheCatalogFindsBothAFieldAndAProperty()
    {
        SaveFieldInfo[] marked = SaveFieldCatalog.Build(typeof(TierFixtureEntity), new RecordingLog());

        Assert.Contains(marked, m => m.MemberName == nameof(TierFixtureEntity.Legs));
        Assert.Contains(marked, m => m.MemberName == nameof(TierFixtureEntity.Note));
    }

    /// <summary>
    /// A backing field carries the attribute the property was given, so a naive
    /// walk would find the value twice and carry it under two keys.
    /// </summary>
    [Fact]
    public void AMarkedPropertyIsFoundOnceAndNotAlsoAsItsBackingField()
    {
        SaveFieldInfo[] marked = SaveFieldCatalog.Build(typeof(TierFixtureEntity), new RecordingLog());

        Assert.Single(marked, m => m.Key == nameof(TierFixtureEntity.Note));
    }

    [Fact]
    public void AMemberTheAutomaticTierAlreadyCarriesIsRefused()
    {
        SaveFieldInfo[] marked = SaveFieldCatalog.Build(typeof(TierDoubleMarkedEntity), new RecordingLog());

        Assert.DoesNotContain(marked, m => m.MemberName == "Ticks");
    }

    [Fact]
    public void TwoMarkedMembersUnderOneKeyLeaveOnlyOne()
    {
        SaveFieldInfo[] marked = SaveFieldCatalog.Build(typeof(TierKeyClashEntity), new RecordingLog());

        Assert.Single(marked, m => m.Key == "same");
    }

    //-- the CUSTOM tier ------------------------------------------------------

    [Fact]
    public void ACustomObjectSurvivesTheReload()
    {
        var before = Fixture();
        before.Bag = new TierBag
        {
            Count = 9,
            Label = "carried",
            Where = new Vector(1f, 2f, 3f),
            Legs = new List<int> { 4, 5, 6 },
        };

        var after = Fixture();
        RoundTrip(before, after);

        Assert.Equal(9, after.Bag.Count);
        Assert.Equal("carried", after.Bag.Label);
        Assert.Equal(3f, after.Bag.Where.Z);
        Assert.Equal(new[] { 4, 5, 6 }, after.Bag.Legs);
    }

    /// <summary>
    /// A FRESH instance, never the one that was saved. Keeping the object would
    /// keep the assembly it came from alive — and would hand back an instance of
    /// the class the reload exists to replace.
    /// </summary>
    [Fact]
    public void ACustomObjectComesBackAsAFreshInstance()
    {
        var before = Fixture();
        before.Bag = new TierBag { Count = 9 };

        var after = Fixture();
        RoundTrip(before, after);

        Assert.NotSame(before.Bag, after.Bag);
    }

    [Fact]
    public void ACustomRecordsRowsSurviveInOrder()
    {
        var before = Fixture();
        var after = Fixture();
        RoundTrip(before, after);

        Assert.Equal(2, after.RestoredRows);
        Assert.Equal("one", after.FirstRow);
    }

    /// <summary>
    /// The custom record rides the LOCAL store, which is what makes a savegame
    /// carry it with no format change — <c>SavedEntityCodec</c> already writes
    /// that store.
    /// </summary>
    [Fact]
    public void ACustomRecordIsCarriedInsideTheLocalStore()
    {
        var before = Fixture();

        var record = new SavedEntity(7u, "tier_fixture", "fixture");
        before.SaveState(record);

        Assert.Contains(record.Local, pair => pair.Key.StartsWith("custom.", StringComparison.Ordinal));
    }

    //-- the object graph's stated contract -----------------------------------

    [Fact]
    public void TheGraphRefusesADelegateAndWritesNothing()
    {
        var holding = new TierHoldsADelegate { Do = () => { } };

        Assert.Null(SaveGraph.Write(holding, "test", "k", new RecordingLog()));
    }

    [Fact]
    public void TheGraphRefusesACycleAndWritesNothing()
    {
        var a = new TierCycles { Count = 1 };
        a.Next = a;

        Assert.Null(SaveGraph.Write(a, "test", "k", new RecordingLog()));
    }

    [Fact]
    public void TheGraphRefusesATypeWithNoParameterlessConstructor()
    {
        Assert.Null(SaveGraph.Write(new TierNoDefaultCtor(3), "test", "k", new RecordingLog()));
    }

    [Fact]
    public void TheGraphRefusesAGraphDeeperThanItsLimit()
    {
        var deep = new TierCycles();
        TierCycles tail = deep;

        for (int i = 0; i < SaveGraph.MaxDepth + 2; i++)
            tail = tail.Next = new TierCycles { Count = i };

        Assert.Null(SaveGraph.Write(deep, "test", "k", new RecordingLog()));
    }

    /// <summary>
    /// The hot-reload case the whole tier exists for: the class CHANGED between
    /// the build that saved and the build that reads. A member that has gone is
    /// dropped; a member that is new keeps its own initialiser.
    /// </summary>
    [Fact]
    public void AMemberTheNewBuildDroppedIsNotCarried()
    {
        string text = SaveGraph.Write(new TierBag { Count = 9, Label = "gone" }, "test", "k", new RecordingLog())!;

        var read = (TierBagReshaped?)SaveGraph.Read(text, typeof(TierBagReshaped), "test", "k", TestServices.UnattachedHandles(), new RecordingLog());

        Assert.NotNull(read);
        Assert.Equal(9, read!.Count);
    }

    [Fact]
    public void AMemberTheOldBuildNeverWroteKeepsItsInitialiser()
    {
        string text = SaveGraph.Write(new TierBag { Count = 9 }, "test", "k", new RecordingLog())!;

        var read = (TierBagReshaped?)SaveGraph.Read(text, typeof(TierBagReshaped), "test", "k", TestServices.UnattachedHandles(), new RecordingLog());

        Assert.Equal("fresh", read!.Added);
    }

    /// <summary>
    /// A value goes across as one LINE, so a newline inside one would become a
    /// second line and every member after it would read from the wrong key.
    /// </summary>
    [Fact]
    public void AValueHoldingANewlineDoesNotDerailTheMembersAfterIt()
    {
        var bag = new TierBag { Label = "two\nlines", Count = 9 };

        string text = SaveGraph.Write(bag, "test", "k", new RecordingLog())!;
        var read = (TierBag?)SaveGraph.Read(text, typeof(TierBag), "test", "k", TestServices.UnattachedHandles(), new RecordingLog());

        Assert.Equal("two\nlines", read!.Label);
        Assert.Equal(9, read.Count);
    }

    [Fact]
    public void AValueHoldingAnEqualsSignSurvives()
    {
        var bag = new TierBag { Label = "a=b" };

        string text = SaveGraph.Write(bag, "test", "k", new RecordingLog())!;
        var read = (TierBag?)SaveGraph.Read(text, typeof(TierBag), "test", "k", TestServices.UnattachedHandles(), new RecordingLog());

        Assert.Equal("a=b", read!.Label);
    }

    //-- handles, which is the sharp edge -------------------------------------

    [Fact]
    public void AHandleInsideACustomObjectSurvives()
    {
        var bag = new TierBag { Who = new EntityHandle(0x0004_0011u) };

        string text = SaveGraph.Write(bag, "test", "k", new RecordingLog())!;
        var read = (TierBag?)SaveGraph.Read(text, typeof(TierBag), "test", "k", TestServices.UnattachedHandles(), new RecordingLog());

        Assert.Equal(0x0004_0011u, read!.Who.Value);
    }

    /// <summary>
    /// <b>ENTRY ZERO IS A REAL SLOT.</b> A handle that could not be restored
    /// must read as <c>INVALID_EHANDLE_INDEX</c> and never as the raw
    /// handle 0, which names worldspawn.
    /// </summary>
    [Fact]
    public void AHandleThatCouldNotBeReadIsNoneAndNotEntryZero()
    {
        var record = new ReloadRecord("test");
        var access = new SaveRecordAccess(record, "test", new RecordingLog());

        Assert.Equal(EntityHandle.None, access.ReadHandle("never-written"));
    }

    /// <summary>
    /// A restored handle is re-checked against the engine the SavedHandles was
    /// built over, never a shared one (plan_abi_hotreload D8): the same handle
    /// survives on a side whose engine says it lives and is dropped on one
    /// whose engine says it does not.
    /// </summary>
    [Fact]
    public void EachSidesHandlesAskTheirOwnEngine()
    {
        var handle = new EntityHandle(0x0003_0021u);

        var alive = TestServices.Fake().Returns("Host.IsInitialized", true).Returns("Entities.IsValid", true);
        var gone = TestServices.Fake().Returns("Host.IsInitialized", true).Returns("Entities.IsValid", false);

        Assert.Equal(handle, new SavedHandles(alive, alive).Resolve(handle));
        Assert.Equal(EntityHandle.None, new SavedHandles(gone, gone).Resolve(handle));
        Assert.Equal(1, alive.CountOf("Entities.IsValid"));
        Assert.Equal(1, gone.CountOf("Entities.IsValid"));
    }

    [Fact]
    public void TheInvalidHandleStaysInvalidThroughResolve()
        => Assert.Equal(EntityHandle.None, TestServices.UnattachedHandles().Resolve(EntityHandle.None));

    //-- the record and its flattening ----------------------------------------

    [Fact]
    public void AFlattenedRecordRoundTripsItsScalars()
    {
        var record = new ReloadRecord("owner");
        record.Set("n", 7);
        record.Set("s", "text");

        var bag = new Dictionary<string, string>();
        ReloadRecordText.Flatten(record, "p.", (k, v) => bag[k] = v);

        ReloadRecord back = ReloadRecordText.Unflatten("owner", "p.", bag);

        Assert.Equal(7, back.GetInt("n"));
        Assert.Equal("text", back.GetString("s"));
    }

    [Fact]
    public void AFlattenedRecordKeepsItsRowsInOrder()
    {
        var record = new ReloadRecord("owner");
        for (int i = 0; i < 12; i++)
            record.AddRow().Set("i", i);

        var bag = new Dictionary<string, string>();
        ReloadRecordText.Flatten(record, "p.", (k, v) => bag[k] = v);

        ReloadRecord back = ReloadRecordText.Unflatten("owner", "p.", bag);

        Assert.Equal(12, back.Rows.Count);
        for (int i = 0; i < 12; i++)
            Assert.Equal(i, back.Rows[i].GetInt("i"));
    }

    [Fact]
    public void AFlattenedRecordDoesNotPickUpAnotherPrefixesKeys()
    {
        var bag = new Dictionary<string, string>
        {
            ["mine.s.n"] = "1",
            ["theirs.s.n"] = "2",
        };

        Assert.Equal(1, ReloadRecordText.Unflatten("o", "mine.", bag).GetInt("n"));
    }

    //-- the readout ----------------------------------------------------------

    [Fact]
    public void MeasureCountsEntitiesClassesAndFields()
    {
        var one = new SavedEntity(1u, "ss_pickup", "a");
        one.Set("k", 1);
        var two = new SavedEntity(2u, "ss_pickup", "b");
        two.Set("k", 2);
        var three = new SavedEntity(3u, "ss_beam", "c");
        three.Set("k", 3);

        var tally = SavedEntityCodec.Measure(new[] { one, two, three }, parts: null, io: null,
                                        shellLess: 0);

        Assert.Equal(3, tally.Entities);
        Assert.Equal(2, tally.Classes);
        Assert.Equal(3, tally.Fields);
    }

    [Fact]
    public void MeasureCountsAPartsFieldsAndBytes()
    {
        var record = new ReloadRecord("owner");
        record.Set("a", "1");
        record.AddRow().Set("b", "2");

        var parts = new Dictionary<string, ReloadRecord> { ["owner"] = record };

        var tally = SavedEntityCodec.Measure(Array.Empty<SavedEntity>(), parts, io: null, shellLess: 0);

        Assert.Equal(1, tally.Parts);
        Assert.Equal(2, tally.PartFields);
        Assert.True(tally.PartBytes > 0, "a record holding two keys measures more than nothing");
    }

    /// <summary>
    /// A readout that cannot read zero is not a readout. This is the reading
    /// before any reload has happened, and it is what makes "the think schedule
    /// carried 0 bookings" a visible answer rather than an absence.
    /// </summary>
    [Fact]
    public void AnEmptyMeasureIsAllZeroes()
    {
        var tally = SavedEntityCodec.Measure(Array.Empty<SavedEntity>(), parts: null, io: null,
                                        shellLess: 0);

        Assert.Equal(0, tally.Entities);
        Assert.Equal(0, tally.Classes);
        Assert.Equal(0, tally.Fields);
        Assert.Equal(0, tally.Bytes);
        Assert.Equal(0, tally.Parts);
    }

    //-- the store ------------------------------------------------------------

    [Fact]
    public void TakingThePartsEmptiesThem()
    {
        _clientReload.Clear();
        ReloadCarrier.PutPart(_clientReload, Filled("solo"));

        Assert.Equal(1, ReloadCarrier.PartCount(_clientReload));
        Assert.Single(ReloadCarrier.TakeParts(_clientReload));
        Assert.Equal(0, ReloadCarrier.PartCount(_clientReload));
    }

    [Fact]
    public void AnEmptyPartIsNotStored()
    {
        _clientReload.Clear();
        ReloadCarrier.PutPart(_clientReload, new ReloadRecord("silent"));

        Assert.Equal(0, ReloadCarrier.PartCount(_clientReload));
    }

    /// <summary>
    /// A record kept for a restore that will never come is replayed into
    /// whatever the NEXT reload finds — which for <c>Pickups</c> means telling
    /// it the level's kits are already placed, and the map comes up with none.
    /// </summary>
    [Fact]
    public void ClearingASideDropsItsParts()
    {
        _clientReload.Clear();
        ReloadCarrier.PutPart(_clientReload, Filled("doomed"));
        _clientReload.Clear();

        Assert.Equal(0, ReloadCarrier.PartCount(_clientReload));
    }

    [Fact]
    public void ThePartsOfOneSideAreNotTheOthers()
    {
        _clientReload.Clear();
        _reload.Clear();
        ReloadCarrier.PutPart(_clientReload, Filled("clientside"));

        Assert.Equal(0, ReloadCarrier.PartCount(_reload));

        ReloadCarrier.TakeParts(_clientReload);
    }

    private static ReloadRecord Filled(string owner)
    {
        var record = new ReloadRecord(owner);
        record.Set("k", 1);
        return record;
    }

    private static SaveFieldInfo SaveFieldOf(Type type, string member)
    {
        foreach (SaveFieldInfo info in SaveFieldCatalog.Build(type, new RecordingLog()))
        {
            if (info.MemberName == member)
                return info;
        }

        throw new InvalidOperationException($"{type.Name} has no marked member '{member}'");
    }
}
