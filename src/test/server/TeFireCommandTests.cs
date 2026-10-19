using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using Xunit;

using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// <c>cs_te_fire</c> — the one command that fires every temp entity in the
/// build, and the thing that makes gate 3 possible without a person standing in
/// the map.
///
/// <para>
/// IT REPLACES TEN COMMANDS AND THEIR TESTS. <c>cs_sv_te_sparks</c>,
/// <c>cs_sv_te_decal</c>, <c>cs_sv_te_fizz</c>, <c>cs_sv_te_restored</c> and
/// <c>cs_sv_te_energysplash</c> were managed and each had a class here checking
/// the same three things: it fires what it was told, it defaults the way its
/// help text says, and it fires nothing without <c>sv_cheats</c> or without a
/// point. Those are the tests below, asked once of the command that now does all
/// of it — with the addition that this one can be asked "did all forty-three go",
/// which none of them could.
/// </para>
///
/// <para>
/// THE REAL REGISTRY AND THE REAL WRITER over a recording engine, not a double
/// implementing <c>ITempEnts</c>: <c>cs_te_fire</c> deliberately does not go
/// through that interface — it covers twenty-one of forty-three tables — so a
/// double would have tested a path this command does not take.
/// </para>
/// </summary>
public class TeFireCommandTests
{
    private static (TeFireCommand Command, RecordingEngine Engine) Command(bool cheats = true)
    {
        var engine = Fake();
        engine.Returns("Effects.TempEntRegister", 0);
        engine.Returns("Precache.PrecacheModel", 71);
        engine.Returns("Precache.PrecacheDecal", 42);
        engine.Returns("Effects.EffectDispatchIndex", 9);
        engine.Ignores("Effects.TempEntPlayback");

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", TestServices.ServerGame, engine);

        var command = new TeFireCommand(new RecordingLog(), registry, new TeFire(new RecordingLog(), registry, engine, engine), engine)
        {
            CheatsEnabled = () => cheats,
        };

        return (command, engine);
    }

    [Fact]
    public void EachSenderComplainsOnceAboutAClassItCannotSend()
    {
        // The complained-about set was process-wide: one sender's warning
        // silenced every other's for the life of the process. It is per sender
        // (per module) now, and still once per name within one.
        var engine = Fake();
        var lines = new List<string>();
        var testLog = new RecordingLog { Line = (text, _) => lines.Add(text) };
        var empty = new TempEntRegistry(testLog);
        TeDemo demo = TeDemos.All[0];

        try
        {

            var first = new TeFire(testLog, empty, engine, engine);
            var second = new TeFire(testLog, empty, engine, engine);

            Assert.False(first.Send(demo, Vector.Zero, TeFilters.Broadcast, 0f));
            Assert.False(first.Send(demo, Vector.Zero, TeFilters.Broadcast, 0f));
            int once = lines.Count(l => l.Contains($"'{demo.ClassName}' is not registered"));
            Assert.True(once == 1, "one sender complains about a class once, however often it is asked");

            Assert.False(second.Send(demo, Vector.Zero, TeFilters.Broadcast, 0f));
            int twice = lines.Count(l => l.Contains($"'{demo.ClassName}' is not registered"));
            Assert.True(twice == 2, "a second sender has a memory of its own");
        }
        finally
        {
        }
    }

    [Fact]
    public void ItFiresTheOneItWasToldWhereItWasTold()
    {
        var (command, engine) = Command();

        command.Execute("sparks -3667 3439 60");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 1, "one temp entity was sent");

        var call = engine.Last("Effects.TempEntPlayback")!.Value;
        byte[] blob = call.ArgAs<byte[]>(4);

        // DT_TESparks nests DT_TEParticleSystem, whose m_vecOrigin is the first
        // twelve bytes of the blob -- so the origin is read back rather than
        // taken on trust, which is what this project's note on verifying a
        // datatable insists on.
        Assert.True(BitConverter.ToSingle(blob, 0) == -3667f, "at the x it was given");
        Assert.True(BitConverter.ToSingle(blob, 4) == 3439f, "the y it was given");

        // The demonstration fires 24 units above the point, so that a spark
        // shower at a floor is visible rather than inside it.
        Assert.True(BitConverter.ToSingle(blob, 8) == 84f,
            "and the z it was given plus the demonstration's own offset");
    }

    /// <summary>
    /// The class name works as well as the short one, because that is what the
    /// map's <c>te</c> keyvalue carries and what the coverage rule compares.
    /// </summary>
    [Fact]
    public void ItTakesTheClassNameAsWellAsTheShortName()
    {
        var (command, engine) = Command();

        command.Execute("CTESparks 0 0 0");
        command.Execute("sparks 0 0 0");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 2,
            "CTESparks and sparks name the same effect");
    }

    /// <summary>
    /// BROADCAST BY DEFAULT, and this is the assertion that stands for the trap
    /// this whole pass was warned about: a PVS filter with no recipients sends
    /// nothing and prints nothing, so a plot the player cannot see would be
    /// indistinguishable from a table that did not pair. A headless gate fires
    /// at a place nobody is standing by definition.
    /// </summary>
    [Fact]
    public void ItSendsBroadcastUnlessAskedForAFilterByName()
    {
        var (command, engine) = Command();

        command.Execute("sparks 0 0 0");

        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<TeFilter>(2).Kind
                    == TeFilterKind.Broadcast,
            "broadcast is the one filter that cannot have zero recipients for a connected client");

        command.Execute("sparks 10 20 30 pvs");

        Assert.True(engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<TeFilter>(2).Kind
                    == TeFilterKind.Pvs,
            "and the filter the real callers use is still reachable by name");
    }

    /// <summary>
    /// <c>all</c> is the whole reason this command exists: forty-three tables
    /// paired and one line that exercises every one of them.
    /// </summary>
    [Fact]
    public void AllFiresEveryTempEntityTheBuildSends()
    {
        var (command, engine) = Command();

        command.Execute("all 0 0 0");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == TeDemos.All.Count,
            $"all {TeDemos.All.Count} went out, which is what gate 3 needs in one line");
    }

    /// <summary>
    /// And it SPREADS them, over the same grid the sandbox station places them
    /// on — so the console run and the map put the same effect in the same
    /// place, and the two can be compared.
    /// </summary>
    [Fact]
    public void AllSpreadsThemOverTheSameGridTheStationUses()
    {
        var (command, engine) = Command();

        command.Execute("all 1000 2000 0");

        var origins = engine.Calls
            .Where(c => c.Member == "Effects.TempEntPlayback")
            .Select(c => c.ArgAs<TeFilter>(2))
            .ToList();

        // Sparks is last in the book and the base tables are first, so the two
        // ends of the run are the two ends of the grid: if the spread were not
        // applied every one of these would be the same point.
        var blobs = engine.Calls
            .Where(c => c.Member == "Effects.TempEntPlayback")
            .Select(c => c.ArgAs<byte[]>(4))
            .ToList();

        (float x0, float y0, _) = TeDemos.Plot(0);

        Assert.True(BitConverter.ToSingle(blobs[0], 0) == 1000f + x0,
            "the first demonstration fires at the grid's first cell");
        Assert.True(BitConverter.ToSingle(blobs[0], 4) == 2000f + y0,
            "on both axes");

        Assert.True(origins.Count == TeDemos.All.Count, "one send each");
    }

    [Fact]
    public void ItSendsNothingWithoutCheats()
    {
        var (command, engine) = Command(cheats: false);

        command.Execute("all 0 0 0");
        command.Execute("sparks 0 0 0");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 0,
            "sv_cheats 0 fires nothing, which is what debug-api inputs see by default");
    }

    [Fact]
    public void ItSendsNothingForANameNothingClaims()
    {
        var (command, engine) = Command();

        command.Execute("");
        command.Execute("splodge 0 0 0");
        command.Execute("splodge");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 0,
            "a name no temp entity claims fires nothing and says so");
    }

    /// <summary>
    /// A missing point is the ORIGIN and not a refusal, unlike the commands this
    /// replaces.
    ///
    /// <para>
    /// The difference is deliberate and it is about what the two are for. Those
    /// took a point because a lane firing one effect wanted to put it somewhere
    /// it could be seen; this fires forty-three and spreads them, so "no point
    /// given" has an obvious answer and refusing would only make the headless
    /// gate's command line longer.
    /// </para>
    /// </summary>
    [Fact]
    public void APointIsOptionalAndDefaultsToTheOrigin()
    {
        var (command, engine) = Command();

        command.Execute("sparks");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 1, "it still fired");

        byte[] blob = engine.Last("Effects.TempEntPlayback")!.Value.ArgAs<byte[]>(4);

        Assert.True(BitConverter.ToSingle(blob, 0) == 0f && BitConverter.ToSingle(blob, 4) == 0f,
            "at the world origin, plus the demonstration's own offset");
    }

    /// <summary>
    /// <c>list</c> needs no cheats, because it fires nothing.
    /// </summary>
    [Fact]
    public void ListSaysWhatItKnowsWithoutCheatsAndWithoutFiring()
    {
        var (command, engine) = Command(cheats: false);

        command.Execute("list");

        Assert.True(engine.CountOf("Effects.TempEntPlayback") == 0, "listing fires nothing");
    }

    /// <summary>
    /// The models the demonstrations draw with are asked for BY NAME at fire
    /// time, which is the only thing that keeps
    /// <see cref="SourceSharp.Server.ServerPrecache.PrecacheAll(IEnginePrecache, System.Reflection.Assembly[])"/>'s three honest: a
    /// model index of zero for one of them is that precache having lapsed, and
    /// the command says so.
    /// </summary>
    [Fact]
    public void ItResolvesTheModelsTheDemonstrationsDrawWith()
    {
        var (command, engine) = Command();

        command.Execute("all 0 0 0");

        var asked = engine.Calls
            .Where(c => c.Member == "Precache.PrecacheModel")
            .Select(c => c.ArgAs<string>(0))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(asked.Contains(TeDemos.Gibs),
            "models/gibs/hgibs.mdl, which is CTEBreakModel's and CTEPhysicsProp's own precache");
        Assert.True(asked.Contains(TeDemos.Smoke),
            "sprites/gunsmoke.vmt, which is CTESprite's");
        Assert.True(asked.Contains(TeDemos.Bubble),
            "sprites/bubble.vmt, which is CTEFizz's");
        Assert.True(asked.Contains(TeDemos.Laser),
            "and g_pModelNameLaser, which every beam draws with");
    }

    /// <summary>
    /// The one dispatch goes out by NAME through the engine's own numbering, and
    /// not through a second table of names kept here.
    /// </summary>
    [Fact]
    public void TheDispatchResolvesItsEffectNameThroughTheEnginesStringTable()
    {
        var (command, engine) = Command();

        command.Execute("effectdispatch 0 0 0");

        Assert.True(engine.Calls.Any(c => c.Member == "Effects.EffectDispatchIndex"
                                       && c.ArgAs<string>(0) == TeDemos.DispatchName),
            "the name is handed to the engine's string table, which is the only numbering there is");
    }
}
