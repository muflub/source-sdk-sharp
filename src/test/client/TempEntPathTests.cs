using SourceSharp.Abi;
using SourceSharp.Client.TempEnts;
using SourceSharp.Shared;
using System.Reflection;
using Xunit;
using static SourceSharp.Tests.TestServices;

namespace SourceSharp.Tests;

/// <summary>
/// A temp entity arrives per bullet impact. Its read and write stay out of the metadata, and
/// allocate no more than the state object they hand over.
/// </summary>
public class TempEntPathTests
{
    private static IEnumerable<Type> Declarations(params Assembly[] assemblies)
        => assemblies.SelectMany(a => a.GetTypes())
                     .Where(t => t.GetCustomAttribute<TempEntityAttribute>() is not null);

    private static readonly Assembly[] Games = [ClientGame, typeof(TF2.Client.ClientTeTFBlood).Assembly];

    private static IEnumerable<MethodBase> Roots()
    {
        foreach (Type declaration in Declarations(Games))
        {
            foreach (MethodBase method in TempEntPathCensus.Methods(declaration, "ReadFrom", "ReadMembers", "WriteMembers"))
                yield return method;
        }

        foreach (MethodBase method in TempEntPathCensus.Methods(typeof(TempEntState), "ToBlob"))
            yield return method;

        foreach (MethodBase method in TempEntPathCensus.Methods(typeof(ClientTempEnts), "Dispatch", "PlayLocally"))
            yield return method;

        // Every player's entries: the decode a subclass inherits, and the dispatch's own.
        foreach (MethodBase method in TempEntPathCensus.Methods(typeof(ClientTempEnt<>)))
            yield return method;

        foreach (MethodBase method in TempEntPathCensus.Methods(typeof(ClientTeEffectDispatch), "Play"))
            yield return method;
    }

    [Fact]
    public void Nothing_on_a_temp_entity_s_per_event_path_reads_the_metadata()
    {
        var (walked, reflective) = TempEntPathCensus.From(Roots());

        Assert.Empty(reflective);

        // The walk reached what it is about: generated members, and the accessors they call.
        Assert.Contains("SourceSharp.Shared.TeSparks.ReadMembers", walked);
        Assert.Contains("TF2.Shared.TeTFBlood.WriteMembers", walked);
        Assert.Contains("SourceSharp.Abi.TempEntReader.SlotVector", walked);
        Assert.Contains("SourceSharp.Abi.TempEntWriter.SlotVector", walked);
        Assert.Contains("SourceSharp.Client.TempEnts.ClientTempEnts.Dispatch", walked);
        Assert.True(Declarations(Games).Count() == 47, $"{Declarations(Games).Count()} declarations");
    }

    /// <summary>The census can see reflection at all: registration is where it is allowed to be.</summary>
    [Fact]
    public void The_census_sees_the_reflection_registration_is_allowed()
    {
        var (_, reflective) = TempEntPathCensus.From(TempEntPathCensus.Methods(typeof(TeClassBuilder), "Build"));

        Assert.Contains(reflective, call => call.Contains("GetCustomAttribute", StringComparison.Ordinal));
    }

    //-------------------------------------------------------------------------

    private static IReadOnlyList<TempEntType> Types()
    {
        var engine = Fake();
        engine.Strict = false;

        int next = 5;
        engine.ReturnsEachCall("Effects.TempEntRegister", () => next++);

        var registry = new TempEntRegistry(new RecordingLog());
        registry.RegisterAll("Test", typeof(TF2.Client.ClientTeTFBlood).Assembly, engine);

        return registry.All.Where(t => t.Class.Declaring.Assembly != typeof(TempEntPathTests).Assembly).ToList();
    }

    private const int Rounds = 64;

    /// <summary>What a batch of <paramref name="work"/> allocates on this thread, past its first calls.</summary>
    private static long Allocated(Action work)
    {
        for (int i = 0; i < 8; i++)
            work();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < Rounds; i++)
            work();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    public void Reading_a_declaration_into_a_state_allocates_nothing()
    {
        foreach (TempEntType type in Types())
        {
            if (type.Class.Declaring == typeof(TeEffectData))
                continue;

            var state = (TempEntState)Activator.CreateInstance(type.Class.Declaring)!;
            var blob = new byte[type.BlobBytes];

            long allocated = Allocated(() => state.ReadMembers(new TempEntReader(type, blob)));

            Assert.True(allocated == 0, $"{type.Class.ClassName} allocated {allocated} bytes over {Rounds} reads");
        }
    }

    [Fact]
    public void Writing_a_declaration_into_a_blob_allocates_nothing()
    {
        foreach (TempEntType type in Types())
        {
            if (type.Class.Declaring == typeof(TeEffectData))
                continue;

            var state = (TempEntState)Activator.CreateInstance(type.Class.Declaring)!;
            var blob = new byte[type.BlobBytes];

            long allocated = Allocated(() =>
            {
                var writer = new TempEntWriter(type, blob);
                state.WriteMembers(ref writer);
            });

            Assert.True(allocated == 0, $"{type.Class.ClassName} allocated {allocated} bytes over {Rounds} writes");
        }
    }

    [Fact]
    public void A_nested_table_is_read_and_written_with_no_box()
    {
        TempEntType type = Types().Single(t => t.Class.Declaring == typeof(TeEffectData));
        var blob = new byte[type.BlobBytes];
        TeEffectData data = default;

        long read = Allocated(() => data.ReadMembers(new TempEntReader(type, blob)));
        long written = Allocated(() =>
        {
            var writer = new TempEntWriter(type, blob);
            data.WriteMembers(ref writer);
        });

        Assert.Equal(0, read);
        Assert.Equal(0, written);
    }

    private static TempEntType TypeOf(string className) => Types().Single(t => t.Class.ClassName == className);

    [Fact]
    public void A_read_allocates_the_state_object_and_nothing_else()
    {
        TempEntType sparks = TypeOf("CTESparks");
        var sparksBlob = new byte[sparks.BlobBytes];

        Assert.Equal(Allocated(() => GC.KeepAlive(new TeSparks())),
                     Allocated(() => GC.KeepAlive(TeSparks.ReadFrom(new TempEntReader(sparks, sparksBlob)))));

        // The one with a nested table and the one with a run of vectors, the two shapes the
        // reflective code had to box.
        TempEntType dispatch = TypeOf("CTEEffectDispatch");
        var dispatchBlob = new byte[dispatch.BlobBytes];

        Assert.Equal(Allocated(() => GC.KeepAlive(new TeEffectDispatch())),
                     Allocated(() => GC.KeepAlive(TeEffectDispatch.ReadFrom(new TempEntReader(dispatch, dispatchBlob)))));

        TempEntType spline = TypeOf("CTEBeamSpline");
        var splineBlob = new byte[spline.BlobBytes];

        Assert.Equal(Allocated(() => GC.KeepAlive(new TeBeamSpline())),
                     Allocated(() => GC.KeepAlive(TeBeamSpline.ReadFrom(new TempEntReader(spline, splineBlob)))));
    }

    /// <summary>The measure can see an allocation: a state object is one.</summary>
    [Fact]
    public void The_measure_sees_an_allocation()
    {
        Assert.True(Allocated(() => GC.KeepAlive(new TeSparks())) >= Rounds * 24);
    }

    [Fact]
    public void A_blob_to_send_allocates_the_blob_and_nothing_else()
    {
        TempEntType type = TypeOf("CTEEffectDispatch");
        var state = new TeEffectDispatch();

        Assert.Equal(Allocated(() => GC.KeepAlive(new byte[type.BlobBytes])),
                     Allocated(() => GC.KeepAlive(state.ToBlob(type))));
    }
}
