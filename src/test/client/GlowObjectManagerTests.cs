using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Render;

using Xunit;
using SourceSharp.Client;

namespace SourceSharp.Tests;

/// <summary>
/// The glow-object registry and the outline pass (glow_outline_effect.h/.cpp):
/// the free-list's head reuse, the ShouldDraw gate's halves, the mark pass'
/// blend-zero stencil marks, the blob half's colour-times-alpha, and the
/// composite's quarter-size halo blit that tests the marked pixels out.
/// </summary>
public class GlowObjectManagerTests
{
    private static readonly EntityHandle Player = new(0x0002_0004u);
    private static readonly EntityHandle Other = new(0x0003_0005u);
    private static readonly Vector Color = new(0.5f, 0.4f, 0.2f);

    private const int HaloId = 7;

    private static GlowObjectManager New(
        Func<EntityHandle, bool>? dormant = null,
        string? enable = null,
        Func<string, int>? material = null)
        => new(dormant ?? (_ => false), DeclaredConVars.Holding<GlowOutlineEffectEnableConVar>(enable), material ?? (_ => HaloId));

    private static ViewSceneData Scene(params int[] entIndices)
    {
        var entries = new ViewSceneEntry[entIndices.Length];
        for (int i = 0; i < entIndices.Length; i++)
            entries[i] = new ViewSceneEntry { Group = ViewDrawProgram.GroupOpaqueEntityHuge, Flags = (int)ViewSceneEntryFlags.Renderable, EntIndex = entIndices[i] };
        return new ViewSceneData(new ViewScene { LeafCount = 0, EntryCount = entIndices.Length, HasRenderables = 1 }, [], entries);
    }

    private static List<RenderOp> Render(GlowObjectManager manager, ViewSceneData scene, int dxLevel = 80, int slot = 0)
    {
        var b = new RenderPlanBuilder();
        manager.RenderGlowEffects(scene, 640, 480, dxLevel, slot, b);
        return [.. b.Ops];
    }

    private static int IndexOf(List<RenderOp> ops, RenderOpKind kind) => ops.FindIndex(o => o.Kind == (int)kind);

    private static int LastIndexOf(List<RenderOp> ops, RenderOpKind kind) => ops.FindLastIndex(o => o.Kind == (int)kind);

    [Fact]
    public void RegisteringAfterAnUnregisterTakesTheFreedHeadAndTheArrayDoesNotGrow()
    {
        var manager = New();

        Assert.Equal(0, manager.RegisterGlowObject(Player, Color, 1f, true, false));
        int second = manager.RegisterGlowObject(Other, Color, 1f, true, false);
        Assert.Equal(1, second);

        manager.UnregisterGlowObject(second);
        Assert.Equal(second, manager.RegisterGlowObject(Player, Color, 1f, true, false));
        Assert.Equal(2, manager.Count);
    }

    [Fact]
    public void TheMarkPassDrawsWithBlendingOffInsideStencilMarksBeforeAnyColour()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        var ops = Render(manager, Scene(4));

        Assert.Equal(0f, ops[0].GetF(0));
        // The depth override (test on, write off) over the mark's two loops.
        Assert.Equal((int)RenderOpKind.PostOverrideDepth, ops[1].Kind);
        Assert.Equal(1, ops[1].GetI(0));
        Assert.Equal(0, ops[1].GetI(1));

        // The occluded-only arm's stencil, then the draw.
        int mark = IndexOf(ops, RenderOpKind.DrawEntity);
        Assert.Equal(5, ops[mark - 1].GetI(0));
        Assert.Equal(0, ops[mark].GetI(0));
        Assert.Equal(ViewDrawProgram.StudioRender, ops[mark].GetI(1));

        // Stock's second loop warms the occluded-only's visible pixels.
        int warm = ops.FindIndex(mark + 1, o => o.Kind == (int)RenderOpKind.DrawEntity);
        Assert.Equal(7, ops[warm - 1].GetI(0));
        Assert.Equal(0, ops[warm].GetI(0));
        Assert.Equal(ViewDrawProgram.StudioRender, ops[warm].GetI(1));

        // The override lifts before the blob's colour.
        Assert.Equal((int)RenderOpKind.PostOverrideDepth, ops[warm + 1].Kind);
        Assert.Equal(0, ops[warm + 1].GetI(0));
        Assert.True(ops.FindIndex(o => o.Kind == (int)RenderOpKind.SetColorModulation) > warm);
    }

    [Fact]
    public void TheBlobHalfDrawsTheColorTimesItsAlphaAtItsAlpha()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 0.5f, true, false);

        var ops = Render(manager, Scene(4));

        int blob = LastIndexOf(ops, RenderOpKind.DrawEntity);
        Assert.True(blob > IndexOf(ops, RenderOpKind.DrawEntity));
        var tint = ops[blob - 1];
        Assert.Equal((int)RenderOpKind.SetColorModulation, tint.Kind);
        Assert.Equal(0.25f, tint.GetF(0));
        Assert.Equal(0.2f, tint.GetF(1));
        Assert.Equal(0.1f, tint.GetF(2));
        Assert.Equal(0.5f, ops[blob - 2].GetF(0));
    }

    [Fact]
    public void TheCompositeBlitsTheQuarterSizeHaloOverTheUnmarkedPixels()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        var ops = Render(manager, Scene(4));

        int rect = IndexOf(ops, RenderOpKind.PostDrawRect);
        var halo = ops[rect];
        Assert.Equal(HaloId, halo.GetI(0));
        Assert.Equal(0, halo.GetI(1));
        Assert.Equal(0, halo.GetI(2));
        Assert.Equal(640, halo.GetI(3));
        Assert.Equal(480, halo.GetI(4));
        Assert.Equal(160, halo.GetI(5));
        Assert.Equal(120, halo.GetI(6));
        Assert.Equal(0f, halo.GetF(0));
        Assert.Equal(-0.5f, halo.GetF(1));
        Assert.Equal(159f, halo.GetF(2));
        Assert.Equal(119f, halo.GetF(3));
        Assert.Equal(0f, halo.GetF(6));

        int weight = IndexOf(ops, RenderOpKind.PostMaterialVarFloat);
        Assert.Equal(HaloId, ops[weight].GetI(0));
        Assert.Equal((int)PostVar.C0X, ops[weight].GetI(1));
        Assert.Equal(1f, ops[weight].GetF(0));

        int test = IndexOf(ops[weight..], RenderOpKind.PostStencil) + weight;
        Assert.True(weight < test && test < rect);
        Assert.Equal(3, ops[test].GetI(0));
        Assert.Equal(0, ops[^1].GetI(0));
    }

    [Fact]
    public void TheHaloMaterialIsAskedForOnceAcrossFrames()
    {
        var names = new List<string>();
        var manager = New(material: name => { names.Add(name); return HaloId; });
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        Render(manager, Scene(4));
        Render(manager, Scene(4));

        Assert.Single(names);
        Assert.Equal("dev/halo_add_to_screen", names[0]);
    }

    [Fact]
    public void AConvarOfZeroDrawsNothingAndANullOneLeavesGlowOn()
    {
        var off = New(enable: "0");
        off.RegisterGlowObject(Player, Color, 1f, true, false);
        Assert.Empty(Render(off, Scene(4)));

        var missing = New();
        missing.RegisterGlowObject(Player, Color, 1f, true, false);
        Assert.NotEmpty(Render(missing, Scene(4)));
    }

    [Fact]
    public void AConvarOfOneDrawsTheGlowLikeAnyNonZero()
    {
        var manager = New(enable: "1");
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        Assert.NotEmpty(Render(manager, Scene(4)));
    }

    [Fact]
    public void ADormantPlayerDrawsNothingAndEvenAsksNoMaterial()
    {
        var names = new List<string>();
        var manager = New(dormant: _ => true, material: name => { names.Add(name); return HaloId; });
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        Assert.Empty(Render(manager, Scene(4)));
        Assert.Empty(names);
    }

    [Fact]
    public void AnObjectForOneSplitScreenSlotDrawsOnlyInItAndForEverySlotDrawsAnywhere()
    {
        var forOne = New();
        forOne.RegisterGlowObject(Player, Color, 1f, true, false, splitScreenSlot: 1);
        Assert.Empty(Render(forOne, Scene(4)));
        Assert.NotEmpty(Render(forOne, Scene(4), slot: 1));

        var forAll = New();
        forAll.RegisterGlowObject(Player, Color, 1f, true, false);
        Assert.NotEmpty(Render(forAll, Scene(4), slot: 5));
    }

    [Fact]
    public void NeitherRenderFlagDrawsNothing()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 1f, false, false);

        Assert.Empty(Render(manager, Scene(4)));
    }

    [Fact]
    public void AnEntityMissingFromTheSceneListStillCompositesButDrawsNoModel()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        var ops = Render(manager, Scene());

        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.DrawEntity);
        Assert.Contains(ops, o => o.Kind == (int)RenderOpKind.PostDrawRect);
    }

    [Fact]
    public void HasGlowEffectFollowsTheRegistrationInTheSlot()
    {
        var manager = New();
        Assert.False(manager.HasGlowEffect(Player));

        int slot = manager.RegisterGlowObject(Player, Color, 1f, true, false);
        Assert.True(manager.HasGlowEffect(Player));
        Assert.False(manager.HasGlowEffect(Other));

        manager.UnregisterGlowObject(slot);
        Assert.False(manager.HasGlowEffect(Player));
    }

    [Fact]
    public void TheGlowRunsInsideTheLastSectionOfTheMainView()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        var context = new ViewDrawProgram.Context(RenderConst.DfDrawEntities,
            [new ViewDrawProgram.Draw(RenderOpKind.DrawOpaque, 0, 0, RenderConst.ViewMain),
             new ViewDrawProgram.Draw(RenderOpKind.DrawTranslucent, 0, 0, RenderConst.ViewMain)],
            DrawEntities: true, DrawOpaqueRenderables: true, DrawTranslucentRenderables: true, DrawTranslucentWorld: false,
            EntityClips: false, VcollideWireframe: false, TranslucentFlags0: 0, TranslucentFlags1: 0,
            DxLevel: 80, ViewWidth: 640, ViewHeight: 480);
        var b = new RenderPlanBuilder();
        ViewDrawProgram.Build(ViewSceneData.FromBytes([]) ?? new ViewSceneData(new ViewScene(), [], []), context, b, manager);

        List<RenderOp> ops = [.. b.Ops];
        int firstEnd = IndexOf(ops, RenderOpKind.SectionEnd);
        int glow = IndexOf(ops, RenderOpKind.PostStencil);
        Assert.True(glow > firstEnd);
        Assert.True(glow < ops.FindLastIndex(o => o.Kind == (int)RenderOpKind.SectionEnd));
    }

    [Fact]
    public void AShadowDepthViewGlowsNothing()
    {
        var manager = New();
        manager.RegisterGlowObject(Player, Color, 1f, true, false);

        var context = new ViewDrawProgram.Context(RenderConst.DfDrawEntities,
            [new ViewDrawProgram.Draw(RenderOpKind.DrawOpaque, ViewDrawProgram.DepthModeShadow, 0, RenderConst.ViewShadowDepthTexture)],
            DrawEntities: true, DrawOpaqueRenderables: true, DrawTranslucentRenderables: true, DrawTranslucentWorld: false,
            EntityClips: false, VcollideWireframe: false, TranslucentFlags0: 0, TranslucentFlags1: 0,
            DxLevel: 80, ViewWidth: 640, ViewHeight: 480);
        var b = new RenderPlanBuilder();
        ViewDrawProgram.Build(ViewSceneData.FromBytes([]) ?? new ViewSceneData(new ViewScene(), [], []), context, b, manager);

        List<RenderOp> ops = [.. b.Ops];
        Assert.DoesNotContain(ops, o => o.Kind == (int)RenderOpKind.PostStencil);
    }
}
