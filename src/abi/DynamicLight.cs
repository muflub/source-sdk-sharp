using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// One dynamic light. Mirrors SourceSharpDynamicLight.
///
/// <para>
/// A dlight is applied by the renderer at draw time rather than baked into the
/// lightmap, and the engine keeps a small fixed pool of them. Everything ABOUT
/// one is decided in managed code; this is the shape the answer travels in.
/// </para>
///
/// <para>
/// The whole light per call rather than a handle and fifteen setters: it is
/// refreshed every frame by whatever owns it, so a partial update has no
/// meaning and fifteen crossings per light per frame would.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DynamicLight
{
    /// <summary>
    /// Which slot to reuse. Asking again with the same key returns the same
    /// light rather than a new one, which is how a light moves instead of
    /// leaving a trail of stale ones. A negative key is conventionally the same
    /// entity's second light — a spotlight has one at each end.
    /// </summary>
    public int Key;

    /// <summary>
    /// Lights MODELS only and never the world. Much cheaper, and what a light
    /// flagged "no world illumination" actually wants.
    /// </summary>
    public int IsElight;

    public float OriginX, OriginY, OriginZ;
    public float DirectionX, DirectionY, DirectionZ;

    public float Radius;

    /// <summary>
    /// Colour with the engine's shared exponent, not a byte triple. The
    /// exponent is what lets a dlight be brighter than white.
    /// </summary>
    public int Red, Green, Blue, Exponent;

    /// <summary>Which lightstyle animates it, and the engine's DLIGHT_* bits.</summary>
    public int Style;
    public int Flags;

    /// <summary>A cone, in degrees. A zero outer angle is not a cone at all.</summary>
    public float InnerAngle;
    public float OuterAngle;

    /// <summary>
    /// How long from now it lasts. A light refreshed every frame asks for a
    /// little more than a frame; one placed and left asks for a great deal.
    /// </summary>
    public float LiveFor;

    /// <summary>
    /// How much radius the light sheds each second — <c>dlight_t::decay</c>,
    /// "drop this each second".
    ///
    /// <para>
    /// ZERO FOR EVERY LIGHT THAT IS REFRESHED. A light rewritten sixty times a
    /// second cannot decay, so <c>ClientDynamicLight</c> and
    /// <c>ClientSpotlightEnd</c> leave this at its default and behave exactly
    /// as they did before the field existed. It is here for
    /// <c>DT_TEDynamicLight</c>, the first light on this ABI that is PLACED AND
    /// LEFT: stock asks the pool once and sets <c>die</c> and <c>decay</c>
    /// together off the wire, so without this the temp entity light would hold
    /// full brightness for its whole life and then vanish.
    /// </para>
    /// </summary>
    public float Decay;
}
