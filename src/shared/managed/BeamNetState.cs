using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a beam tells the client about itself.
///
/// <para>
/// Everything here is needed to DRAW one and nothing else is: two endpoints, how
/// wide at each, how much it wanders, what colour and how bright, how fast the
/// texture scrolls, and which sprite. The server decides all of it; the client
/// hands it to the engine's own beam renderer.
/// </para>
///
/// <para>
/// The sprite travels as a MODEL INDEX rather than a name. The server precaches
/// the material and the index is what both sides already agree on — sending the
/// string per beam would put a name on the wire that the client would only look
/// up again.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsBeam)]
public partial class BeamNetState
{
    /// <summary>Where it starts. Not the entity's origin: a beam is drawn between two points, and neither has to be where it lives.</summary>
    public partial Vector Start { get; set; }

    public partial Vector End { get; set; }

    public partial float Width { get; set; }

    /// <summary>Width at the far end. Kept separate because a taper is a look a mapper asks for.</summary>
    public partial float EndWidth { get; set; }

    /// <summary>How far the line wanders off straight. Zero is a laser; large is lightning.</summary>
    public partial float Noise { get; set; }

    /// <summary>How fast the texture runs along the beam.</summary>
    public partial float ScrollRate { get; set; }

    /// <summary>The precached sprite, as the index both sides already share.</summary>
    public partial int SpriteIndex { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }
    [NetField(Bits = 8)] public partial byte Brightness { get; set; }

    /// <summary>
    /// Whether to draw at all.
    ///
    /// A flag rather than removing the entity, because a laser is switched far
    /// more often than it is built — and on this hierarchy the alternative would
    /// mean the client tearing down and rebuilding its half each time.
    /// </summary>
    public partial bool Visible { get; set; }

    /// <summary>
    /// CBeam's flags that reach the draw (<c>FBEAM_*</c>; stock's
    /// <c>CViewRenderBeams::DrawBeam( C_Beam * )</c> keeps SINENOISE, SOLID,
    /// SHADEIN, SHADEOUT and NOTILE of them). See <see cref="BeamNetFlags"/>.
    /// </summary>
    public partial int Flags { get; set; }

    /// <summary>How far along the beam a shaded beam fades over (CBeam's m_fFadeLength).</summary>
    public partial float FadeLength { get; set; }

    /// <summary>The halo sprite drawn at the start, as a model index; 0 or less is none.</summary>
    public partial int HaloIndex { get; set; }

    public partial float HaloScale { get; set; }

    /// <summary>CBeam's m_flHDRColorScale. 1 is stock's default; SsBeam starts there.</summary>
    public partial float HdrColorScale { get; set; }
}

/// <summary>The <c>FBEAM_*</c> values a server sets in <see cref="BeamNetState.Flags"/> (beam_flags.h).</summary>
public static class BeamNetFlags
{
    public const int SineNoise = 0x00000010;
    public const int Solid = 0x00000020;
    public const int ShadeIn = 0x00000040;
    public const int ShadeOut = 0x00000080;
    public const int NoTile = 0x00000200;
}
