//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using System.Runtime.InteropServices;
using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// A standalone entity's blend for the view, from the distance to the viewer
/// (the decision <c>C_StandaloneEntity::ComputeFxBlend</c> made natively
/// until lane M1-A).
///
/// <para>
/// Two rules, because stock has two and they are not variations on one
/// another:
/// </para>
/// <list type="bullet">
/// <item><see cref="FadeStyle.Lod"/>: cdll_util.cpp's <c>ComputeDistanceFade</c>,
/// which func_lod's <c>GetClientSideFade</c> reaches through
/// <c>UTIL_ComputeEntityFade</c>. It ramps on SQUARED distance to the entity's
/// centre, 255 down to 0.</item>
/// <item><see cref="FadeStyle.PortalWindow"/>:
/// <c>C_FuncAreaPortalWindow::GetDistanceBlend</c>. It ramps LINEARLY on the
/// distance to the nearest face, upward from the translucency limit to solid.</item>
/// </list>
///
/// <para>
/// With no rule the blend is the alpha <c>SetBlending</c> stored. Both stock
/// rules multiply the distance by the local player's
/// <c>GetFOVDistanceAdjustFactor</c>; the managed player has no zoom, and the
/// factor of an unzoomed player is exactly 1, so it is not applied.
/// <c>UTIL_ComputeEntityFade</c>'s screen-size fade is driven by a per-model
/// fade scale nothing on this hierarchy carries, and is not applied either.
/// </para>
/// </summary>
public static class StandaloneFade
{
    /// <summary>
    /// The blend, 0..255, an entity with this rule and band draws at from
    /// <paramref name="viewOrigin"/>, given its world-space surrounding box.
    /// </summary>
    public static int Blend(FadeStyle style, float startDistance, float endDistance, float minimumAlpha,
                            byte renderAlpha, Vector mins, Vector maxs, Vector viewOrigin)
    {
        switch (style)
        {
            case FadeStyle.Lod:
            {
                Vector centre = (mins + maxs) * 0.5f;
                return DistanceFade(startDistance, endDistance, (viewOrigin - centre).LengthSquared);
            }

            case FadeStyle.PortalWindow:
            {
                float distance = DistanceToAabb(mins, maxs, viewOrigin);
                float blend = RemapValClamped(distance, startDistance, endDistance, minimumAlpha, 1.0f);
                return Math.Clamp((int)(blend * 255.0f + 0.5f), 0, 255);
            }

            default:
                return renderAlpha;
        }
    }

    /// <summary>
    /// cdll_util.cpp's <c>ComputeDistanceFade</c> over a squared distance: 255
    /// inside the band's near end, 0 past its far end, and a ramp on squared
    /// distance between.
    /// </summary>
    public static int DistanceFade(float minDistance, float maxDistance, float distanceSquared)
    {
        if (minDistance <= 0 && maxDistance <= 0)
            return 255;

        if (minDistance > maxDistance)
            (minDistance, maxDistance) = (maxDistance, minDistance);

        // A negative near end is based off the far one.
        if (minDistance < 0)
        {
            minDistance = maxDistance - 400;
            if (minDistance < 0)
                minDistance = 0;
        }

        minDistance *= minDistance;
        maxDistance *= maxDistance;

        if (distanceSquared <= minDistance)
            return 255;

        if (distanceSquared >= maxDistance)
            return 0;

        float falloff = 255.0f / (maxDistance - minDistance);
        int alpha = (int)(falloff * (maxDistance - distanceSquared));
        return Math.Clamp(alpha, 0, 255);
    }

    /// <summary>mathlib's <c>CalcDistanceToAABB</c>: 0 inside the box, else the distance to its nearest point.</summary>
    public static float DistanceToAabb(Vector mins, Vector maxs, Vector point)
    {
        float squared = Axis(point.X, mins.X, maxs.X) + Axis(point.Y, mins.Y, maxs.Y) + Axis(point.Z, mins.Z, maxs.Z);
        return MathF.Sqrt(squared);
    }

    private static float Axis(float p, float min, float max)
    {
        if (p < min)
            return (min - p) * (min - p);
        if (p > max)
            return (p - max) * (p - max);
        return 0f;
    }

    /// <summary>mathlib.h's <c>RemapValClamped</c>.</summary>
    public static float RemapValClamped(float val, float a, float b, float c, float d)
    {
        if (a == b)
            return val >= b ? d : c;
        float t = Math.Clamp((val - a) / (b - a), 0.0f, 1.0f);
        return c + (d - c) * t;
    }
}

/// <summary>
/// The pass that writes every standalone entity's <c>FxBlend</c> into its
/// block for the view about to be collated, before the leaf system's blend
/// batch asks each renderable (<see cref="Render.LeafSystem.RenderableListBuilder"/>):
/// the stored alpha for an entity with no fade rule, <see cref="StandaloneFade"/>'s
/// answer from the current view's origin for one with a rule. Per view, as the
/// native <c>ComputeFxBlend</c> it replaces was asked per view.
///
/// <para>
/// One crossing for the block list, and only when some entity fades, one for
/// the current view and one for the fading entities' boxes.
/// </para>
/// </summary>
public sealed class StandaloneFadePass : Render.LeafSystem.IRenderableBlendPass
{
    private readonly IRenderPolicyBlocks _blocks;
    private readonly ICurrentView _view;
    private readonly IStandaloneRenderBounds _bounds;

    // Scratch: the fading entities' block indices and handles, and their boxes.
    private readonly List<int> _fading = [];
    private uint[] _handles = new uint[16];
    private float[] _boxes = new float[16 * 6];

    public StandaloneFadePass(IRenderPolicyBlocks blocks, ICurrentView view, IStandaloneRenderBounds bounds)
    {
        _blocks = blocks;
        _view = view;
        _bounds = bounds;
    }

    /// <summary>Passes run, for the facts and the report.</summary>
    public long Passes { get; private set; }

    /// <summary>The entities the last pass faded by distance.</summary>
    public int LastFaded { get; private set; }

    private static int Tail(string field)
        => (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Client))
         + (int)Marshal.OffsetOf<EngineBlockClientTail>(field);

    private static readonly int HandleOffset = (int)Marshal.OffsetOf<EngineBlock>(nameof(EngineBlock.Head))
                                             + (int)Marshal.OffsetOf<EngineBlockHead>(nameof(EngineBlockHead.Handle));
    private static readonly int FxBlendOffset = Tail(nameof(EngineBlockClientTail.FxBlend));
    private static readonly int RenderAlphaOffset = Tail(nameof(EngineBlockClientTail.RenderColorA));
    private static readonly int FadeStyleOffset = Tail(nameof(EngineBlockClientTail.FadeStyle));
    private static readonly int FadeStartOffset = Tail(nameof(EngineBlockClientTail.FadeStartDistance));
    private static readonly int FadeEndOffset = Tail(nameof(EngineBlockClientTail.FadeEndDistance));
    private static readonly int FadeMinAlphaOffset = Tail(nameof(EngineBlockClientTail.FadeMinAlpha));

    public void BeforeBlends()
    {
        Passes++;
        _fading.Clear();
        int count = _blocks.Begin();
        for (int i = 0; i < count; i++)
        {
            Span<byte> block = _blocks.Block(i);
            if (block.Length < EngineBlock.SizeBytes)
                continue;
            if (ReadInt(block, FadeStyleOffset) == (int)FadeStyle.None)
                WriteInt(block, FxBlendOffset, block[RenderAlphaOffset]);
            else
                _fading.Add(i);
        }

        LastFaded = _fading.Count;
        if (_fading.Count == 0)
            return;

        int n = _fading.Count;
        if (_handles.Length < n)
        {
            _handles = new uint[n];
            _boxes = new float[n * 6];
        }
        for (int k = 0; k < n; k++)
            _handles[k] = BitConverter.ToUInt32(_blocks.Block(_fading[k]).Slice(HandleOffset, sizeof(uint)));
        Span<float> boxes = _boxes.AsSpan(0, n * 6);
        boxes.Clear();
        _bounds.SurroundingBounds(_handles.AsSpan(0, n), boxes);
        Vector eye = _view.Read().Origin;

        for (int k = 0; k < n; k++)
        {
            Span<byte> block = _blocks.Block(_fading[k]);
            ReadOnlySpan<float> box = boxes.Slice(k * 6, 6);
            int blend = StandaloneFade.Blend((FadeStyle)ReadInt(block, FadeStyleOffset),
                                             ReadFloat(block, FadeStartOffset), ReadFloat(block, FadeEndOffset),
                                             ReadFloat(block, FadeMinAlphaOffset), block[RenderAlphaOffset],
                                             new Vector(box[0], box[1], box[2]), new Vector(box[3], box[4], box[5]), eye);
            WriteInt(block, FxBlendOffset, blend);
        }
    }

    private static int ReadInt(ReadOnlySpan<byte> block, int offset)
        => BitConverter.ToInt32(block.Slice(offset, sizeof(int)));

    private static float ReadFloat(ReadOnlySpan<byte> block, int offset)
        => BitConverter.ToSingle(block.Slice(offset, sizeof(float)));

    private static void WriteInt(Span<byte> block, int offset, int value)
        => BitConverter.TryWriteBytes(block.Slice(offset, sizeof(int)), value);
}
