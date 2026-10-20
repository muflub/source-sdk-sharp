namespace SourceSharp.Abi;

/// <summary>
/// Which engine <c>MASK_*</c> a <see cref="TraceMask"/> means on the server
/// (plan_server_native_v2 T4d). The decision was a table in
/// shell_worldquery.cpp; the server's TraceLine, TraceLineSurface and
/// TraceHull slots now carry the engine's own mask and decide nothing. The
/// values are bspflags.h's.
/// </summary>
internal static class TraceMasks
{
    // CONTENTS_* (bspflags.h), the parts the masks below are made of.
    private const int ContentsSolid = 0x1;
    private const int ContentsWindow = 0x2;
    private const int ContentsGrate = 0x8;
    private const int ContentsSlime = 0x10;
    private const int ContentsWater = 0x20;
    private const int ContentsBlockLos = 0x40;
    private const int ContentsOpaque = 0x80;
    private const int ContentsMoveable = 0x4000;
    private const int ContentsMonster = 0x2000000;
    private const int ContentsDebris = 0x4000000;
    private const int ContentsHitbox = 0x40000000;

    /// <summary><c>MASK_SOLID_BRUSHONLY</c>.</summary>
    public const int SolidBrushOnly = ContentsSolid | ContentsMoveable | ContentsWindow | ContentsGrate;

    /// <summary><c>MASK_WATER</c>: water, slime and the moveable bit.</summary>
    public const int Water = ContentsWater | ContentsMoveable | ContentsSlime;

    /// <summary><c>MASK_SHOT</c>: what a bullet hits.</summary>
    public const int Shot = ContentsSolid | ContentsMoveable | ContentsMonster | ContentsWindow | ContentsDebris | ContentsHitbox;

    /// <summary><c>MASK_SHOT_HULL</c>.</summary>
    public const int ShotHull = ContentsSolid | ContentsMoveable | ContentsMonster | ContentsWindow | ContentsDebris | ContentsGrate;

    /// <summary><c>MASK_SOLID</c>.</summary>
    public const int Solid = ContentsSolid | ContentsMoveable | ContentsWindow | ContentsMonster | ContentsGrate;

    /// <summary><c>MASK_BLOCKLOS</c>.</summary>
    public const int BlockLos = ContentsSolid | ContentsMoveable | ContentsBlockLos;

    /// <summary><c>MASK_OPAQUE_AND_NPCS | CONTENTS_WINDOW</c>.</summary>
    public const int OpaqueAndNpcsWindow = ContentsSolid | ContentsMoveable | ContentsOpaque | ContentsMonster | ContentsWindow;

    /// <summary><c>MASK_OPAQUE | CONTENTS_MONSTER | CONTENTS_DEBRIS</c>: a glow's sight line (C7 pixel visibility).</summary>
    public const int GlowSight = ContentsSolid | ContentsMoveable | ContentsOpaque | ContentsMonster | ContentsDebris;

    /// <summary><c>CONTENTS_SOLID</c> alone: a glow overlay's line at the sky (C7 glow overlays).</summary>
    public const int SolidOnly = ContentsSolid;

    /// <summary><c>MASK_ALL</c>.</summary>
    public const int All = unchecked((int)0xFFFFFFFF);

    /// <summary>
    /// A line trace's mask: the nine named masks, and the brushes-only mask for
    /// anything else (the native table's fall-through).
    /// </summary>
    public static int Line(TraceMask mask) => mask switch
    {
        TraceMask.Shot => Shot,
        TraceMask.BrushesAndWater => SolidBrushOnly | Water,
        TraceMask.All => All,
        TraceMask.ShotHull => ShotHull,
        TraceMask.Solid => Solid,
        TraceMask.BlockLos => BlockLos,
        TraceMask.OpaqueAndNpcsWindow => OpaqueAndNpcsWindow,
        TraceMask.GlowSight => GlowSight,
        TraceMask.ContentsSolid => SolidOnly,
        _ => SolidBrushOnly,
    };

    /// <summary>
    /// A hull trace's mask: a shot, or solid. Anything that would fit or not --
    /// env_entity_maker's question -- is asked against MASK_SOLID.
    /// </summary>
    public static int Hull(TraceMask mask) => mask == TraceMask.Shot ? Shot : Solid;
}
