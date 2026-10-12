using System;

using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>CDecalEmitterSystem</c>, in C# — plan_gamedll_after.md Phase A3.
///
/// <para>
/// The script is supplied through the strict double's <c>Files.ReadGameFile</c>,
/// which is the seam the port exists to create: the engine finds the file,
/// managed code parses it.
/// </para>
/// </summary>
public class DecalEmitterTests
{
    /// <summary>
    /// A small script with every shape the real one uses: a translation block
    /// with an empty entry and one naming a group that does not exist, two
    /// groups, and a group whose single entry carries a weight.
    /// </summary>
    private const string Script = """
        // a header comment
        "TranslationData"
        {
        	"-"		""
        	"C"		"Impact.Concrete"
        	"M"		"Impact.Metal"
        	"Q"		"Impact.NotThere"
        }

        "Impact.Concrete"
        {
        	"decals/concrete/shot1_subrect"	"1"
        	"decals/concrete/shot2_subrect"	"1"
        }

        "Impact.Metal"
        {
        	"decals/metal/shot1_subrect"	"1"
        }
        """;

    /// <summary>
    /// Stubs a member that must answer a DIFFERENT value per call.
    ///
    /// One stubbed constant would make every entry share an index, and a fact
    /// asserting "the metal decal answers 13" would then be true of any entry
    /// at all -- the shape RecordingEngine.ReturnsEachCall's own doc comment
    /// records costing three tests their subject.
    /// </summary>
    private static void Sequence(RecordingEngine engine, string member, params int[] values)
    {
        int at = 0;
        engine.ReturnsEachCall(member, () => values[at < values.Length ? at++ : values.Length - 1]);
    }

    private static (DecalEmitter Decals, RecordingEngine Engine) Build(string? script = Script)
    {
        var engine = Fake();
        engine.Returns("Files.ReadGameFile", script);

        // The weighted pick draws unless the running weight is still zero, and
        // a stubbed 0 makes the draw deterministic — which is what a fact about
        // WHICH index came back needs. The one fact about the draw not
        // happening at all builds its own engine, so this stub cannot make that
        // one pass by accident.
        engine.Returns("Host.RandomFloat", 0f);

        var decals = new DecalEmitter(new RecordingLog(), engine, engine, engine);
        return (decals, engine);
    }

    /// <summary>
    /// The precache answers, in the order the entries are read: 11, 12, 13.
    /// Distinct so a fact can say WHICH entry an index came back for, which a
    /// single stubbed answer could not.
    /// </summary>
    private static (DecalEmitter Decals, RecordingEngine Engine) Loaded()
    {
        var (decals, engine) = Build();
        Sequence(engine, "Precache.PrecacheDecal", 11, 12, 13);
        ((IGameLifecycle)decals).OnLevelInitPreEntity("dm_lockdown");
        return (decals, engine);
    }

    // ---- the parse -----------------------------------------------------------

    [Fact]
    public void TheScriptIsReadThroughTheEngineAndNotOffDisk()
    {
        var (decals, engine) = Build();
        decals.Load();

        var call = engine.Last("Files.ReadGameFile");

        Assert.True(call is not null, "the emitter asks the engine's filesystem for the script");
        Assert.True((string?)call!.Value.Args[0] == "scripts/decals_subrect.txt",
            "and it asks for the path stock reads, on the engine's own search paths -- "
            + "the file is in neither the mod nor any VPK, so System.IO would find nothing");
    }

    [Fact]
    public void EveryLineOfEveryGroupBecomesAnEntry()
    {
        var (decals, _) = Build();
        decals.Load();

        Assert.True(decals.Counts.Entries == 3, "two concrete decals and one metal one");
        Assert.True(decals.Counts.Groups == 2, "TranslationData is not a decal group");
    }

    [Fact]
    public void ATranslationNamingAGroupThatDoesNotExistIsDropped()
    {
        var (decals, _) = Build();
        decals.Load();

        // Three lines in TranslationData carry a value; one of them names
        // "Impact.NotThere". Stock warns and does not insert it, and a
        // translation table that kept it would answer a group nothing can look
        // up.
        Assert.True(decals.Counts.Materials == 2,
            "only the translations whose group exists are kept");
    }

    [Fact]
    public void AnEmptyTranslationIsNotAnEntry()
    {
        var (decals, _) = Build();
        decals.Load();

        // "-" "" is the file saying "do not decal this surface". Its ABSENCE
        // from the table is what makes that the answer, so a reader that stored
        // it would translate '-' to the empty group name rather than to nothing.
        Assert.True(decals.TranslateForGameMaterial("Impact.Concrete", '-') == string.Empty,
            "the dash material decals nothing at all");
    }

    [Fact]
    public void AMissingScriptIsSurvivedAndEveryLookupThenAnswersMinusOne()
    {
        var (decals, _) = Build(script: null);
        decals.Load();

        Assert.True(decals.Counts.Entries == 0, "nothing was read");
        Assert.True(decals.IndexForName("Impact.Concrete") == -1,
            "and every lookup answers -1 rather than throwing at the first impact");
    }

    // ---- the precache --------------------------------------------------------

    [Fact]
    public void EveryEntryIsPrecachedOnTheLevelsPreEntityHalf()
    {
        var (_, engine) = Loaded();

        Assert.True(engine.CountOf("Precache.PrecacheDecal") == 3,
            "all three entries are precached, once each");
    }

    [Fact]
    public void ALookupBeforeAnyPrecacheAnswersMinusOne()
    {
        var (decals, _) = Build();
        decals.Load();

        // Stock's own initial value for precache_index. It matters that this is
        // -1 rather than 0: 0 is a real decal index, and answering it would
        // stamp whatever the engine precached first onto every impact.
        Assert.True(decals.IndexForName("Impact.Metal") == -1,
            "an entry that has not been precached has no index to give");
    }

    [Fact]
    public void APrecacheIndexIsWhatALookupAnswers()
    {
        var (decals, _) = Loaded();

        // The metal group has ONE entry, so the weighted pick cannot choose
        // between anything and the answer is that entry's index -- the third
        // precached, so 13.
        Assert.True(decals.IndexForName("Impact.Metal") == 13,
            "a one-entry group answers that entry's precache index");
    }

    [Fact]
    public void TheSecondLevelInitPrecachesAgainRatherThanKeepingTheOldIndices()
    {
        var (decals, engine) = Loaded();

        Sequence(engine, "Precache.PrecacheDecal", 21, 22, 23);
        ((IGameLifecycle)decals).OnLevelInitPreEntity("dm_lockdown");

        // The precache tables are rebuilt with the map, so an index from the
        // last map names a different material on this one -- or nothing.
        Assert.True(decals.IndexForName("Impact.Metal") == 23,
            "the second map's index replaces the first's");
    }

    [Fact]
    public void TheScriptIsReadOnceAcrossTwoMaps()
    {
        var (decals, engine) = Loaded();

        Sequence(engine, "Precache.PrecacheDecal", 21, 22, 23);
        ((IGameLifecycle)decals).OnLevelInitPreEntity("dm_lockdown");

        Assert.True(engine.CountOf("Files.ReadGameFile") == 1,
            "the file does not change between maps; the precache does");
    }

    // ---- the lookups ---------------------------------------------------------

    [Fact]
    public void AnUnknownGroupAnswersMinusOne()
    {
        var (decals, _) = Loaded();

        Assert.True(decals.IndexForName("Impact.NoSuchThing") == -1,
            "a name no group carries is not decalled");
    }

    [Fact]
    public void AnEmptyNameIsRefusedWithoutTouchingTheTable()
    {
        var (decals, _) = Loaded();

        Assert.True(decals.IndexForName("") == -1, "an empty name is stock's own first refusal");
    }

    [Fact]
    public void TheWeightedPickAsksTheEnginesRandomStreamAndNotAManagedOne()
    {
        var (decals, engine) = Loaded();

        engine.Returns("Host.RandomFloat", 0.0f);
        decals.IndexForName("Impact.Concrete");

        // Server randomness is replayed by demo playback, so a managed
        // generator keeps state the recording knows nothing about.
        Assert.True(engine.CountOf("Host.RandomFloat") > 0,
            "the pick draws from the engine's stream");
    }

    [Fact]
    public void NoNumberIsDrawnWhileTheRunningWeightIsStillZero()
    {
        // Stock's `if (!totalweight || RandomFloat(...) < w)` SHORT-CIRCUITS,
        // so the first entry of a group is taken without a draw. Drawing anyway
        // would advance a stream demo playback replays -- a difference nothing
        // in this process could see and every recorded demo would.
        var engine = Fake();
        engine.Returns("Files.ReadGameFile", """
            "TranslationData" { "C" "Impact.Zero" }
            "Impact.Zero"
            {
            	"decals/a"	"0"
            	"decals/b"	"0"
            }
            """);

        var decals = new DecalEmitter(new RecordingLog(), engine, engine, engine);
        Sequence(engine, "Precache.PrecacheDecal", 4, 5);
        ((IGameLifecycle)decals).OnLevelInitPreEntity("dm_lockdown");

        int index = decals.IndexForName("Impact.Zero");

        Assert.True(engine.CountOf("Host.RandomFloat") == 0,
            "a group whose weights are all zero draws no random number at all");
        Assert.True(index == 5, "and answers its LAST entry, which is what stock's loop leaves");
    }

    [Fact]
    public void ANameIsAnsweredForAnIndexThatWasPrecached()
    {
        var (decals, _) = Loaded();

        Assert.True(decals.NameForIndex(13) == "decals/metal/shot1_subrect",
            "the third entry precached at 13 is the metal one");
    }

    [Fact]
    public void AnIndexNoEntryCarriesAnswersNullRatherThanEmpty()
    {
        var (decals, _) = Loaded();

        // Null and empty stay different all the way to the shim, which is what
        // turns null into stock's "". Collapsed here, a caller could not tell
        // "no such index" from "the entry is blank".
        Assert.True(decals.NameForIndex(99) is null, "no entry carries index 99");
    }

    // ---- the translation -----------------------------------------------------

    [Fact]
    public void ConcreteIsNotTranslatedAtAll()
    {
        var (decals, _) = Loaded();

        // Stock's first line: CHAR_TEX_CONCRETE returns the name unchanged.
        // Null is "no answer", which the shim turns back into the name given.
        Assert.True(decals.TranslateForGameMaterial("Impact.Metal", 'C') is null,
            "a concrete surface uses whatever decal it was handed");
    }

    [Fact]
    public void OnlyImpactConcreteIsEverTranslated()
    {
        var (decals, _) = Loaded();

        // Stock translates one name and one only: the generic impact. A decal
        // asked for by any other name is what the caller meant.
        Assert.True(decals.TranslateForGameMaterial("Impact.Metal", 'M') is null,
            "a decal that already names its material is left alone");
    }

    [Fact]
    public void TheGenericImpactBecomesTheSurfacesOwnGroup()
    {
        var (decals, _) = Loaded();

        Assert.True(decals.TranslateForGameMaterial("Impact.Concrete", 'M') == "Impact.Metal",
            "an impact on metal uses the metal group");
    }

    [Fact]
    public void AGameMaterialWithNoTranslationAnswersNull()
    {
        var (decals, _) = Loaded();

        Assert.True(decals.TranslateForGameMaterial("Impact.Concrete", 'Z') is null,
            "a surface the file says nothing about keeps the decal it was handed");
    }

    [Fact]
    public void AHighGameMaterialIsTheSameKeyWhicheverWayACharCarriedIt()
    {
        // THE SIGNEDNESS RULE, AND IT IS WHY THE MATERIAL CROSSES AS AN int.
        // `char` is signed on x86-64 and unsigned on ARM, and this value is a
        // BYTE USED AS A KEY -- stock builds a one-character string out of it
        // and looks that up. A managed side keying on the widened number would
        // look up "-23" on one platform and "233" on the other, so a material
        // above 127 would translate on one and silently not on the other.
        var engine = Fake();
        engine.Returns("Files.ReadGameFile",
            "\"TranslationData\" { \"é\" \"Impact.High\" }\n"
          + "\"Impact.High\" { \"decals/high\" \"1\" }");
        engine.Returns("Host.RandomFloat", 0f);
        engine.Returns("Precache.PrecacheDecal", 7);

        var decals = new DecalEmitter(new RecordingLog(), engine, engine, engine);
        ((IGameLifecycle)decals).OnLevelInitPreEntity("dm_lockdown");

        Assert.True(decals.TranslateForGameMaterial("Impact.Concrete", 0xE9) == "Impact.High",
            "the byte 0xE9 finds the material the file keys on that byte");
        Assert.True(decals.TranslateForGameMaterial("Impact.Concrete", -23) == "Impact.High",
            "and so does the same byte arriving as the negative number a signed char makes of it");
    }

    // ---- the participant ------------------------------------------------------

    [Fact]
    public void TheEmitterIsALifecycleParticipantAtItsOwnStage()
    {
        var (decals, _) = Build();

        Assert.True(((IGameLifecycle)decals).LifecycleOrder == (int)LifecycleStage.Decals,
            "the decal script has a stage of its own, so its position is a decision "
            + "somebody made rather than a registration order");
    }

    [Fact]
    public void ItDoesItsWorkInThePreEntityHalfAndNotThePostEntityOne()
    {
        var (decals, engine) = Build();
        engine.Returns("Precache.PrecacheDecal", 1);

        ((IGameLifecycle)decals).OnLevelInitPostEntity("dm_lockdown");

        // A precache after the first Spawn() is a LATE PRECACHE: the engine
        // warns and the index it hands back is not on the wire, so the decal
        // silently does not appear on a client.
        Assert.True(engine.CountOf("Precache.PrecacheDecal") == 0,
            "nothing is precached in the half that runs after the entities exist");
        Assert.True(engine.CountOf("Files.ReadGameFile") == 0,
            "and the script is not even read there");
    }
}
