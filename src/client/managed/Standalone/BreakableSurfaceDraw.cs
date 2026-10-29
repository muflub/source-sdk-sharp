//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// What a broken func_breakable_surf draws, decided here and handed to the
/// native surface renderer (shell_surface_render.cpp) to carry out (lane M1-A;
/// the renderer read the blob and chose all of this natively until then).
///
/// <para>
/// Stock's <c>C_BreakableSurface</c> (c_func_breakablesurf.cpp):
/// <c>FindCrackedMaterial</c> and <c>OnDataChanged</c>'s material lists choose
/// the materials; <c>RenderBrushModelSurface</c>'s order (a tile's grout
/// highlights, then the panes still standing, then the cracked edges) and
/// <c>DrawSolidBlocks</c>' vertical runs of standing panes are the plan.
/// What stays native is the mesh building: each quad's corners in the
/// surface's frame (the engine's <c>VectorAngles</c>/<c>AngleVectors</c> of the
/// normal, which the server's pane grid also uses), and the brush surface's
/// texture and lightmap coordinates per vertex.
/// </para>
/// </summary>
public static class BreakableSurfaceDraw
{
    /// <summary>ShatterSurface_t's tile.</summary>
    public const int SurfaceTile = 1;

    /// <summary>The plan's header: the normal, the corner and the pane size, as float bits.</summary>
    public const int HeaderWords = 8;

    /// <summary>One entry: kind, column, row, and the kind's argument.</summary>
    public const int EntryWords = 4;

    /// <summary>A run of standing panes up a column: the argument is the run's length in rows.</summary>
    public const int KindSolid = 0;

    /// <summary>A cracked edge: the argument packs the side, the edge shape and the style.</summary>
    public const int KindEdge = 1;

    /// <summary>A tile's grout behind a fallen piece: packed as an edge.</summary>
    public const int KindHighlight = 2;

    /// <summary>The material the standing panes draw their texture from.</summary>
    public const string BoxMaterial = "models/brokenglass/glassbroken_solid";

    /// <summary>The stand-in for a surface that is not one face.</summary>
    public const string DebugMaterial = "debug/debugempty";

    // Stock's InitMaterial table: style 0 is the _03 set, 2 the _01; the shapes
    // none, full, left and right are a, b, c and d. Indexed edge * 3 + style.
    private static string[] EdgeNames(string prefix)
    {
        var names = new string[PaneEdgeShapes * PaneEdges.Styles];
        const string shapes = "abcd";
        for (int edge = 0; edge < PaneEdgeShapes; edge++)
            for (int style = 0; style < PaneEdges.Styles; style++)
                names[edge * PaneEdges.Styles + style] = $"{prefix}0{3 - style}{shapes[edge]}";
        return names;
    }

    private const int PaneEdgeShapes = 4;

    /// <summary>
    /// <c>FindCrackedMaterial</c> and the material lists: a surface that is
    /// not exactly one face draws the debug stand-in (and says so in
    /// <paramref name="warning"/>); one whose material names a
    /// <c>$crackmaterial</c> draws its pieces in that, which alone takes the
    /// detail swap; any other draws them in its own material. A tile surface
    /// gets the tile edges, everything else the glass ones.
    /// </summary>
    public static SurfaceMaterialChoice ChooseMaterials(in SurfaceMaterialFacts facts, int surfaceType, out string? warning)
    {
        warning = null;
        string pieces;
        bool swaps = false;
        bool debug = false;
        if (facts.MaterialCount != 1)
        {
            warning = $"[SourceSharp] func_breakable_surf has {facts.MaterialCount} materials, not 1; " +
                      "the cracks will not draw\n";
            pieces = DebugMaterial;
            debug = true;
        }
        else if (facts.HasCrackMaterial)
        {
            pieces = facts.CrackMaterial;
            swaps = true;
        }
        else
        {
            pieces = facts.Material;
        }

        return new SurfaceMaterialChoice(pieces, swaps, debug, BoxMaterial,
                                         EdgeNames(surfaceType == SurfaceTile ? "models/brokentile/tilebroken_"
                                                                             : "models/brokenglass/glassbroken_"));
    }

    /// <summary>The argument an edge or a highlight carries.</summary>
    public static int PackEdge(PaneSide side, PaneEdge edge, byte style)
        => (int)side | ((int)edge << 2) | (style << 4);

    /// <summary>
    /// The plan for a broken surface: the header, then (for a tile) one
    /// highlight per cracked edge, then each column's runs of standing panes,
    /// then the cracked edges in <paramref name="edges"/>' order (which
    /// <see cref="PaneEdges.Build"/> groups by material). The grid is clamped
    /// to <see cref="BreakableSurfaceNetState.MaxPanels"/>, as the renderer's
    /// own bound.
    /// </summary>
    public static int[] Plan(int wide, int high, Vector normal, Vector corner, float panelWidth, float panelHeight,
                             int surfaceType, Func<int, int, bool> standing, IReadOnlyList<PaneEdgeDraw> edges)
    {
        wide = Math.Clamp(wide, 0, BreakableSurfaceNetState.MaxPanels);
        high = Math.Clamp(high, 0, BreakableSurfaceNetState.MaxPanels);

        var words = new List<int>(HeaderWords + EntryWords * (edges.Count * 2 + wide))
        {
            Bits(normal.X), Bits(normal.Y), Bits(normal.Z),
            Bits(corner.X), Bits(corner.Y), Bits(corner.Z),
            Bits(panelWidth), Bits(panelHeight),
        };

        if (surfaceType == SurfaceTile)
        {
            foreach (PaneEdgeDraw e in edges)
                words.AddRange([KindHighlight, e.W, e.H, PackEdge(e.Side, e.Edge, e.Style)]);
        }

        for (int w = 0; w < wide; w++)
        {
            int run = 0;
            int h;
            for (h = 0; h < high; h++)
            {
                if (standing(w, h))
                {
                    run++;
                }
                else if (run > 0)
                {
                    words.AddRange([KindSolid, w, h - run, run]);
                    run = 0;
                }
            }

            if (run > 0)
                words.AddRange([KindSolid, w, h - run, run]);
        }

        foreach (PaneEdgeDraw e in edges)
            words.AddRange([KindEdge, e.W, e.H, PackEdge(e.Side, e.Edge, e.Style)]);

        return words.ToArray();
    }

    private static int Bits(float f) => BitConverter.SingleToInt32Bits(f);
}
