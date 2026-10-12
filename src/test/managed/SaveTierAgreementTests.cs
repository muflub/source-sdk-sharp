extern alias client;
using SaveTiers = client::SourceSharp.Abi.SaveTiers;
// SaveTiers is the client's, so its records and handles are the client's copy
// of the shared save vocabulary (plan_side_isolation P7); unqualified, the
// names are the server's, which the entity base's walk writes.
using ClientSavedEntity = client::SourceSharp.Abi.SavedEntity;
using ClientSavedHandles = client::SourceSharp.Abi.SavedHandles;
using ClientSaveCustom = client::SourceSharp.Abi.ISaveCustom;
using ClientSaveWriter = client::SourceSharp.Abi.ISaveWriter;
using ClientSaveReader = client::SourceSharp.Abi.ISaveReader;
using System;
using System.Collections.Generic;
using System.Linq;
using SourceSharp.Abi;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// THE TWO WALKS OVER THE MARKED AND CUSTOM TIERS MUST AGREE, and this is the
/// only thing that can notice when they stop.
///
/// <para>
/// <b>WHY THERE ARE TWO.</b> <c>ScriptedEntity.SaveDeclaredState</c> walks all
/// three tiers, and it is the SERVER's entity base;
/// <c>ClientStandaloneEntity.SaveDeclaredState</c> walks only the automatic one,
/// so <c>[SaveField]</c> on a client class is silence. Step 6e needed the marked
/// and custom tiers on a CLIENT class (<c>ClientSsRagdoll</c>) and owns neither
/// base — <c>ScriptedEntity.cs</c> is the <c>Entity</c> base and
/// <c>ClientStandaloneEntity.cs</c> is step 6b's — so the walk landed in
/// <see cref="SaveTiers"/>, where both sides can reach it, and
/// <c>.claude/entity-port/requests/6e.md</c> asks for the two bases to be
/// pointed at it.
/// </para>
///
/// <para>
/// <b>UNTIL THAT HAPPENS THERE ARE TWO IMPLEMENTATIONS, AND TWO SPELLINGS OF
/// ONE THING IS DRIFT WAITING TO HAPPEN.</b> The facts below are what makes
/// that a claim that can fail: the same shape goes through both walks and the
/// records are compared KEY BY KEY AND VALUE BY VALUE. A member one walk visits
/// and the other does not is a difference in the key set; a member both visit
/// and spell differently is a difference in the values. Neither can hide.
/// </para>
/// </summary>
public class SaveTierAgreementTests
{
    /// <summary>
    /// The subject: a class with one of everything the marked tier handles, and
    /// a custom record besides.
    ///
    /// <para>
    /// It derives from <see cref="ScriptedEntity"/> because one of the two walks
    /// is a private method of that class and can only be reached through it. The
    /// OTHER walk takes any object, so it is handed the same instance — which is
    /// what makes this a comparison of two walks over ONE shape rather than of
    /// two shapes.
    /// </para>
    ///
    /// <para>
    /// Since the shared source compiles into each side (plan_side_isolation P7)
    /// each walk asks for its OWN side's <c>ISaveCustom</c>, so the one shape
    /// implements both, with one body. <c>[SaveField]</c> is Shared.Interop's
    /// and so already one type for both walks.
    /// </para>
    /// </summary>
    [ScriptedEntity("tier_agreement")]
    public sealed class AgreementEntity : ScriptedEntity, ISaveCustom, ClientSaveCustom
    {
        [SaveField] public int Legs = 4;
        [SaveField] public string Note { get; set; } = "stock";
        [SaveField] public bool Awake = true;
        [SaveField] public float Weight = 2.5f;
        [SaveField] public Vector Where = new(1f, 2f, 3f);
        [SaveField] public QAngle Facing = new(10f, 20f, 30f);
        [SaveField("mood")] public AgreementMood Feeling = AgreementMood.Cross;
        [SaveField] public EntityHandle Friend = new(41u);

        /// <summary>Get-only: saved for a reader, never written back.</summary>
        [SaveField] public int Doubled => Legs * 2;

        /// <summary>Not marked. The control — neither walk may carry it.</summary>
        public int Unmarked = 9;

        public void OnSave(ISaveWriter writer)
        {
            writer.Write("n", 7);
            writer.Write("s", "seven");
            writer.AddRow().Set("k", 1);
        }

        public void OnRestore(ISaveReader reader)
        {
            RestoredN = reader.ReadInt("n");
            RestoredS = reader.ReadString("s");
            RestoredRows = reader.Rows.Count;
        }

        // The same record, for the client's walk: one body, its side's types.
        void ClientSaveCustom.OnSave(ClientSaveWriter writer)
        {
            writer.Write("n", 7);
            writer.Write("s", "seven");
            writer.AddRow().Set("k", 1);
        }

        void ClientSaveCustom.OnRestore(ClientSaveReader reader)
        {
            RestoredN = reader.ReadInt("n");
            RestoredS = reader.ReadString("s");
            RestoredRows = reader.Rows.Count;
        }

        public int RestoredN;
        public string RestoredS = string.Empty;
        public int RestoredRows;
    }

    public enum AgreementMood
    {
        Calm = 0,
        Cross = 1,
    }

    private static AgreementEntity Subject()
    {
        var entity = new AgreementEntity { ClassName = "tier_agreement" };
        entity.BindAreas(TestServices.Services());

        // The automatic tier's three inputs, emptied: this file is about the
        // marked and custom tiers, and a map key or an output in the record
        // would be a difference between the two walks that is not one of theirs.
        entity.VarSets = Array.Empty<ScriptedVarSet>();
        entity.Outputs = Array.Empty<Output>();

        return entity;
    }

    /// <summary>What the SERVER base's walk puts in a record.</summary>
    private static SavedEntity ThroughTheEntityBase(AgreementEntity subject)
    {
        var record = new SavedEntity(7u, subject.ClassName, "fixture");

        subject.SaveState(record);

        return record;
    }

    /// <summary>What <see cref="SaveTiers"/> puts in a record.</summary>
    private static ClientSavedEntity ThroughSaveTiers(AgreementEntity subject)
    {
        var record = new ClientSavedEntity(7u, subject.ClassName, "fixture");

        SaveTiers.SaveMarked(subject, record, "fixture", new RecordingLog());
        SaveTiers.SaveCustom(subject, record, "fixture", new RecordingLog());

        return record;
    }

    //-------------------------------------------------------------------------

    /// <summary>
    /// THE FIXTURE IS NOT EMPTY. Every comparison below is between two
    /// dictionaries, and two empty dictionaries are equal.
    /// </summary>
    [Fact]
    public void TheSubjectActuallyCarriesSomething()
    {
        ClientSavedEntity mine = ThroughSaveTiers(Subject());

        Assert.True(mine.Local.Count >= 10,
                    $"the fixture carried only {mine.Local.Count} keys");
    }

    [Fact]
    public void TheTwoWalksVisitTheSameMEMBERS()
    {
        List<string> theirs = ThroughTheEntityBase(Subject()).Local.Keys.OrderBy(k => k).ToList();
        List<string> mine = ThroughSaveTiers(Subject()).Local.Keys.OrderBy(k => k).ToList();

        Assert.Equal(theirs, mine);
    }

    [Fact]
    public void AndSPELLEveryValueTheSameWay()
    {
        IReadOnlyDictionary<string, string> theirs = ThroughTheEntityBase(Subject()).Local;
        IReadOnlyDictionary<string, string> mine = ThroughSaveTiers(Subject()).Local;

        foreach (KeyValuePair<string, string> pair in theirs)
        {
            Assert.True(mine.TryGetValue(pair.Key, out string? ours),
                        $"SaveTiers did not carry '{pair.Key}'");
            Assert.Equal(pair.Value, ours);
        }
    }

    /// <summary>
    /// AND NEITHER CARRIES WHAT IS NOT MARKED. The control: a field with no
    /// attribute must be absent from both, or "the two agree" would be true of
    /// two walks that both carried everything.
    /// </summary>
    [Fact]
    public void NeitherWalkCarriesAnUnmarkedField()
    {
        Assert.False(ThroughTheEntityBase(Subject()).Has(nameof(AgreementEntity.Unmarked)));
        Assert.False(ThroughSaveTiers(Subject()).Has(nameof(AgreementEntity.Unmarked)));
    }

    /// <summary>
    /// THE CUSTOM RECORD LANDS UNDER THE SAME PREFIX, which matters beyond
    /// tidiness: a record written by one walk is read back by the other whenever
    /// a class moves between the two sides, and a different prefix would lose it
    /// silently.
    /// </summary>
    [Fact]
    public void TheCustomRecordUsesOnePrefix()
    {
        ClientSavedEntity mine = ThroughSaveTiers(Subject());

        Assert.Contains(mine.Local.Keys,
                        k => k.StartsWith(SaveTiers.CustomPrefix, StringComparison.Ordinal));
        Assert.Equal("custom.", SaveTiers.CustomPrefix);
    }

    //-------------------------------------------------------------------------
    // The round trip through SaveTiers alone, for the members the ragdoll uses.
    //-------------------------------------------------------------------------

    /// <summary><see cref="TestServices"/>' unattached handles, as the client's type.</summary>
    private static ClientSavedHandles ClientHandles()
    {
        var engine = ScriptedEntityRegistry.UnattachedEngine();
        return new ClientSavedHandles(engine.Require<IEngineHost>(), engine.Require<IEngineEntities>());
    }

    private static AgreementEntity RoundTrip(AgreementEntity from)
    {
        ClientSavedEntity record = ThroughSaveTiers(from);

        var into = Subject();

        // Everything different from the class's initialisers, so a value that
        // was NOT restored reads as the initialiser rather than as the saved
        // value by coincidence.
        into.Legs = -1;
        into.Note = "wrong";
        into.Awake = false;
        into.Weight = -1f;
        into.Where = new Vector(9f, 9f, 9f);
        into.Facing = new QAngle(9f, 9f, 9f);
        into.Feeling = AgreementMood.Calm;
        into.Friend = EntityHandle.None;

        SaveTiers.LoadMarked(into, record, "fixture", ClientHandles(), new RecordingLog());
        SaveTiers.LoadCustom(into, record, "fixture", ClientHandles(), new RecordingLog());

        return into;
    }

    [Fact]
    public void AnIntSurvivesTheRoundTrip()
        => Assert.Equal(4, RoundTrip(Subject()).Legs);

    [Fact]
    public void AStringSurvivesTheRoundTrip()
        => Assert.Equal("stock", RoundTrip(Subject()).Note);

    [Fact]
    public void ABoolSurvivesTheRoundTrip()
        => Assert.True(RoundTrip(Subject()).Awake);

    [Fact]
    public void AFloatSurvivesTheRoundTrip()
        => Assert.Equal(2.5f, RoundTrip(Subject()).Weight, 3);

    [Fact]
    public void AVectorSurvivesTheRoundTrip()
        => Assert.Equal(2f, RoundTrip(Subject()).Where.Y, 3);

    [Fact]
    public void AQAngleSurvivesTheRoundTrip()
        => Assert.Equal(20f, RoundTrip(Subject()).Facing.Yaw, 3);

    [Fact]
    public void AnEnumUnderItsOwnKeySurvivesTheRoundTrip()
        => Assert.Equal(AgreementMood.Cross, RoundTrip(Subject()).Feeling);

    /// <summary>
    /// A HANDLE SURVIVES THE ROUND TRIP. Both walks spelled one through the
    /// map-key formatter, which writes <c>#41</c> (its entry, for a reader), and
    /// neither parser reads that back, so every marked handle came back holding
    /// whatever the new instance started with. Found by lane side-audit-2
    /// (2026-10-02) when the client's first marked handle went in.
    /// </summary>
    [Fact]
    public void AHandleSurvivesTheRoundTrip()
    {
        var from = Subject();
        from.Friend = new EntityHandle(0x0005_8029u);

        Assert.True(RoundTrip(from).Friend.Value == 0x0005_8029u,
            $"the handle comes back as written (0x{RoundTrip(from).Friend.Value:X8})");
    }

    /// <summary>
    /// AND THE INVALID HANDLE COMES BACK INVALID, never as entry zero: its value
    /// does not fit an int, which is how a parser reading ints would lose it.
    /// </summary>
    [Fact]
    public void TheInvalidHandleSurvivesTheRoundTripAsNone()
    {
        var from = Subject();
        from.Friend = EntityHandle.None;

        ClientSavedEntity record = ThroughSaveTiers(from);
        var into = Subject();
        into.Friend = new EntityHandle(5u);

        SaveTiers.LoadMarked(into, record, "fixture", ClientHandles(), new RecordingLog());

        Assert.True(into.Friend.Value == EntityHandle.None.Value,
            $"None comes back None (0x{into.Friend.Value:X8})");
    }

    [Fact]
    public void TheCustomRecordSurvivesTheRoundTrip()
    {
        AgreementEntity back = RoundTrip(Subject());

        Assert.Equal(7, back.RestoredN);
        Assert.Equal("seven", back.RestoredS);
        Assert.Equal(1, back.RestoredRows);
    }

    /// <summary>
    /// A GET-ONLY MEMBER IS SAVED AND NOT RESTORED, and the class is told so
    /// rather than left to discover it. What it comes back holding is whatever
    /// this build computes — here, twice whatever Legs was restored to.
    /// </summary>
    [Fact]
    public void AGetOnlyMemberComesBackComputedAndNotCarried()
    {
        var from = Subject();
        from.Legs = 11;

        AgreementEntity back = RoundTrip(from);

        Assert.Equal(11, back.Legs);
        Assert.Equal(22, back.Doubled);
    }

    /// <summary>
    /// A KEY THAT WAS NEVER CARRIED LEAVES THE MEMBER ALONE — the ordinary case
    /// for a field somebody has just added, and the one that must NOT be a
    /// warning or a zeroing.
    /// </summary>
    [Fact]
    public void AMemberTheRecordDoesNotMentionKeepsItsInitialiser()
    {
        var record = new ClientSavedEntity(7u, "tier_agreement", "fixture");

        var into = Subject();
        into.Legs = 77;

        SaveTiers.LoadMarked(into, record, "fixture", ClientHandles(), new RecordingLog());

        Assert.Equal(77, into.Legs);
    }
}
