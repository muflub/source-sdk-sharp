using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// Where the water beneath a point is, and what kind (plan_server_native_v2
/// T4f): shell_worldquery.cpp's SourceSharp_WaterSurfaceBelow, over the managed
/// tracer. One trace for three answers, because running it twice would let the
/// two answers disagree about the same water.
/// </summary>
public static class WaterQuery
{
    // bspflags.h
    private const int ContentsSlime = 0x10;

    /// <summary>MASK_WATER: CONTENTS_WATER | CONTENTS_MOVEABLE | CONTENTS_SLIME.</summary>
    public const int MaskWater = 0x20 | 0x4000 | ContentsSlime;

    /// <summary>MASK_SOLID_BRUSHONLY: CONTENTS_SOLID | MOVEABLE | WINDOW | GRATE.</summary>
    public const int MaskSolidBrushOnly = 0x1 | 0x4000 | 0x2 | 0x8;

    /// <summary>How far down the trace looks.</summary>
    public const float Depth = 4096f;

    /// <summary>
    /// <see cref="WaterBelow.Submerged"/> when the point is already in water
    /// (asked first: a downward trace from inside water finds its bottom);
    /// otherwise a trace straight down for water or brush, and
    /// <see cref="WaterBelow.Found"/> only when what stopped it was water --
    /// a MASK_WATER trace stops on the floor as readily as on the surface.
    /// </summary>
    public static WaterBelow Below(IServerTraceRoute tracer, Vector start)
    {
        ArgumentNullException.ThrowIfNull(tracer);

        if ((tracer.PointContents(start) & MaskWater) != 0)
            return new WaterBelow(Submerged: true, Found: false, Slime: false, Surface: start);

        ServerTrace t = tracer.Line(start, start - new Vector(0f, 0f, Depth),
                                    (uint)(MaskWater | MaskSolidBrushOnly), EntityHandle.None, 0);

        if (t.Trace.Fraction >= 1f)
            return new WaterBelow(Submerged: false, Found: false, Slime: false, Surface: start);

        bool found = (t.Trace.Contents & MaskWater) != 0;
        return new WaterBelow(
            Submerged: false,
            Found: found,
            Slime: (t.Trace.Contents & ContentsSlime) != 0,
            Surface: found ? t.Trace.EndPos : start);
    }
}
