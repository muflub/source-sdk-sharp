using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// <see cref="ViewDrawProgram"/>'s rules that the recorded frames do not reach
/// (plan_client_novalve C7 step 2): no fixture has a translucent entity at
/// blend 0, one that ignores the z buffer, one that samples the frame buffer,
/// a detail leaf, the studio-stats entity or more than 512 static props. Each
/// fact names the viewrender.cpp function whose rule it holds.
/// </summary>
public class ViewDrawProgramTests
{
    private sealed class SceneBuilder
    {
        private readonly List<ViewSceneEntry>[] _groups = Enumerable.Range(0, ViewScene.GroupCount).Select(_ => new List<ViewSceneEntry>()).ToArray();
        private readonly List<ViewSceneLeaf> _leaves = [];

        public SceneBuilder Leaf(int leaf, bool translucent = false, bool detail = false)
        {
            var l = new ViewSceneLeaf { Leaf = leaf, ActualLeaf = _leaves.Count, Detail = detail ? 1 : 0 };
            l.SetTranslucent(0, translucent);
            l.SetTranslucent(1, translucent);
            _leaves.Add(l);
            return this;
        }

        public SceneBuilder Entry(int group, ViewSceneEntryFlags flags, int leaf = 0, int blend = 255)
        {
            var e = new ViewSceneEntry { Group = group, Flags = (int)(flags | ViewSceneEntryFlags.Renderable), Leaf = leaf, FxBlend = blend, EntIndex = 1 };
            e.SetColor(0, 1f);
            e.SetColor(1, 1f);
            e.SetColor(2, 1f);
            _groups[group].Add(e);
            return this;
        }

        public ViewSceneData Build()
        {
            var header = new ViewScene { LeafCount = _leaves.Count, EntryCount = _groups.Sum(g => g.Count), HasRenderables = 1 };
            for (int g = 0; g < ViewScene.GroupCount; g++)
                header.SetGroupCount(g, _groups[g].Count);
            return new ViewSceneData(header, [.. _leaves], [.. _groups.SelectMany(g => g)]);
        }
    }

    private static ViewDrawProgram.Context Context(int drawFlags = RenderConst.DfDrawEntities, params ViewDrawProgram.Draw[] draws)
        => new(drawFlags, draws, DrawEntities: true, DrawOpaqueRenderables: true, DrawTranslucentRenderables: true,
               DrawTranslucentWorld: true, EntityClips: true, VcollideWireframe: false,
               TranslucentFlags0: (int)RenderPlanText.EngineDrawWorldListFlags(drawFlags & ~RenderConst.DfDrawSkybox),
               TranslucentFlags1: drawFlags);

    private static readonly ViewDrawProgram.Draw Translucent = new(RenderOpKind.DrawTranslucent, 0, 0, RenderConst.ViewMain);
    private static readonly ViewDrawProgram.Draw NoZ = new(RenderOpKind.DrawNoZTranslucent, 0, 0, RenderConst.ViewMain);
    private static readonly ViewDrawProgram.Draw Opaque = new(RenderOpKind.DrawOpaque, 0, 0, RenderConst.ViewMain);

    private static List<RenderOp> Build(ViewSceneData scene, ViewDrawProgram.Context context)
    {
        var b = new RenderPlanBuilder();
        ViewDrawProgram.Build(scene, context, b);
        return [.. b.Ops];
    }

    private static int Count(List<RenderOp> ops, RenderOpKind kind) => ops.Count(o => o.Kind == (int)kind);

    [Fact]
    public void ATranslucentEntityAtBlendZeroIsNotDrawn()
    {
        // DrawTranslucentRenderable: `if ( blend <= 0.0f ) return;`
        var scene = new SceneBuilder().Leaf(1).Entry(ViewDrawProgram.GroupTranslucentEntity, 0, blend: 0)
                                      .Entry(ViewDrawProgram.GroupTranslucentEntity, 0, blend: 128).Build();
        var ops = Build(scene, Context(draws: Translucent));
        Assert.Equal(1, Count(ops, RenderOpKind.DrawEntity));
        Assert.Equal(1, ops.Single(o => o.Kind == (int)RenderOpKind.DrawEntity).GetI(0));
    }

    [Fact]
    public void AnEntityThatIgnoresTheZBufferDrawsOnlyInTheNoZPass()
    {
        // DrawTranslucentRenderable: `if ( pEnt->IgnoresZBuffer() != bIgnoreDepth ) return;`
        var scene = new SceneBuilder().Leaf(1).Entry(ViewDrawProgram.GroupTranslucentEntity, ViewSceneEntryFlags.IgnoresZ).Build();
        var ops = Build(scene, Context(draws: [Translucent, NoZ]));
        int end = ops.FindIndex(o => o.Kind == (int)RenderOpKind.SectionEnd);
        Assert.Equal(0, Count(ops[..end], RenderOpKind.DrawEntity));
        Assert.Equal(1, Count(ops[end..], RenderOpKind.DrawEntity));
    }

    [Fact]
    public void AFrameBufferSamplerIsSkippedInAWaterTargetsView()
    {
        // DrawTranslucentRenderables: `if( bRenderingWaterRenderTargets ) continue;`
        var scene = new SceneBuilder().Leaf(1).Entry(ViewDrawProgram.GroupTranslucentEntity, ViewSceneEntryFlags.PowerOfTwoFrameBuffer).Build();
        var inWater = Build(scene, Context(RenderConst.DfDrawEntities | RenderConst.DfRenderReflection, Translucent));
        var main = Build(scene, Context(draws: Translucent));
        Assert.Equal(0, Count(inWater, RenderOpKind.DrawEntity));
        Assert.Equal(1, Count(main, RenderOpKind.DrawEntity));
        Assert.Equal(1, Count(main, RenderOpKind.FrameBufferForEntity));
    }

    [Fact]
    public void TheLeavesDrawFromTheBackWithTheirEntitiesBetween()
    {
        // DrawTranslucentRenderables: leaves from the last to the entity's, the
        // entity, then the rest down to leaf 0.
        var scene = new SceneBuilder().Leaf(10, translucent: true).Leaf(11, translucent: true).Leaf(12, translucent: true)
                                      .Entry(ViewDrawProgram.GroupTranslucentEntity, 0, leaf: 1).Build();
        var ops = Build(scene, Context(draws: Translucent));
        var order = ops.Where(o => o.Kind is (int)RenderOpKind.TranslucentSurfaces or (int)RenderOpKind.DrawEntity)
                       .Select(o => o.Kind == (int)RenderOpKind.DrawEntity ? "e" : $"l{o.GetI(0)}").ToList();
        Assert.Equal(["l2", "l1", "e", "l0"], order);
    }

    [Fact]
    public void ADetailLeafDrawsWithItsEntitiesNotBeforeThem()
    {
        // DrawTranslucentRenderables' bDrawDetailProps arm.
        var scene = new SceneBuilder().Leaf(10, detail: true).Leaf(11, detail: true)
                                      .Entry(ViewDrawProgram.GroupTranslucentEntity, 0, leaf: 1).Build();
        var ops = Build(scene, Context(draws: Translucent));
        Assert.Equal(2, Count(ops, RenderOpKind.DetailRenderInLeaf));
        Assert.All(ops.Where(o => o.Kind == (int)RenderOpKind.DetailRenderInLeaf), o => Assert.Equal(11, o.GetI(0)));
        Assert.DoesNotContain(ops.TakeWhile(o => o.Kind != (int)RenderOpKind.DrawEntity),
                              o => o.Kind == (int)RenderOpKind.DetailLeaf && o.GetI(0) == 11);
    }

    [Fact]
    public void MoreThan512StaticPropsDrawInBatchesOf512()
    {
        // DrawOpaqueRenderables_DrawStaticProps' MAX_STATICS_PER_BATCH.
        var b = new SceneBuilder().Leaf(1);
        for (int i = 0; i < 600; i++)
            b.Entry(ViewDrawProgram.GroupOpaqueStaticHuge, 0);
        var ops = Build(b.Build(), Context(draws: Opaque));
        Assert.Equal(2, Count(ops, RenderOpKind.DrawStaticPropsBatch));
        Assert.Equal(600, Count(ops, RenderOpKind.StaticProp));
    }

    [Fact]
    public void TheStudioStatsEntityIsDrawnAloneOnlyInTheMainView()
    {
        // DrawOpaqueRenderables_DrawStaticProps: g_pStudioStatsEntity in VIEW_MAIN.
        var scene = new SceneBuilder().Leaf(1).Entry(ViewDrawProgram.GroupOpaqueStaticHuge, ViewSceneEntryFlags.StudioStats).Build();
        var main = Build(scene, Context(draws: Opaque));
        var sky = Build(scene, Context(draws: new ViewDrawProgram.Draw(RenderOpKind.DrawOpaque, 0, 0, RenderConst.View3DSky)));
        Assert.Equal(ViewDrawProgram.StudioGenerateStats | ViewDrawProgram.StudioRender,
                     main.Single(o => o.Kind == (int)RenderOpKind.DrawEntity).GetI(1));
        Assert.Equal(1, Count(sky, RenderOpKind.StaticProp));
    }

    [Fact]
    public void EveryDrawOfTheViewIsOneSection()
    {
        var scene = new SceneBuilder().Leaf(1).Build();
        var ops = Build(scene, Context(draws: [Opaque, Translucent, NoZ]));
        Assert.Equal(3, Count(ops, RenderOpKind.SectionEnd));
    }
}
