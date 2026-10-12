using SourceSharp.Abi;
using SourceSharp.Client;
using SourceSharp.Client.Interop;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_abi_hotreload D7: the client's <see cref="StandaloneMirror"/> belongs
/// to the client's composition. The engine's pushes arrive through the
/// Client.Interop handlers, the render loop reads through the Abi render
/// binding, and both must reach the SAME instance, the one the module
/// registered, while a second composition in the same process has its own.
///
/// <para>
/// Each fact composes real <see cref="ClientModule"/>s (their engine areas
/// only; a full Initialize needs an engine) and drives the handler the module's
/// root builds. The render binding answers from the mirror only on the client
/// side, so each fact enters it and puts the thread back.
/// </para>
/// </summary>
public class StandaloneMirrorCompositionTests
{
    private static readonly EntityHandle Prop = new(0x0004_0021u);

    private static ClientModule Composed(SideState? state = null)
    {
        var module = new ClientModule(new ModuleEngine(new EngineSide(Side.Client, state ?? new SideState(Side.Client))));
        module.ComposeEngineAreas();
        return module;
    }

    // No thread marks a side since plan_side_isolation P5: the module's own
    // engine is the client's, whichever side ran last.
    private static T AsClient<T>(Func<T> read) => read();

    private static bool Dormant(ClientModule module, EntityHandle entity)
        => AsClient(() => module.Services.Require<IEngineEntityRender>().IsDormant(entity));

    [Fact]
    public void ADormancyPushReachesTheRenderReaderOfTheSameComposition()
    {
        var module = Composed();

        module.NewStandaloneBinding().OnStandaloneDormancyChanged(Prop.Value, 1);

        Assert.True(Dormant(module, Prop), "the push and the render read reach one mirror");
    }

    [Fact]
    public void ADormancyPushIsInvisibleToASecondComposition()
    {
        var pushed = Composed();
        var other = Composed();

        pushed.NewStandaloneBinding().OnStandaloneDormancyChanged(Prop.Value, 1);

        Assert.True(Dormant(pushed, Prop), "the pushed composition sees it (the positive arm)");
        Assert.False(Dormant(other, Prop), "a second composition has a mirror of its own");
    }

    [Fact]
    public void TheContainersMirrorIsTheOneThePushFeeds()
    {
        // What cs_cl_mirror_check is handed: the container's registration.
        var module = Composed();

        module.NewStandaloneBinding().OnStandaloneDormancyChanged(Prop.Value, 1);

        Assert.True(module.Services.Require<StandaloneMirror>().IsDormant(Prop),
                    "the gate command reads the mirror the push fed");
    }

    [Fact]
    public void ARemovalForgetsWhatTheRenderReaderSaw()
    {
        var module = Composed();
        var binding = module.NewStandaloneBinding();

        binding.OnStandaloneDormancyChanged(Prop.Value, 1);
        Assert.True(Dormant(module, Prop), "dormant before the removal (the positive arm)");

        binding.OnStandaloneRemoved(Prop.Value);

        Assert.False(Dormant(module, Prop), "a recycled handle inherits nothing");
    }

    [Fact]
    public void LevelShutdownClearsTheMirrorItIsHanded()
    {
        var module = Composed();
        var mirror = module.Services.Require<StandaloneMirror>();

        module.NewStandaloneBinding().OnStandaloneDormancyChanged(Prop.Value, 1);
        Assert.True(Dormant(module, Prop), "dormant before the shutdown (the positive arm)");

        new ClientRuntimeBinding(new Crossings(), new RecordingLog(), new QuietHost(), mirror, module.Services.Require<ModelDescriptors>(), module.Services.Require<BreakModels>()).LevelShutdown();

        Assert.False(Dormant(module, Prop), "the level's records went with it");
    }

    [Fact]
    public void TheRuntimeHandlerTheRootBuildsHoldsThisCompositionsMirror()
    {
        var module = Composed();

        var held = typeof(ClientRuntimeBinding)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(StandaloneMirror))
            .GetValue(module.NewRuntimeBinding());

        Assert.Same(module.Services.Require<StandaloneMirror>(), held);
    }

    [Fact]
    public void AHotReloadCarriesDormancyAndSequenceToTheNextComposition()
    {
        // One side state across the reload, as the Bootstrap keeps it: what
        // the reload store carries is the state's (statics to zero).
        var state = new SideState(Side.Client);
        var before = Composed(state);
        var mirror = before.Services.Require<StandaloneMirror>();

        before.NewStandaloneBinding().OnStandaloneDormancyChanged(Prop.Value, 1);
        mirror.NoteSequence(Prop, 5, 1f, restart: false, now: 2f);
        mirror.NoteSequenceInfo(Prop, 1.5f, looping: true);

        before.ReloadSave();

        var after = Composed(state);
        after.ReloadRestore();

        var carried = after.Services.Require<StandaloneMirror>();
        Assert.NotSame(mirror, carried);
        Assert.True(Dormant(after, Prop), "dormancy survives the reload");
        Assert.Equal((5, 2f, 1.5f, true, 1),
                     (carried.Sequence(Prop).Sequence, carried.Sequence(Prop).StartTime,
                      carried.Sequence(Prop).Duration, carried.Sequence(Prop).Looping,
                      carried.Sequence(Prop).Starts));
    }

    private sealed class QuietHost : IClientRuntimeHost
    {
        public void Frame(float frameTime) { }
        public void OnStage(int stage) { }
        public void LevelInit(string mapName) { }
        public void LevelShutdown() { }
        public void InvalidateMdlCache() { }
        public bool ParticlesNeedFullReload(int previousMapParticleFiles) => true;
        public string? MapMaterialReloadSet(string nextBsp, string previousBsp) => null;
    }
}
