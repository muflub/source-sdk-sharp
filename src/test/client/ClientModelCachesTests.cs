using System.Reflection;
using SourceSharp.Abi;
using SourceSharp.Client;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// The client's half of <c>SideModelCachesTests</c> (plan_abi_hotreload D8,
/// lane d8e): a client composition registers and clears its own model
/// caches, and the client's level shutdown clears the caches it is handed and
/// no others. Each side compiles its own copy of the shared interop
/// (plan_shim_areas_split phase 3), and a client composition is built from the
/// client's, so these live in the client suite.
/// </summary>
public class ClientModelCachesTests
{
    private const string Model = "models/props_junk/wood_crate001a.mdl";

    private const string Crate = "break { \"model\" \"props_junk/Wood_Crate001a_Chunk01\" }";

    private static ModelDescriptors Descriptors(List<string> asked)
        => new(model => { asked.Add(model); return 0; },
               _ => Model, new RecordingLog());

    [Fact]
    public void TwoClientCompositionsRegisterAndClearTheirOwnCaches()
    {
        var a = new ClientModule(new ClientModuleEngine(TestSides.Create(Side.Client)), new FakeClientEngineAdapters());
        var b = new ClientModule(new ClientModuleEngine(TestSides.Create(Side.Client)), new FakeClientEngineAdapters());
        a.ComposeEngineAreas();
        b.ComposeEngineAreas();

        Assert.NotSame(a.Services.Require<ModelDescriptors>(), b.Services.Require<ModelDescriptors>());
        Assert.NotSame(a.Services.Require<BreakModels>(), b.Services.Require<BreakModels>());

        var runtime = b.NewRuntimeBinding();
        Assert.Same(b.Services.Require<ModelDescriptors>(), Held<ModelDescriptors>(runtime));
        Assert.Same(b.Services.Require<BreakModels>(), Held<BreakModels>(runtime));
    }

    [Fact]
    public void TheClientsLevelShutdownClearsTheCachesItIsHandedAndNoOthers()
    {
        var mine = Descriptors([]);
        var theirs = Descriptors([]);
        var breaks = new BreakModels(_ => 7, _ => -1, _ => Crate, mine, new RecordingLog());
        mine.ForModel(Model);
        theirs.ForModel(Model);
        breaks.Count(Model);

        new SourceSharp.Client.Interop.ClientRuntimeBinding(new Crossings(), new RecordingLog(), new QuietHost(), new StandaloneMirror(), mine, breaks).LevelShutdown();

        Assert.Equal(0, mine.CachedCount);
        Assert.Equal(0, breaks.Parses);
        Assert.Equal(1, theirs.CachedCount);
    }

    // The one field of type T an object holds (a primary constructor's
    // captured parameter included).
    private static T Held<T>(object holder)
        => (T)holder.GetType()
               .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
               .Single(f => f.FieldType == typeof(T))
               .GetValue(holder)!;

    private sealed class QuietHost : SourceSharp.Client.Interop.IClientRuntimeHost
    {
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) => true;
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) => null;
        public void MapMaterialSearchPath(string? path, string? pathId, bool addToHead) { }
        public void MapMaterialMapAccess(bool open) { }
        public string? MapMaterialReloadPlan(string? subString) => null;
        public int WorldDataChanged(ReadOnlySpan<byte> blob, int updateType, Span<float> plan) => 0;
        public void LevelInitPreEntity() { }
        public void PostInit(int adapterMaxDxLevel) { }
    }
}
