//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;
using SourceSharp.Abi;

namespace SourceSharp.Client.Ropes;

/// <summary>
/// One point of a camera-facing strip: tier2's <c>BeamSeg_t</c>, field for
/// field, because the native executor hands an array of these straight to
/// <c>CBeamSegDraw</c>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BeamSeg
{
    public Vector Pos;
    public Vector Color;
    public float TexCoord;
    public float Width;
    public float Alpha;
}

/// <summary>What <c>BuildRope</c> reads besides the rope's nodes.</summary>
public readonly record struct RopeBuildView(
    Vector ViewForward, Vector ViewOrigin, int ScreenWidth, bool FakeAA, bool FullBright, int SubdivConVar,
    float SmoothEnlarge, float SmoothMinAlpha, float SmoothMaxAlpha, float SmoothMinWidth, float SmoothMaxAlphaWidth)
{
    /// <summary>The rope ConVars at their c_rope.cpp defaults, for one view.</summary>
    public static RopeBuildView WithDefaults(in Vector forward, in Vector origin, int screenWidth, bool fakeAA)
        => new(forward, origin, screenWidth, fakeAA, false, 2, 1.4f, 0.2f, 0.5f, 0.3f, 1.75f);
}

/// <summary>The inputs of one rope's build (stock's <c>BuildRopeQueuedData_t</c>
/// plus the members <c>BuildRope</c> reads off the rope).</summary>
public readonly ref struct RopeBuildInput
{
    public ReadOnlySpan<Vector> Predicted { get; init; }
    public ReadOnlySpan<Vector> LightValues { get; init; }
    public Vector ColorMod { get; init; }
    public float RopeLength { get; init; }
    public float Slack { get; init; }
    public float CurScroll { get; init; }
    public float Width { get; init; }
    public float TextureScale { get; init; }
    public int TextureHeight { get; init; }
    public int Subdiv { get; init; }
    public RopeFlags Flags { get; init; }
}

/// <summary>
/// <c>C_RopeKeyframe::BuildRope</c>: a rope's nodes into the strip that is
/// drawn, Catmull-Rom subdivided, lit, textured along its length and (in the
/// FakeAA arm) widened to a minimum screen width with a translucent back pass.
/// Also <c>RenderSolidRopes</c>' solid-pass remap, which is arithmetic too.
/// </summary>
public static class RopeBuilder
{
    /// <summary><c>MAX_ROPE_SUBDIVS</c>.</summary>
    public const int MaxSubdivs = 8;

    /// <summary><c>MAX_ROPE_SEGMENTS</c>: nodes plus every subdivision between them.</summary>
    public const int MaxSegments = RopePhysics.MaxNodes + (RopePhysics.MaxNodes - 1) * MaxSubdivs;

    private const int SlackFudge = -100;

    // g_BarbedSubdivs: three, each a double literal rounded once to float.
    private const int BarbedSubdivCount = 3;

    private static Vector BarbedSubdiv(int i) => i switch
    {
        0 => new((float)1.5, (float)(1.5 * 1.5), (float)(1.5 * 1.5 * 1.5)),
        1 => new((float)-0.5, (float)(-0.5 * -0.5), (float)(-0.5 * -0.5 * -0.5)),
        _ => new((float)0.5, (float)(0.5 * 0.5), (float)(0.5 * 0.5 * 0.5)),
    };

    /// <summary>g_RopeSubdivs[subdiv][i] = (t, t², t³), t = (i + 1) / (subdiv + 1).</summary>
    public static Vector SubdivVector(int subdiv, int i)
    {
        float t = (float)(i + 1) / (subdiv + 1);
        return new Vector(t, t * t, t * t * t);
    }

    /// <summary>Builds the strip. Returns the segment count; fills
    /// <paramref name="segments"/> and <paramref name="backWidths"/>.</summary>
    public static int Build(in RopeBuildInput rope, in RopeBuildView view,
                            Span<BeamSeg> segments, Span<float> backWidths, out float maxBackWidth)
    {
        int nodeCount = rope.Predicted.Length;

        bool barbed = (rope.Flags & RopeFlags.Barbed) != 0;
        int subdivCount;
        int subdivRow = 0;

        if (barbed)
        {
            subdivCount = BarbedSubdivCount;
        }
        else
        {
            int subdiv = rope.Subdiv;
            if (subdiv == 255)
                subdiv = view.SubdivConVar;
            if (subdiv >= MaxSubdivs)
                subdiv = MaxSubdivs - 1;

            subdivCount = subdiv;
            subdivRow = subdiv;
        }

        int segmentCount = 0;
        int prevNode = 0;
        float subdivScale = 1.0f / (subdivCount + 1);
        int lastNode = nodeCount - 1;
        Vector colorMod = rope.ColorMod;

        for (int node = 0; node < nodeCount; node++)
        {
            Vector light = view.FullBright ? new Vector(1, 1, 1) : rope.LightValues[node];

            segments[segmentCount].Pos = rope.Predicted[node];
            segments[segmentCount].Color = RopeMath.Mul(light, colorMod);
            segmentCount++;

            if (node < lastNode)
            {
                int next = node + 1;
                int nextNext = node + 2;

                if (next >= nodeCount)
                {
                    next = nextNext = lastNode;
                }
                else if (nextNext >= nodeCount)
                {
                    nextNext = lastNode;
                }

                Vector lightNext = view.FullBright ? new Vector(1, 1, 1) : rope.LightValues[node + 1];
                Vector colorInc = subdivScale * RopeMath.Mul(lightNext - light, colorMod);

                CatmullRom(rope.Predicted[prevNode], rope.Predicted[node],
                           rope.Predicted[next], rope.Predicted[nextNext],
                           out Vector t3, out Vector t2, out Vector t1, out Vector c);

                for (int s = 0; s < subdivCount; s++)
                {
                    Vector tv = barbed ? BarbedSubdiv(s) : SubdivVector(subdivRow, s);

                    segments[segmentCount].Color = segments[segmentCount - 1].Color + colorInc;
                    segments[segmentCount].Pos = c + (tv.X * t1) + (tv.Y * t2) + (tv.Z * t3);
                    segmentCount++;
                }

                prevNode = node;
            }
        }

        maxBackWidth = 0;

        float pixelsPerInch = 4.0f / rope.TextureScale;
        float totalTexCoord = pixelsPerInch * (rope.RopeLength + rope.Slack + SlackFudge);
        int totalPoints = (nodeCount - 1) * subdivCount + 1;
        float actualInc = (totalTexCoord / totalPoints) / (float)rope.TextureHeight;

        float texCoord = rope.CurScroll;

        if (view.FakeAA)
        {
            float screenWidth = view.ScreenWidth;
            float halfScreenWidth = screenWidth / 2.0f;

            for (int i = 0; i < segmentCount; i++)
            {
                ref BeamSeg seg = ref segments[i];
                seg.TexCoord = texCoord;

                float z = view.ViewForward.Dot(seg.Pos - view.ViewOrigin);
                z = z > 0.1f ? z : 0.1f;

                float screenSpaceWidth = rope.Width * halfScreenWidth / z;

                if (screenSpaceWidth < view.SmoothMinWidth)
                {
                    seg.Alpha = view.SmoothMinAlpha;
                    seg.Width = view.SmoothMinWidth * z / halfScreenWidth;
                    backWidths[i] = 0.0f;
                }
                else
                {
                    seg.Alpha = screenSpaceWidth > view.SmoothMaxAlphaWidth
                        ? view.SmoothMaxAlpha
                        : RopeMath.RemapVal(screenSpaceWidth, view.SmoothMinWidth, view.SmoothMaxAlphaWidth,
                                            view.SmoothMinAlpha, view.SmoothMaxAlpha);

                    seg.Width = rope.Width;
                    backWidths[i] = rope.Width - (z * view.SmoothEnlarge) / screenWidth;

                    if (backWidths[i] < 0.0f)
                        backWidths[i] = 0.0f;
                    else
                        maxBackWidth = maxBackWidth > backWidths[i] ? maxBackWidth : backWidths[i];
                }

                texCoord += actualInc;
            }
        }
        else
        {
            for (int i = 0; i < segmentCount; i++)
            {
                ref BeamSeg seg = ref segments[i];
                seg.TexCoord = texCoord;
                seg.Alpha = 0.3f;
                seg.Width = rope.Width;
                backWidths[i] = -1.0f;

                texCoord += actualInc;
            }
        }

        return segmentCount;
    }

    /// <summary><c>Catmull_Rom_Spline_Matrix</c>: the four basis terms.</summary>
    private static void CatmullRom(in Vector p1, in Vector p2, in Vector p3, in Vector p4,
                                   out Vector t3, out Vector t2, out Vector t1, out Vector c)
    {
        t3 = 0.5f * ((-1f * p1) + (3f * p2) + (-3f * p3) + p4);
        t2 = 0.5f * ((2f * p1) + (-5f * p2) + (4f * p3) - p4);
        t1 = 0.5f * ((-1f * p1) + p3);
        c = p2;
    }

    /// <summary>
    /// <c>RenderSolidRopes</c>' FakeAA arm: the solid pass is drawn at the back
    /// widths with alpha remapped from them. Returns false for a rope that pass
    /// skips (min alpha zero and nothing wider than the min width).
    /// </summary>
    public static bool SolidPass(ReadOnlySpan<BeamSeg> built, ReadOnlySpan<float> backWidths, float maxBackWidth,
                                 float solidMinWidth, float solidMaxWidth, float solidMinAlpha, float solidMaxAlpha,
                                 Span<BeamSeg> solid)
    {
        if (solidMinAlpha == 0.0f && maxBackWidth <= solidMinWidth)
            return false;

        for (int i = 0; i < built.Length; i++)
        {
            solid[i] = built[i];
            solid[i].Width = backWidths[i];
            solid[i].Alpha = RopeMath.RemapVal(solid[i].Width, solidMinWidth, solidMaxWidth, solidMinAlpha, solidMaxAlpha);
            solid[i].Alpha = Math.Clamp(solid[i].Alpha, 0.0f, 1.0f);
        }

        return true;
    }
}
