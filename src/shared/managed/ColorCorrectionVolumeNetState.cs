using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What <c>color_correction_volume</c> tells the client — and, unlike its
/// point sibling, the weight is deliberately NOT here.
///
/// <para>
/// Stock networks one <c>m_Weight</c> the server computes by watching "the
/// local player" cross the brush, and <c>UTIL_GetLocalPlayer()</c> answers NULL
/// the moment <c>maxClients &gt; 1</c> — so in this mod the native entity has
/// only ever sent a constant zero. One weight was never the right shape anyway:
/// whether the correction applies depends on whether the VIEWER is standing in
/// the volume, which is a per-viewer fact with no single server-side value even
/// in principle. So the wire carries the volume itself — where it is, what it
/// loads, how fast it fades — and each client runs the fade against its own
/// player. See <c>SourceSharp.Client.Standalone.ClientColorCorrectionVolume</c>
/// for the half that owns the weight now.
/// </para>
///
/// <para>
/// The bounds travel as WORLD-SPACE corners rather than as offsets from an
/// origin the client would have to ask for. A standalone entity's origin is a
/// wire fact, not a slot call — <c>EntityGetOrigin</c> is server-only and
/// answers a silent zero on the client (Part 4b's first live violation was
/// exactly that) — and a brush volume never moves, so the two corners are
/// written once at spawn and are the whole geometry. An axis-aligned box
/// rather than the brush's exact planes; see docs/divergences.md.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.ColorCorrectionVolume)]
public partial class ColorCorrectionVolumeNetState
{
    /// <summary>The lookup table to load, relative to the mod directory. The
    /// same 128 as <see cref="ColorCorrectionNetState.LookupFile"/>, for the
    /// same measured reason.</summary>
    [NetString(128)] public partial string LookupFile { get; set; }

    /// <summary>Lower corner of the volume, world space.</summary>
    public partial Vector BoundsMin { get; set; }

    /// <summary>Upper corner of the volume, world space.</summary>
    public partial Vector BoundsMax { get; set; }

    /// <summary>The weight the fade tops out at inside the volume. The map's
    /// "maxweight". Stock parses it and then fades to a flat 1.0 regardless;
    /// this build honours it, the same correction its point sibling already
    /// made — see docs/divergences.md.</summary>
    public partial float MaxWeight { get; set; }

    /// <summary>Seconds for the full fade in either direction. The map's
    /// "fadeDuration". Zero or negative means instant.</summary>
    public partial float FadeDuration { get; set; }

    /// <summary>Whether the volume acts at all. Off drops the weight to zero
    /// immediately, which is stock's behaviour — only crossings fade.</summary>
    public partial bool Enabled { get; set; }
}
