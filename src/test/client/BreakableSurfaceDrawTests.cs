using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Client.Standalone;
using SourceSharp.Shared;

using Xunit;

namespace SourceSharp.Tests;

/// <summary>
/// What a broken func_breakable_surf draws (lane M1-A: the choices
/// shell_surface_render.cpp made natively), written from stock's
/// <c>C_BreakableSurface</c> (c_func_breakablesurf.cpp): <c>FindCrackedMaterial</c>
/// and <c>OnDataChanged</c>'s material lists, <c>RenderBrushModelSurface</c>'s
/// order and <c>DrawSolidBlocks</c>' runs; and the client entity handing the
/// choice and the plan to the native draw.
/// </summary>
public class BreakableSurfaceDrawTests
{
    private static SurfaceMaterialFacts OneFace(bool crack) =>
        new(1, "glass/window01", crack, crack ? "glass/window01_crack" : "");

    //=========================================================================
    // The materials: FindCrackedMaterial.
    //=========================================================================

    [Fact]
    public void ASurfaceThatIsNotOneFaceDrawsTheDebugStandIn()
    {
        var choice = BreakableSurfaceDraw.ChooseMaterials(new SurfaceMaterialFacts(2, "", false, ""), 0, out string? warning);

        Assert.Equal(BreakableSurfaceDraw.DebugMaterial, choice.Pieces);
        Assert.True(choice.IsDebug);
        Assert.False(choice.SwapsDetail);
        Assert.Contains("2 materials", warning);
    }

    [Fact]
    public void ANamedCrackMaterialDrawsThePiecesAndTakesTheSwap()
    {
        var choice = BreakableSurfaceDraw.ChooseMaterials(OneFace(crack: true), 0, out string? warning);

        Assert.Equal("glass/window01_crack", choice.Pieces);
        Assert.True(choice.SwapsDetail);
        Assert.Null(warning);
    }

    [Fact]
    public void WithNoCrackMaterialThePiecesAreTheSurfacesOwnWithNoSwap()
    {
        var choice = BreakableSurfaceDraw.ChooseMaterials(OneFace(crack: false), 0, out _);

        Assert.Equal("glass/window01", choice.Pieces);
        Assert.False(choice.SwapsDetail);
        Assert.False(choice.IsDebug);
    }

    [Fact]
    public void TheSolidBlockIsStocksBrokenGlassSolid()
        => Assert.Equal("models/brokenglass/glassbroken_solid",
                        BreakableSurfaceDraw.ChooseMaterials(OneFace(false), 0, out _).Box);

    [Fact]
    public void GlassGetsTheTwelveGlassEdgesStyleZeroBeingTheThirdSet()
    {
        var edges = BreakableSurfaceDraw.ChooseMaterials(OneFace(false), 0, out _).Edges;

        Assert.Equal(12, edges.Count);
        Assert.Equal("models/brokenglass/glassbroken_03a", edges[(int)PaneEdge.Flat * 3 + 0]);
        Assert.Equal("models/brokenglass/glassbroken_02b", edges[(int)PaneEdge.Full * 3 + 1]);
        Assert.Equal("models/brokenglass/glassbroken_01d", edges[(int)PaneEdge.Right * 3 + 2]);
    }

    [Fact]
    public void TileGetsTheTileEdges()
    {
        var edges = BreakableSurfaceDraw.ChooseMaterials(OneFace(false), BreakableSurfaceDraw.SurfaceTile, out _).Edges;

        Assert.Equal("models/brokentile/tilebroken_03c", edges[(int)PaneEdge.Left * 3 + 0]);
        Assert.All(edges, e => Assert.StartsWith("models/brokentile/tilebroken_", e));
    }

    //=========================================================================
    // The plan.
    //=========================================================================

    private static readonly Vector Normal = new(0f, 1f, 0f);
    private static readonly Vector Corner = new(8f, 16f, 32f);

    private static int[] Plan(int wide, int high, Func<int, int, bool> standing, IReadOnlyList<PaneEdgeDraw>? edges = null,
                              int surfaceType = 0)
        => BreakableSurfaceDraw.Plan(wide, high, Normal, Corner, 12f, 10f, surfaceType, standing, edges ?? []);

    // The entries after the header, four words each.
    private static List<int[]> Entries(int[] plan)
    {
        var entries = new List<int[]>();
        for (int i = BreakableSurfaceDraw.HeaderWords; i < plan.Length; i += BreakableSurfaceDraw.EntryWords)
            entries.Add(plan[i..(i + BreakableSurfaceDraw.EntryWords)]);
        return entries;
    }

    [Fact]
    public void ThePlanOpensWithTheSurfacesFrame()
    {
        int[] plan = Plan(1, 1, (_, _) => true);

        float[] header = plan[..BreakableSurfaceDraw.HeaderWords].Select(BitConverter.Int32BitsToSingle).ToArray();
        Assert.Equal([0f, 1f, 0f, 8f, 16f, 32f, 12f, 10f], header);
    }

    [Fact]
    public void StandingPanesAreDrawnInRunsUpEachColumn()
    {
        // One column of five with the middle pane gone: two runs, rows 0-1 and 3-4.
        int[] plan = Plan(1, 5, (_, h) => h != 2);

        Assert.Equal([[BreakableSurfaceDraw.KindSolid, 0, 0, 2], [BreakableSurfaceDraw.KindSolid, 0, 3, 2]],
                     Entries(plan));
    }

    [Fact]
    public void AColumnWithNothingStandingDrawsNoRun()
        => Assert.DoesNotContain(Entries(Plan(2, 3, (w, _) => w == 1)), e => e[1] == 0);

    [Fact]
    public void EdgesFollowTheRunsInTheOrderGiven()
    {
        var edges = new List<PaneEdgeDraw>
        {
            new(0, 1, PaneSide.Top, PaneEdge.Flat, 0),
            new(0, 1, PaneSide.Bottom, PaneEdge.Full, 2),
        };
        var entries = Entries(Plan(1, 3, (_, h) => h != 1, edges));

        Assert.Equal(BreakableSurfaceDraw.KindSolid, entries[0][0]);
        Assert.Equal([BreakableSurfaceDraw.KindEdge, 0, 1, BreakableSurfaceDraw.PackEdge(PaneSide.Top, PaneEdge.Flat, 0)],
                     entries[^2]);
        Assert.Equal([BreakableSurfaceDraw.KindEdge, 0, 1, BreakableSurfaceDraw.PackEdge(PaneSide.Bottom, PaneEdge.Full, 2)],
                     entries[^1]);
    }

    [Fact]
    public void ATileDrawsItsGroutHighlightsFirst()
    {
        var edges = new List<PaneEdgeDraw> { new(0, 1, PaneSide.Left, PaneEdge.Left, 1) };
        var entries = Entries(Plan(1, 3, (_, h) => h != 1, edges, BreakableSurfaceDraw.SurfaceTile));

        Assert.Equal(BreakableSurfaceDraw.KindHighlight, entries[0][0]);
        Assert.Equal(BreakableSurfaceDraw.KindEdge, entries[^1][0]);
    }

    [Fact]
    public void GlassDrawsNoHighlights()
    {
        var edges = new List<PaneEdgeDraw> { new(0, 1, PaneSide.Left, PaneEdge.Left, 1) };

        Assert.DoesNotContain(Entries(Plan(1, 3, (_, h) => h != 1, edges)), e => e[0] == BreakableSurfaceDraw.KindHighlight);
    }

    [Fact]
    public void TheGridIsClampedToTheLargestOne()
    {
        // A wire grid of 40 x 40 draws the first 16 columns of 16 rows.
        var entries = Entries(Plan(40, 40, (_, _) => true));

        Assert.Equal(BreakableSurfaceNetState.MaxPanels, entries.Count);
        Assert.All(entries, e => Assert.Equal(BreakableSurfaceNetState.MaxPanels, e[3]));
    }

    [Fact]
    public void AnEdgePacksItsSideShapeAndStyle()
        => Assert.Equal(3 | (2 << 2) | (1 << 4), BreakableSurfaceDraw.PackEdge(PaneSide.Left, PaneEdge.Left, 1));

    [Fact]
    public void ThePlansNumbersAreTheNativeRenderersOwn()
    {
        string header = File.ReadAllText(RepoLayout.Repo("src", "client", "native", "shell_surface_render.h"));

        int Value(string name)
        {
            Match m = Regex.Match(header, @"\b" + name + @"\s*=\s*(\d+)\s*,");
            Assert.True(m.Success, $"shell_surface_render.h names {name}");
            return int.Parse(m.Groups[1].Value);
        }

        Assert.Equal(BreakableSurfaceDraw.HeaderWords, Value("SURFACE_PLAN_HEADER_WORDS"));
        Assert.Equal(BreakableSurfaceDraw.EntryWords, Value("SURFACE_PLAN_ENTRY_WORDS"));
        Assert.Equal(BreakableSurfaceDraw.KindSolid, Value("SURFACE_PLAN_SOLID"));
        Assert.Equal(BreakableSurfaceDraw.KindEdge, Value("SURFACE_PLAN_EDGE"));
        Assert.Equal(BreakableSurfaceDraw.KindHighlight, Value("SURFACE_PLAN_HIGHLIGHT"));
    }

    //=========================================================================
    // The client entity hands the choice and the plan to the native draw.
    //=========================================================================

    private sealed class Surface : IBreakableSurfaceRenderer
    {
        public int FactsAsked;
        public readonly List<SurfaceMaterialChoice> Materials = [];
        public readonly List<int[]> Plans = [];

        public SurfaceMaterialFacts MaterialFacts(EntityHandle entity)
        {
            FactsAsked++;
            return OneFace(crack: true);
        }

        public void SetMaterials(EntityHandle entity, in SurfaceMaterialChoice choice) => Materials.Add(choice);

        public void SetPlan(EntityHandle entity, ReadOnlySpan<int> words) => Plans.Add(words.ToArray());
    }

    private static ClientBreakableSurf Glass(Surface surface)
    {
        var engine = new RecordingEngine();
        var glass = new ClientBreakableSurf(engine, surface);
        glass.State.BindLocal();
        glass.Handle = new EntityHandle(9u);
        glass.State.Wide = 2;
        glass.State.High = 2;
        glass.State.PanelWidth = 12f;
        glass.State.PanelHeight = 12f;
        glass.State.Normal = Normal;
        glass.State.FillPanes();
        return glass;
    }

    [Fact]
    public void AnUnbrokenSurfaceHandsOverNothing()
    {
        var surface = new Surface();

        Glass(surface).OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);

        Assert.Equal(0, surface.FactsAsked);
        Assert.Empty(surface.Materials);
        Assert.Empty(surface.Plans);
    }

    [Fact]
    public void ABrokenSurfaceChoosesItsMaterialsOnceAndPlansEveryUpdate()
    {
        var surface = new Surface();
        var glass = Glass(surface);
        glass.State.Broken = true;
        glass.State.SetPaneSolid(0, 0, false);

        glass.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.Created);
        glass.State.SetPaneSolid(1, 1, false);
        glass.OnDataChanged(ReadOnlySpan<byte>.Empty, DataUpdateType.DataTableChanged);

        Assert.Equal(1, surface.FactsAsked);
        Assert.Equal("glass/window01_crack", Assert.Single(surface.Materials).Pieces);
        Assert.Equal(2, surface.Plans.Count);
        Assert.Contains(Entries(surface.Plans[0]), e => e[0] == BreakableSurfaceDraw.KindEdge);
    }
}
