using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// plan_shim_areas_split phase 5: each client adapter takes the shim areas it
/// calls, handed over by the client's composition root. A fact per seam reaches
/// the adapter through that registration, as its interface, over a
/// <see cref="FakeShimRoot"/>, so it covers both that the root passes the right
/// area and that the adapter calls the right slot. Over an engine that never
/// attached, the registration refuses before it builds any adapter.
/// </summary>
public unsafe class ClientAdapterRegistrationTests
{
    // A slot declared SuppressGCTransition cannot call a managed callback, so
    // each fact drives one that is not.
    [UnmanagedCallersOnly]
    private static int TextureSeventeen(void* self) => 17;

    /// <summary>The client's adapters, composed as the client's root composes them, over <paramref name="side"/>.</summary>
    private static ServiceContainer Composed(EngineSide side)
    {
        var services = new ServiceContainer();
        new ClientEntityFetches().AddTo(services, new ClientModuleEngine(side));
        return services;
    }

    private static EngineSide Attached(FakeShimRoot root)
    {
        var side = TestSides.Create(Side.Client);
        Assert.True(side.Attachment.AttachShims(root.Bind(), out string why), why);
        return side;
    }

    /// <summary>
    /// The client's registration of <typeparamref name="T"/> over a root that
    /// serves <paramref name="slot"/> of <paramref name="version"/> as
    /// <paramref name="fn"/>, called once.
    /// </summary>
    private static TResult Through<T, TResult>(string version, string slot, nint fn, Func<T, TResult> call)
        where T : class
    {
        using var root = new FakeShimRoot();
        root.Live(version, slot, fn);
        return call(Composed(Attached(root)).Require<T>());
    }

    private const string Fx = "SourceSharpFxClient001";

    [UnmanagedCallersOnly] private static int One(void* self) => 1;
    [UnmanagedCallersOnly] private static int Seven(void* self) => 7;
    [UnmanagedCallersOnly] private static int Nine(void* self, void* output) => 9;

    [Fact]
    public void TheRegisteredFxSuppressionAsksTheFxArea()
        => Assert.True(Through<IFxSuppression, bool>(Fx, nameof(ShimFxClientFwd.FxCanPredict),
                                                     (nint)(delegate* unmanaged<void*, int>)&One, s => s.CanPredict()));

    [Fact]
    public void TheRegisteredFxRenderAsksTheFxArea()
        => Assert.Equal(7, Through<IFxRender, int>(Fx, nameof(ShimFxClientFwd.FxLocalPlayer),
                                                   (nint)(delegate* unmanaged<void*, int>)&Seven, r => r.LocalPlayer()));

    [Fact]
    public void TheRegisteredFlashReadsAskTheFxArea()
        => Assert.Equal(7, Through<IFxFlashReads, int>(Fx, nameof(ShimFxClientFwd.FxMaterialsCached),
                                                       (nint)(delegate* unmanaged<void*, int>)&Seven, r => r.MaterialsCached()));

    [Fact]
    public void TheRegisteredFxPrecacheAsksTheFxArea()
        => Assert.Equal(7, Through<IFxPrecache, int>(Fx, nameof(ShimFxClientFwd.FxPrecacheCount),
                                                     (nint)(delegate* unmanaged<void*, int>)&Seven, p => p.Count()));

    [Fact]
    public void TheRegisteredSpriteDrawAsksTheFxArea()
        => Assert.Equal(9, Through<IFxSpriteDraw, int>(Fx, nameof(ShimFxClientFwd.FxSpriteDrawEnd),
                                                       (nint)(delegate* unmanaged<void*, void*, int>)&Nine,
                                                       d => d.End(new float[ClientSpriteRequest.OutFloats])));

    private const string Particles = "SourceSharpParticlesClient001";

    [ThreadStatic] private static float[]? t_seen;

    [UnmanagedCallersOnly] private static int ContentsOfZ(void* self, uint owner, float* point) => (int)point[2];

    [UnmanagedCallersOnly]
    private static int NameFx(void* self, int index, byte* name, int capacity)
    {
        name[0] = (byte)'f';
        name[1] = (byte)'x';
        return 2;
    }

    [UnmanagedCallersOnly]
    private static int WorldTraceQuarter(void* self, float* start, float* end, float* output, int floats)
    {
        for (int i = 0; i < floats; i++)
            output[i] = 0;
        output[0] = 0.25f;
        return floats;
    }

    [UnmanagedCallersOnly]
    private static int PlaneTraceHalf(void* self, float* start, float* end, int mask, float* output, int floats)
    {
        for (int i = 0; i < floats; i++)
            output[i] = 0;
        output[0] = 0.5f;
        output[4] = mask;
        return floats;
    }

    [UnmanagedCallersOnly] private static void SeeDestroy(void* self, uint owner, int channel) => t_seen = [owner, channel];
    [UnmanagedCallersOnly] private static void SeeMeshDestroy(void* self, uint owner) => t_seen = [owner];
    [UnmanagedCallersOnly] private static void SeeWind(void* self, float* wind, float* sway, int winds) => t_seen = [winds, wind[0]];

    [Fact]
    public void TheRegisteredBrushContentsAskTheParticlesArea()
        => Assert.Equal(5, Through<IClientBrushContents, int>(Particles, nameof(ShimParticlesClientFwd.ParticlesBrushPointContents),
                                                              (nint)(delegate* unmanaged<void*, uint, float*, int>)&ContentsOfZ,
                                                              b => b.PointContents(EntityHandle.None, new Vector(0, 0, 5))));

    [Fact]
    public void TheRegisteredParticleSystemsAskTheParticlesArea()
        => Assert.Equal(7, Through<IClientParticleSystems, int>(Particles, nameof(ShimParticlesClientFwd.ParticleDefinitionReads),
                                                                (nint)(delegate* unmanaged<void*, int>)&Seven, p => p.DefinitionReads()));

    [Fact]
    public void TheRegisteredOneShotsAskTheParticlesArea()
        => Assert.Equal("fx", Through<IClientParticleOneShots, string?>(Particles, nameof(ShimParticlesClientFwd.ParticleSystemNameFromIndex),
                                                                        (nint)(delegate* unmanaged<void*, int, byte*, int, int>)&NameFx,
                                                                        o => o.NameFromIndex(3)));

    [Fact]
    public void TheRegisteredWorldTraceAsksTheParticlesArea()
        => Assert.Equal(0.25f, Through<IParticleWorldTrace, float>(Particles, nameof(ShimParticlesClientFwd.ParticlesWorldTrace),
                                                                   (nint)(delegate* unmanaged<void*, float*, float*, float*, int, int>)&WorldTraceQuarter,
                                                                   t => t.TraceLine(Vector.Zero, new Vector(0, 0, 64)).Fraction));

    [Fact]
    public void TheRegisteredPlaneTraceAsksTheParticlesArea()
        => Assert.Equal((0.5f, 3f), Through<IClientPlaneTrace, (float, float)>(Particles, nameof(ShimParticlesClientFwd.ParticlesTraceLine),
                                                                               (nint)(delegate* unmanaged<void*, float*, float*, int, float*, int, int>)&PlaneTraceHalf,
                                                                               t => t.TraceLine(Vector.Zero, new Vector(0, 0, 64), 3) is var r ? (r.Fraction, r.PlaneDist) : default));

    [Fact]
    public void TheRegisteredExecutorDestroysThroughTheParticlesArea()
    {
        t_seen = null;
        Through<INativeParticleExecutor, int>(Particles, nameof(ShimParticlesClientFwd.ParticlesDestroy),
                                              (nint)(delegate* unmanaged<void*, uint, int, void>)&SeeDestroy,
                                              x => { x.Destroy(new EntityHandle(0x0001_0002u), 6); return 0; });

        Assert.Equal([0x0001_0002u, 6f], t_seen!);
    }

    [Fact]
    public void TheRegisteredWorldTextMeshesDestroyThroughTheParticlesArea()
    {
        t_seen = null;
        Through<IClientWorldTextMeshes, int>(Particles, nameof(ShimParticlesClientFwd.WorldTextMeshDestroy),
                                             (nint)(delegate* unmanaged<void*, uint, void>)&SeeMeshDestroy,
                                             m => { m.Destroy(new EntityHandle(0x0001_0003u)); return 0; });

        Assert.Equal([0x0001_0003u], t_seen!);
    }

    [Fact]
    public void TheRegisteredWindPublisherPublishesThroughTheEffectsRenderArea()
    {
        t_seen = null;
        Through<IClientWindPublisher, int>("SourceSharpEffectsRenderClient001", nameof(ShimEffectsRenderClientFwd.WindPublish),
                                           (nint)(delegate* unmanaged<void*, float*, float*, int, void>)&SeeWind,
                                           w => { w.Publish(new Vector(2, 0, 0), null, 1); return 0; });

        Assert.Equal([1f, 2f], t_seen!);
    }

    private const string EffectsRender = "SourceSharpEffectsRenderClient001";
    private const string View = "SourceSharpViewClient001";

    [UnmanagedCallersOnly] private static int HasModel(void* self, uint entity) => entity == 0x0001_0004u ? 1 : 0;

    [UnmanagedCallersOnly]
    private static int RopeMaterialTall(void* self, byte* name, int* height, int* back)
    {
        *height = 32;
        *back = 1;
        return 11;
    }

    [UnmanagedCallersOnly] private static void SeeOverride(void* self, int active, float* origin, float* angles, int setAngles)
        => t_seen = [active, origin[0], setAngles];

    [UnmanagedCallersOnly]
    private static int TargetInfo(void* self, int target, int* out4)
    {
        out4[0] = 640; out4[1] = 480; out4[2] = target; out4[3] = 2;
        return 1;
    }

    [Fact]
    public void TheRegisteredBeamExecutorAsksTheEffectsRenderArea()
        => Assert.True(Through<IClientBeamExecutor, bool>(EffectsRender, nameof(ShimEffectsRenderClientFwd.BeamEntityHasModel),
                                                          (nint)(delegate* unmanaged<void*, uint, int>)&HasModel,
                                                          b => b.EntityHasModel(new EntityHandle(0x0001_0004u))));

    [Fact]
    public void TheRegisteredRopeExecutorAsksTheEffectsRenderArea()
    {
        var (handle, height, back) = Through<IClientRopeExecutor, (int, int, bool)>(
            EffectsRender, nameof(ShimEffectsRenderClientFwd.RopeMaterial),
            (nint)(delegate* unmanaged<void*, byte*, int*, int*, int>)&RopeMaterialTall,
            r => (r.Material("cable/rope", out int h, out bool b), h, b));

        Assert.Equal((11, 32, true), (handle, height, back));
    }

    [Fact]
    public void TheRegisteredPrecipitationEngineAsksThePrecipitationArea()
        => Assert.Equal(7, Through<IClientPrecipitationEngine, int>("SourceSharpPrecipitationClient001",
                                                                    nameof(ShimPrecipitationClientFwd.PrecipitationScreenWidth),
                                                                    (nint)(delegate* unmanaged<void*, int>)&Seven, p => p.ScreenWidth()));

    [Fact]
    public void TheRegisteredObserverCameraSetsTheViewThroughTheViewArea()
    {
        t_seen = null;
        Through<IObserverCameraSlots, int>(View, nameof(ShimViewClientFwd.ViewOverrideSet),
                                           (nint)(delegate* unmanaged<void*, int, float*, float*, int, void>)&SeeOverride,
                                           o => { o.SetView(true, new Vector(9, 0, 0), default, false); return 0; });

        Assert.Equal([1f, 9f, 0f], t_seen!);
    }

    [Fact]
    public void TheRegisteredViewTargetsAskTheViewArea()
    {
        var info = Through<IViewTargets, ViewTargetInfo>(View, nameof(ShimViewClientFwd.ViewTargetInfo),
                                                         (nint)(delegate* unmanaged<void*, int, int*, int>)&TargetInfo,
                                                         v => v.Info(3, out var i) ? i : default);

        Assert.Equal(new ViewTargetInfo(640, 480, 3, 2), info);
    }

    // The pixel visibility's query counts (C7): the visible query's count and the counting query's.
    [UnmanagedCallersOnly]
    private static void QueryPixelsOf(void* self, int visible, int count, int* answer)
    {
        answer[0] = visible * 10;
        answer[1] = count * 10;
    }

    [Fact]
    public void TheRegisteredPixelVisSlotsCountThroughTheViewArea()
    {
        var counts = Through<IPixelVisSlots, (int, int)>(View, nameof(ShimViewClientFwd.PixelVisQueryPixels),
                                                         (nint)(delegate* unmanaged<void*, int, int, int*, void>)&QueryPixelsOf,
                                                         s => { s.QueryPixels(3, 4, out int v, out int c); return (v, c); });
        Assert.Equal((30, 40), counts);
    }

    // The current view: its id, and the origin's x in the first float.
    [UnmanagedCallersOnly]
    private static int ViewSix(void* self, float* output, int floats)
    {
        output[0] = 12.5f;
        return 6;
    }

    [Fact]
    public void TheRegisteredPixelVisSlotsReadTheViewThroughTheViewArea()
    {
        var view = Through<IPixelVisSlots, CurrentViewState>(View, nameof(ShimViewClientFwd.PixelVisView),
                                                             (nint)(delegate* unmanaged<void*, float*, int, int>)&ViewSix,
                                                             s => s.View());
        Assert.Equal((6, 12.5f), (view.Id, view.Origin.X));
    }

    [ThreadStatic] private static string? t_found;

    // The material asked for: its name and group, answered with an id.
    [UnmanagedCallersOnly]
    private static int FindTwelve(void* self, byte* name, byte* group)
    {
        t_found = Marshal.PtrToStringUTF8((nint)name) + " " + Marshal.PtrToStringUTF8((nint)group);
        return 12;
    }

    [Fact]
    public void TheRegisteredGlowMaterialsFindThroughTheViewArea()
    {
        t_found = null;
        int id = Through<IGlowMaterials, int>(View, nameof(ShimViewClientFwd.GlowMaterialFind),
                                              (nint)(delegate* unmanaged<void*, byte*, byte*, int>)&FindTwelve,
                                              m => m.Find("sun/overlay", "ClientEffect textures"));
        Assert.Equal((12, "sun/overlay ClientEffect textures"), (id, t_found));
    }

    [ThreadStatic] private static string? t_declared;

    // What arrived: the slot, the name, the group and the three ints.
    [UnmanagedCallersOnly]
    private static int SeeDeclare(void* self, int slot, byte* name, byte* group, int complain, int flags, int release)
    {
        t_declared = $"{slot} {Marshal.PtrToStringUTF8((nint)name)} {Marshal.PtrToStringUTF8((nint)group)} {complain} {flags} {release}";
        return 1;
    }

    [Fact]
    public void TheRegisteredRenderTargetTableDeclaresThroughTheViewArea()
    {
        t_declared = null;
        bool declared = Through<IRenderTargetTable, bool>(View, nameof(ShimViewClientFwd.RenderTargetDeclare),
                                                          (nint)(delegate* unmanaged<void*, int, byte*, byte*, int, int, int, int>)&SeeDeclare,
                                                          t => t.Declare(3, "_rt_X", "RenderTargets", true, 4, 5));
        Assert.True(declared);
        Assert.Equal("3 _rt_X RenderTargets 1 4 5", t_declared);
    }

    [UnmanagedCallersOnly] private static int ContentsOfZOnly(void* self, float* point) => (int)point[2];

    [Fact]
    public void TheRegisteredSplashCarryOutAsksThePhysicsArea()
        => Assert.Equal(6, Through<IClientSplashCarryOut, int>("SourceSharpPhysicsClient001",
                                                               nameof(ShimPhysicsClientFwd.PhysicsClientPointContents),
                                                               (nint)(delegate* unmanaged<void*, float*, int>)&ContentsOfZOnly,
                                                               c => c.PointContents(new Vector(0, 0, 6))));

    [UnmanagedCallersOnly] private static int SevenFor(void* self, byte* name) => 7;
    [UnmanagedCallersOnly] private static int GotCmd(void* self, int command, void* cmd) => 1;

    [UnmanagedCallersOnly]
    private static int ChannelFloats(void* self, float* values, int floats)
    {
        for (int i = 0; i < floats; i++)
            values[i] = 0;
        values[0] = 1.5f;
        return floats;
    }

    [UnmanagedCallersOnly]
    private static int TwoProps(void* self, int index, byte* cls, int clsBytes, byte* table, int tableBytes,
                                byte* prop, int propBytes, int* type, int* flags, int* offset, int* elements)
        => index < 0 ? 2 : 0;

    [UnmanagedCallersOnly] private static void SeeConVar(void* self, byte* name, byte* value) => t_seen = [name[0], value[0]];

    [UnmanagedCallersOnly]
    private static int FoundVarThree(void* self, int handle, byte* name, int complain, int* found)
    {
        *found = 1;
        return 3;
    }

    // One block, so the lists read a count; allocated per fact by the fact.
    [ThreadStatic] private static nint t_blocks;

    [UnmanagedCallersOnly]
    private static void** OneInterpBlock(void* self, int* count, float* amount)
    {
        *count = 1;
        *amount = 0.25f;
        return (void**)t_blocks;
    }

    [UnmanagedCallersOnly]
    private static void** OnePolicyBlock(void* self, int* count)
    {
        *count = 1;
        return (void**)t_blocks;
    }

    private static TResult WithOneBlock<TResult>(Func<TResult> run)
    {
        t_blocks = Marshal.AllocHGlobal(sizeof(nint));
        try
        {
            *(nint*)t_blocks = 0;
            return run();
        }
        finally
        {
            Marshal.FreeHGlobal(t_blocks);
            t_blocks = 0;
        }
    }

    [Fact]
    public void TheRegisteredConVarSlotsAskTheConVarsArea()
        => Assert.Equal(7, Through<IClientConVarSlots, int>("SourceSharpConVarsClient001", nameof(ShimConVarsClientFwd.ConVarsClientWatch),
                                                            (nint)(delegate* unmanaged<void*, byte*, int>)&SevenFor, c => c.Watch("fov")));

    [Fact]
    public void TheRegisteredEffectRegistrationsAskTheFxArea()
        => Assert.Equal(7, Through<IClientEffectRegistrations, int>(Fx, nameof(ShimFxClientFwd.EffectRegistrationCount),
                                                                    (nint)(delegate* unmanaged<void*, int>)&Seven, r => r.Count));

    [Fact]
    public void TheRegisteredNetChannelAsksTheNetArea()
        => Assert.Equal(1.5f, Through<IClientNetChannel, float?>("SourceSharpNetClient001", nameof(ShimNetClientFwd.ClientNetChannel),
                                                                 (nint)(delegate* unmanaged<void*, float*, int, int>)&ChannelFloats,
                                                                 n => n.Channel()?.Time));

    [Fact]
    public void TheRegisteredRecvTablesAskTheStandaloneArea()
        => Assert.Equal(2, Through<IClientRecvTables, int>("SourceSharpStandaloneClient001", nameof(ShimStandaloneClientFwd.ClientRecvProp),
                                                           (nint)(delegate* unmanaged<void*, int, byte*, int, byte*, int, byte*, int, int*, int*, int*, int*, int>)&TwoProps,
                                                           r => r.Props().Count));

    [Fact]
    public void TheRegisteredSoundscapeAudioSetsAConVarThroughTheConsoleArea()
    {
        t_seen = null;
        Through<ISoundscapeAudio, int>("SourceSharpConsole001", nameof(ShimConsoleFwd.SetNamedConVarValue),
                                       (nint)(delegate* unmanaged<void*, byte*, byte*, void>)&SeeConVar,
                                       a => { a.SetConVar("dsp_player", "9"); return 0; });

        Assert.Equal([(float)'d', (float)'9'], t_seen!);
    }

    [Fact]
    public void TheRegisteredReplayEngineAsksThePredictionArea()
        => Assert.True(Through<IClientReplayEngine, bool>("SourceSharpPredictionClient001", nameof(ShimPredictionClientFwd.ClientGetUserCmd),
                                                          (nint)(delegate* unmanaged<void*, int, void*, int>)&GotCmd,
                                                          r => r.TryGetUserCmd(5, out _)));

    [Fact]
    public void TheRegisteredInterpolationBlocksAskTheStandaloneArea()
        => Assert.Equal((1, 0.25f), WithOneBlock(() => Through<IInterpolationBlocks, (int, float)>(
               "SourceSharpStandaloneClient001", nameof(ShimStandaloneClientFwd.ClientInterpBlocks),
               (nint)(delegate* unmanaged<void*, int*, float*, void**>)&OneInterpBlock,
               b => (b.Begin(out float amount), amount))));

    [Fact]
    public void TheRegisteredRenderPolicyBlocksAskTheStandaloneArea()
        => Assert.Equal(1, WithOneBlock(() => Through<IRenderPolicyBlocks, int>(
               "SourceSharpStandaloneClient001", nameof(ShimStandaloneClientFwd.ClientPolicyBlocks),
               (nint)(delegate* unmanaged<void*, int*, void**>)&OnePolicyBlock, b => b.Begin())));

    [Fact]
    public void TheRegisteredMaterialProxyExecutorAsksTheProxyArea()
    {
        var (slot, found) = Through<IClientMaterialProxyExecutor, (int, bool)>(
            "SourceSharpMaterialProxyClient001", nameof(ShimMaterialProxyClientFwd.ProxyFindVar),
            (nint)(delegate* unmanaged<void*, int, byte*, int, int*, int>)&FoundVarThree,
            x => (x.FindVar(1, "$frame", false, out bool f), f));

        Assert.Equal((3, true), (slot, found));
    }

    [UnmanagedCallersOnly]
    private static int OverlayLine(void* self, int index, float* x, float* y, int* line, uint* rgba, byte* text, int bytes)
    {
        *line = index + 1;
        return 4;
    }

    [Fact]
    public void TheRegisteredDebugOverlayAsksTheUiArea()
    {
        var (bytes, line) = Through<IClientDebugOverlay, (int, int)>(
            "SourceSharpUiClient001", nameof(ShimUiClientFwd.UiDebugOverlayText),
            (nint)(delegate* unmanaged<void*, int, float*, float*, int*, uint*, byte*, int, int>)&OverlayLine,
            o => (o.Text(2, out _, out _, out int l, out _, new byte[16]), l));

        Assert.Equal((4, 3), (bytes, line));
    }

    [ThreadStatic] private static int t_layouts;

    [UnmanagedCallersOnly] private static void CountLayout(void* self) => t_layouts++;

    [Fact]
    public void TheRegisteredClientModeLayoutCallsThroughTheUiArea()
    {
        t_layouts = 0;
        Through<IClientModeLayout, int>("SourceSharpUiClient001", nameof(ShimUiClientFwd.ClientModeLayout),
                                        (nint)(delegate* unmanaged<void*, void>)&CountLayout,
                                        l => { l.Layout(); return 0; });
        Assert.Equal(1, t_layouts);
    }

    [Fact]
    public void TheRegisteredUiSurfaceCallsThroughTheUiArea()
    {
        Assert.Equal(17, Through<IUiSurface, int>("SourceSharpUiClient001", nameof(ShimUiClientFwd.UiTextureCreate),
                                                  (nint)(delegate* unmanaged<void*, int>)&TextureSeventeen,
                                                  s => s.TextureCreate()));
    }

    /// <summary>
    /// Nothing asks whether the engine attached (plan_shim_areas_split phase 11
    /// review): over a side as its load makes it, before the attach, the
    /// registration builds every adapter, and the first engine call through one
    /// refuses, naming the area it reached. The game never composes before the
    /// attach, and a test with no engine hands the default areas instead.
    /// </summary>
    [Fact]
    public void OverAnUnattachedEngineTheFirstCallRefusesNamingTheArea()
    {
        var services = Composed(EngineSide.Over(SideAttachment.Detached(Side.Client, new SideState(Side.Client))));

        var refusal = Assert.Throws<InvalidOperationException>(() => services.Require<IUiSurface>().TextureCreate());
        Assert.Contains("SSFwd_ShimUiClient was called before the shim areas were attached", refusal.Message);
    }

    /// <summary>Over a side of the default areas every adapter answers its default.</summary>
    [Fact]
    public void OverTheDefaultAreasAnAdapterAnswersItsDefault()
        => Assert.Equal(0, Composed(TestSides.Create(Side.Client)).Require<IUiSurface>().TextureCreate());
}
