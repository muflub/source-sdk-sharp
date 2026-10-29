using SourceSharp.Abi;
using SourceSharp.Server;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// propdata.txt's breakable-chunk table, in C# — plan_gamedll_after.md Phase A3.
///
/// <para>
/// Half of <c>CPropData</c>. The other half — the KeyValues tree
/// <c>SourceSharp_EntityGetPropData</c> and <c>ParsePropFromKV</c> read — is
/// still native, and the two steps that load and free it are asserted here as
/// steps rather than reimplemented.
/// </para>
/// </summary>
public class PropDataTests
{
    /// <summary>
    /// The real file's shape: one root, a BreakableModels section inside it,
    /// groups with backslashed model paths, and the "sorted smallest to
    /// largest" ordering the size cap depends on.
    /// </summary>
    private const string Script = """
        "PropData.txt"
        {
        	"Metal.Small"
        	{
        		"health"	"20"
        	}

        	"BreakableModels"
        	{
        		"WoodChunks"
        		{
        			"models\Gibs\wood_gib01e.mdl"	"1"
        			"models\Gibs\wood_gib01d.mdl"	"1"
        			"models\Gibs\wood_gib01c.mdl"	"1"
        		}
        		"GlassChunks"
        		{
        			"models\Gibs\Glass_shard01.mdl"	"1"
        		}
        	}
        }
        """;

    private static (PropData Props, RecordingEngine Engine) Build(string? script = Script)
    {
        var engine = Fake();
        engine.Returns("Files.ReadGameFile", script);
        engine.Returns("Precache.PrecacheModel", 1);

        return (new PropData(new RecordingLog(), engine, engine, engine), engine);
    }

    private static (PropData Props, RecordingEngine Engine) Loaded()
    {
        var (props, engine) = Build();
        ((IGameLifecycle)props).OnLevelInitPreEntity("dm_lockdown");
        return (props, engine);
    }

    // ---- the parse -----------------------------------------------------------

    [Fact]
    public void TheFileIsReadThroughTheEngineAndNotOffDisk()
    {
        var (props, engine) = Build();
        props.Load();

        var call = engine.Last("Files.ReadGameFile");

        Assert.True(call is not null, "the table asks the engine's filesystem for the file");
        Assert.True((string?)call!.Value.Args[0] == "scripts/propdata.txt",
            "and for the path stock reads, on the engine's search paths -- propdata.txt "
            + "is in neither the mod nor any VPK");
    }

    [Fact]
    public void OnlyTheBreakableModelsSectionIsRead()
    {
        var (props, _) = Build();
        props.Load();

        // "Metal.Small" is a prop-data section and belongs to the NATIVE half.
        // Reading it here would be a second owner of one fact, which is the
        // thing the partition between the two readers exists to avoid.
        Assert.True(props.Counts.Groups == 2, "two chunk groups and no prop-data sections");
    }

    [Fact]
    public void AModelPathKeepsItsBackslashes()
    {
        var (props, engine) = Loaded();

        engine.Returns("Host.RandomFloat", 0f);

        // Stock passes these straight to the precache and to the model index.
        // A reader that interpreted \G and \w would hand the engine a path to
        // nothing -- and a missing model still answers an index, so the prop
        // breaks into invisible pieces with no error anywhere.
        Assert.True(props.RandomChunkModel("GlassChunks", -1) == "models\\Gibs\\Glass_shard01.mdl",
            "the path is what the file says");
    }

    [Fact]
    public void AMissingFileIsSurvivedAndEveryGroupThenAnswersNull()
    {
        var (props, _) = Build(script: null);
        props.Load();

        Assert.True(props.Counts.Groups == 0, "nothing was read");
        Assert.True(props.RandomChunkModel("WoodChunks", -1) is null,
            "and a prop that breaks gets no chunk rather than an exception");
    }

    // ---- the steps -----------------------------------------------------------

    // plan_server_novalve.md P2.4: there is no native tree any more. The two
    // IShimLevel steps that loaded and freed it are retired, and a model's prop
    // data is resolved here (ModelPropData) over the file this reads.

    [Fact]
    public void NoNativePropDataStepIsTaken()
    {
        var (props, engine) = Loaded();

        ((IGameLifecycle)props).OnLevelShutdownPostEntity();

        Assert.True(engine.CountOf("Level.PropDataLoadFile") == 0 &&
                    engine.CountOf("Level.PropDataClearFile") == 0,
            "the retired steps are not called");
        Assert.True(engine.CountOf("Files.ReadGameFile") == 1, "the file IS read, here");
    }

    [Fact]
    public void TheFileIsStandingForPropDataOnceThePreEntityHalfHasRun()
    {
        var (props, _) = Loaded();

        // Anything reached from the entity spawns that follow this stage asks
        // for a model's prop data, which reads this file's sections as bases.
        // Wood.Small is not in the fixture, so a base naming it contributes
        // nothing and the model's own health stands; the fixture's own chunk
        // group is proof the file was parsed.
        Assert.Equal(12, props.ModelPropValues("mdlkeyvalue { prop_data { base NoSuch health 12 } }").Health);
        Assert.True(props.Counts.Groups > 0, "the file was parsed before any prop can ask");
    }

    [Fact]
    public void EveryChunkModelIsPrecachedOnTheLevelsPreEntityHalf()
    {
        var (_, engine) = Loaded();

        Assert.True(engine.CountOf("Precache.PrecacheModel") == 4,
            "three wood gibs and one glass shard");
    }

    [Fact]
    public void TheFileIsReadOnceAcrossTwoMapsAndPrecachedOnEach()
    {
        var (props, engine) = Loaded();

        ((IGameLifecycle)props).OnLevelShutdownPostEntity();
        ((IGameLifecycle)props).OnLevelInitPreEntity("dm_lockdown");

        // propdata.txt does not change between maps; a model's precache index
        // does, because the precache tables are rebuilt with the map.
        Assert.True(engine.CountOf("Files.ReadGameFile") == 1, "the file is read once");
        Assert.True(engine.CountOf("Precache.PrecacheModel") == 8, "and precached on both maps");
    }

    // ---- the pick ------------------------------------------------------------

    [Fact]
    public void AGroupIsFoundByPREFIXBecauseStockFindsItThatWay()
    {
        var (props, engine) = Loaded();
        engine.Returns("Host.RandomFloat", 0f);

        // Q_strncmp( STRING(iszChunkType), pszBreakableSection, strlen(section) ):
        // the GROUP's name must start with what was asked for. Tightening this
        // to an equality would silently stop resolving every model whose
        // breakable_model key is a prefix -- as a prop that breaks into nothing.
        Assert.True(props.RandomChunkModel("Wood", -1) == "models\\Gibs\\wood_gib01e.mdl",
            "\"Wood\" finds \"WoodChunks\"");
    }

    [Fact]
    public void AnUnknownGroupAnswersNull()
    {
        var (props, _) = Loaded();

        Assert.True(props.RandomChunkModel("Feathers", -1) is null,
            "a group the file does not name has no model to give");
    }

    [Fact]
    public void AnEmptySectionIsRefusedWithoutTouchingTheTable()
    {
        var (props, _) = Loaded();

        // An empty prefix would match the FIRST group, which is not "no answer"
        // -- it is a wrong one, and a prop whose breakable_model key is blank
        // would start shedding wood.
        Assert.True(props.RandomChunkModel("", -1) is null, "an empty section names no group");
    }

    [Fact]
    public void TheSizeCapBoundsTheIndexAndNotAMeasuredSize()
    {
        var (props, engine) = Loaded();

        // Stock: RandomInt( 0, MIN(iMaxSize, count-1) ). The groups are
        // documented in propdata.txt as "sorted in order of smallest to
        // largest", so a low index is a small gib -- the cap is an index and
        // reading it as anything else would be reading a comment as an API.
        //
        // THE TWO ANSWERS ARE COMPARED WITH EACH OTHER, and that is what makes
        // this a fact about the cap. Asserting the capped answer alone was
        // GREEN through a mutation that ignored the cap outright: the stubbed
        // draw is a constant, so a wider range answered the same entry and the
        // difference the cap makes was never on either side of the assertion.
        engine.Returns("Host.RandomFloat", 2.5f);

        Assert.True(props.RandomChunkModel("WoodChunks", -1) == "models\\Gibs\\wood_gib01c.mdl",
            "with no cap the same draw reaches the third and largest entry");
        Assert.True(props.RandomChunkModel("WoodChunks", 1) == "models\\Gibs\\wood_gib01d.mdl",
            "and a cap of one holds the same draw down to the second");
    }

    [Fact]
    public void ACapLargerThanTheGroupIsTheGroup()
    {
        var (props, engine) = Loaded();
        engine.Returns("Host.RandomFloat", 2.99f);

        Assert.True(props.RandomChunkModel("WoodChunks", 99) == "models\\Gibs\\wood_gib01c.mdl",
            "MIN(maxSize, count-1) is still the last entry");
    }

    [Fact]
    public void TheDrawComesFromTheEnginesStreamAndNotAManagedOne()
    {
        var (props, engine) = Loaded();
        engine.Returns("Host.RandomFloat", 1.5f);

        Assert.True(props.RandomChunkModel("WoodChunks", -1) == "models\\Gibs\\wood_gib01d.mdl",
            "the second entry, which is what a draw of 1.5 selects");
        Assert.True(engine.CountOf("Host.RandomFloat") == 1,
            "and it is drawn from the stream demo playback replays, once");
    }

    [Fact]
    public void ADrawAtTheOpenEndOfTheIntervalStaysInsideTheGroup()
    {
        var (props, engine) = Loaded();

        // RandomFloat(0, top+1) is inclusive of top+1 on this engine, and
        // (int)(top+1) is one past the end. Stock's RandomInt is inclusive of
        // `top`, so the clamp is what makes the two agree rather than reading
        // past the array on the one draw in a million that lands there.
        engine.Returns("Host.RandomFloat", 3.0f);

        Assert.True(props.RandomChunkModel("WoodChunks", -1) == "models\\Gibs\\wood_gib01c.mdl",
            "the highest draw answers the last entry rather than running off the end");
    }

    // ---- the participant ------------------------------------------------------

    [Fact]
    public void ThePropDataIsALifecycleParticipantAtItsOwnStage()
    {
        var (props, _) = Build();

        Assert.True(((IGameLifecycle)props).LifecycleOrder == (int)LifecycleStage.PropData,
            "propdata has a stage of its own");
    }

    [Fact]
    public void ItRunsBeforeTheDecalScriptBecauseStocksRowsDid()
    {
        // CPropData was row 9 of kServerSystems[] and CDecalEmitterSystem row
        // 11, and no row between them declared a pre-entity stage. So the order
        // between these two is stock's, and it is the one thing about their
        // stage values that was a decision.
        Assert.True((int)LifecycleStage.PropData < (int)LifecycleStage.Decals,
            "propdata parses before the decal script, as it did in the table");
    }

    [Fact]
    public void ItDoesNothingInThePostEntityHalf()
    {
        var (props, engine) = Build();

        ((IGameLifecycle)props).OnLevelInitPostEntity("dm_lockdown");

        Assert.True(engine.CountOf("Precache.PrecacheModel") == 0,
            "a precache after the entities exist is a late precache");
    }
}
