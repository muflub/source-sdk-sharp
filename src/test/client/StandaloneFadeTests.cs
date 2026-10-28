using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render.LeafSystem;
using SourceSharp.Client.Standalone;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// A standalone entity's blend for the view (lane M1-A: the rule
/// <c>C_StandaloneEntity::ComputeFxBlend</c> made natively), written from
/// stock's two: cdll_util.cpp's <c>ComputeDistanceFade</c> (func_lod's
/// <c>GetClientSideFade</c>) and <c>C_FuncAreaPortalWindow::GetDistanceBlend</c>;
/// and the pass that writes it into each block before the leaf system's blend
/// batch asks.
/// </summary>
public class StandaloneFadeTests
{
    private static readonly Vector Box = new(10f, 10f, 10f);

    // A box of 20 units about a centre.
    private static (Vector Mins, Vector Maxs) Around(Vector centre) => (centre - Box, centre + Box);

    private static int Lod(float near, float far, Vector eye)
    {
        var (mins, maxs) = Around(Vector.Zero);
        return StandaloneFade.Blend(FadeStyle.Lod, near, far, 0f, 255, mins, maxs, eye);
    }

    private static int Window(float start, float end, float limit, Vector eye)
    {
        var (mins, maxs) = Around(Vector.Zero);
        return StandaloneFade.Blend(FadeStyle.PortalWindow, start, end, limit, 255, mins, maxs, eye);
    }

    //=========================================================================
    // func_lod: ComputeDistanceFade.
    //=========================================================================

    [Fact]
    public void ALodInsideTheNearEndIsSolid()
        => Assert.Equal(255, Lod(100f, 200f, new Vector(50f, 0f, 0f)));

    [Fact]
    public void ALodPastTheFarEndIsGone()
        => Assert.Equal(0, Lod(100f, 200f, new Vector(250f, 0f, 0f)));

    [Fact]
    public void ALodRampsOnSquaredDistance()
    {
        // 255 / (200^2 - 100^2) * (200^2 - 150^2) = 148.75: a linear ramp would be 127.
        Assert.Equal(148, Lod(100f, 200f, new Vector(150f, 0f, 0f)));
    }

    [Fact]
    public void ALodBandGivenBackToFrontIsTheSameBand()
        => Assert.Equal(Lod(100f, 200f, new Vector(150f, 0f, 0f)), Lod(200f, 100f, new Vector(150f, 0f, 0f)));

    [Fact]
    public void ALodWithNoBandIsSolid()
        => Assert.Equal(255, Lod(0f, 0f, new Vector(5000f, 0f, 0f)));

    [Fact]
    public void ALodNegativeNearEndIsTheFarEndLessFourHundred()
    {
        // Near -1, far 500: the near end is 100, so a viewer at 50 sees it solid.
        Assert.Equal(255, Lod(-1f, 500f, new Vector(50f, 0f, 0f)));
    }

    [Fact]
    public void ALodMeasuresFromItsBoxCentre()
    {
        // The box is off the origin: what counts is how far the viewer is from its centre.
        var (mins, maxs) = Around(new Vector(1000f, 0f, 0f));
        Assert.Equal(255, StandaloneFade.Blend(FadeStyle.Lod, 100f, 200f, 0f, 255, mins, maxs, new Vector(1050f, 0f, 0f)));
    }

    //=========================================================================
    // func_areaportalwindow: GetDistanceBlend.
    //=========================================================================

    [Fact]
    public void AWindowUpCloseIsAtItsTranslucencyLimit()
    {
        // Inside the box the distance is 0, before the start: the limit, 0.25 of 255.
        Assert.Equal(64, Window(100f, 300f, 0.25f, Vector.Zero));
    }

    [Fact]
    public void AWindowPastItsFadeDistanceIsSolid()
        => Assert.Equal(255, Window(100f, 300f, 0.25f, new Vector(500f, 0f, 0f)));

    [Fact]
    public void AWindowRampsLinearlyBetween()
    {
        // 210 out from the centre is 200 from the face: halfway, 0.2 + 0.8 * 0.5 = 0.6 -> 153.
        Assert.Equal(153, Window(100f, 300f, 0.2f, new Vector(210f, 0f, 0f)));
    }

    [Fact]
    public void AWindowMeasuresToItsNearestFace()
    {
        // Off one corner: the distance is to the corner, sqrt(3) * 100 = 173.2.
        float d = StandaloneFade.DistanceToAabb(Vector.Zero - Box, Box, new Vector(110f, 110f, 110f));
        Assert.Equal(MathF.Sqrt(3f) * 100f, d, 3);
    }

    [Fact]
    public void AWindowWithAZeroWidthBandIsSolidAtItsEdge()
        => Assert.Equal(1.0f, StandaloneFade.RemapValClamped(200f, 200f, 200f, 0.25f, 1.0f));

    //=========================================================================
    // No rule.
    //=========================================================================

    [Fact]
    public void WithNoRuleTheBlendIsTheStoredAlpha()
    {
        var (mins, maxs) = Around(Vector.Zero);
        Assert.Equal(77, StandaloneFade.Blend(FadeStyle.None, 100f, 200f, 0f, 77, mins, maxs, new Vector(5000f, 0f, 0f)));
    }

    //=========================================================================
    // The pass.
    //=========================================================================

    private sealed class Blocks : IRenderPolicyBlocks
    {
        public readonly byte[][] Raw;
        public int Begins;

        public Blocks(int count)
        {
            Raw = new byte[count][];
            for (int i = 0; i < count; i++)
                Raw[i] = new byte[EngineBlock.SizeBytes];
        }

        public int Begin()
        {
            Begins++;
            return Raw.Length;
        }

        public Span<byte> Block(int index) => Raw[index];
    }

    private sealed class View(Vector origin) : ICurrentView
    {
        public int Reads;

        public CurrentViewState Read()
        {
            Reads++;
            return new CurrentViewState(0, origin, new Vector(1f, 0f, 0f), new Vector(0f, 1f, 0f), new Vector(0f, 0f, 1f));
        }
    }

    // Each handle's box is 20 units about (handle * 1000, 0, 0).
    private sealed class Bounds : IStandaloneRenderBounds
    {
        public readonly List<uint[]> Asked = [];

        public int SurroundingBounds(ReadOnlySpan<uint> handles, Span<float> minsMaxs)
        {
            Asked.Add(handles.ToArray());
            for (int i = 0; i < handles.Length; i++)
            {
                float x = handles[i] * 1000f;
                minsMaxs[6 * i + 0] = x - 10f; minsMaxs[6 * i + 1] = -10f; minsMaxs[6 * i + 2] = -10f;
                minsMaxs[6 * i + 3] = x + 10f; minsMaxs[6 * i + 4] = 10f; minsMaxs[6 * i + 5] = 10f;
            }
            return handles.Length;
        }
    }

    private static int Tail(string field)
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
         + (int)Marshal.OffsetOf<EngineBlockClientTail>(field);

    private static readonly int HandleAt = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
                                         + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Handle));

    private static void Int(byte[] b, int at, int v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);

    private static void Float(byte[] b, int at, float v) => BitConverter.TryWriteBytes(b.AsSpan(at, 4), v);

    private static int FxBlend(byte[] b) => BitConverter.ToInt32(b, Tail(nameof(EngineBlockClientTail.FxBlend)));

    private static void Fade(byte[] b, uint handle, FadeStyle style, float start, float end)
    {
        Int(b, HandleAt, (int)handle);
        Int(b, Tail(nameof(EngineBlockClientTail.FadeStyle)), (int)style);
        Float(b, Tail(nameof(EngineBlockClientTail.FadeStartDistance)), start);
        Float(b, Tail(nameof(EngineBlockClientTail.FadeEndDistance)), end);
    }

    [Fact]
    public void ThePassWritesTheStoredAlphaWhereNothingFades()
    {
        var blocks = new Blocks(1);
        blocks.Raw[0][Tail(nameof(EngineBlockClientTail.RenderColorA))] = 90;
        Int(blocks.Raw[0], Tail(nameof(EngineBlockClientTail.FxBlend)), 255);

        new StandaloneFadePass(blocks, new View(Vector.Zero), new Bounds()).BeforeBlends();

        Assert.Equal(90, FxBlend(blocks.Raw[0]));
    }

    [Fact]
    public void ThePassFadesAFadingEntityFromTheCurrentView()
    {
        // Entity 2's box is about x = 2000; the viewer stands 150 from its centre.
        var blocks = new Blocks(2);
        blocks.Raw[0][Tail(nameof(EngineBlockClientTail.RenderColorA))] = 255;
        Fade(blocks.Raw[1], 2, FadeStyle.Lod, 100f, 200f);
        var view = new View(new Vector(2150f, 0f, 0f));
        var bounds = new Bounds();

        new StandaloneFadePass(blocks, view, bounds).BeforeBlends();

        Assert.Equal(148, FxBlend(blocks.Raw[1]));
        Assert.Equal(255, FxBlend(blocks.Raw[0]));
        Assert.Equal([2u], Assert.Single(bounds.Asked));
        Assert.Equal(1, view.Reads);
    }

    [Fact]
    public void ThePassAsksForNoViewOrBoxWhenNothingFades()
    {
        var blocks = new Blocks(3);
        var view = new View(Vector.Zero);
        var bounds = new Bounds();

        new StandaloneFadePass(blocks, view, bounds).BeforeBlends();

        Assert.Equal(0, view.Reads);
        Assert.Empty(bounds.Asked);
        Assert.Equal(1, blocks.Begins);
    }

    [Fact]
    public void ThePassSkipsAShortBlock()
    {
        var blocks = new Blocks(1);
        blocks.Raw[0] = new byte[16];

        new StandaloneFadePass(blocks, new View(Vector.Zero), new Bounds()).BeforeBlends();

        Assert.All(blocks.Raw[0], b => Assert.Equal(0, b));
    }

    //=========================================================================
    // The leaf system runs the pass before each blend batch.
    //=========================================================================

    private sealed class Order(List<string> log) : IRenderableBlendPass
    {
        public void BeforeBlends() => log.Add("fade");
    }

    private sealed class Native(List<string> log) : ILeafNative
    {
        public int ComputeFxBlends(nint system, ReadOnlySpan<nint> renderables, Span<int> pending)
        {
            log.Add("blends");
            return 0;
        }

        public void Facts(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> what, Span<int> facts) => facts.Clear();

        public void Receives(ReadOnlySpan<nint> renderables, ReadOnlySpan<int> flags, Span<int> answers) => answers.Fill(1);

        public int EnumerateLeaves(ReadOnlySpan<float> boxes, Span<int> output) => 0;

        public void LeavesAreas(ReadOnlySpan<int> runs, int count, Span<int> areas) => areas[..count].Fill(1);

        public void BoxQueries(ReadOnlySpan<float> boxes, ReadOnlySpan<int> programs, Span<int> answers) => answers[..programs.Length].Clear();

        public void DetailModels(nint detailSystem, ReadOnlySpan<int> ranges, Span<nint> models) => models.Clear();
    }

    private sealed class Settings : ILeafListSettings
    {
        public bool PortalTestEnts => false;
        public bool PortalsOpenAll => false;
        public int DrawLeaf => -1;
    }

    [Fact]
    public void TheTranslucentLeafPassFadesBeforeItAsksForBlends()
    {
        var log = new List<string>();
        var registry = new LeafRegistry(new Native(log));
        registry.LevelInitPreEntity(4);
        int handle = registry.AddRenderable(0x150, 8, 1);   // a translucent entity, a brush
        registry.AddRenderableToLeaves(handle, [2]);
        var builder = new RenderableListBuilder(registry, new Settings(), new Order(log));

        builder.ComputeTranslucentRenderLeaf([2], 1, 0, 1);

        Assert.Equal(["fade", "blends"], log);
    }

    [Fact]
    public void TheViewModelPassFadesBeforeItAsksForBlends()
    {
        var log = new List<string>();
        var registry = new LeafRegistry(new Native(log));
        registry.LevelInitPreEntity(4);
        registry.AddRenderable(0x160, 10, 3);   // an opaque view model
        var builder = new RenderableListBuilder(registry, new Settings(), new Order(log));

        builder.CollateViewModelRenderables();

        Assert.Equal(["fade", "blends"], log);
    }
}
