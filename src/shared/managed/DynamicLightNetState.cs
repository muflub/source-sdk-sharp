using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a <c>light_dynamic</c> tells the client about itself.
///
/// <para>
/// A dynamic light is applied by the renderer at draw time rather than baked
/// into the lightmap, which is what makes it the one kind of light that can
/// move, be switched, or follow something. The engine keeps a small pool of
/// them; the client's half of this entity asks for one every frame and fills it
/// from these values.
/// </para>
///
/// <para>
/// The position and the direction are in here rather than read from the
/// entity's own origin and angles, and that is deliberate: the light's aim is
/// re-derived on the SERVER every tenth of a second from whatever it is
/// pointed at, so the direction is a networked decision rather than a
/// consequence of where the entity happens to be facing.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.LightDynamic)]
public partial class DynamicLightNetState
{
    /// <summary>Where the light is.</summary>
    public partial Vector Origin { get; set; }

    /// <summary>Which way a cone points. Ignored when the light is not a cone.</summary>
    public partial Vector Direction { get; set; }

    /// <summary>How far it reaches.</summary>
    public partial float Radius { get; set; }

    /// <summary>
    /// Brightness as the engine's shared colour exponent, which is what lets a
    /// dlight be brighter than white. Stock clamps a mapper's number to
    /// ±127 and warns; so does the entity.
    /// </summary>
    public partial int Exponent { get; set; }

    /// <summary>
    /// The cone, in degrees. A zero outer angle means the light is a point
    /// rather than a spot, and that distinction decides whether the client
    /// bothers casting the second light at the far end.
    /// </summary>
    [NetField(Bits = 9)] public partial float InnerAngle { get; set; }
    [NetField(Bits = 9)] public partial float OuterAngle { get; set; }

    /// <summary>How big the pool of light where a cone lands is.</summary>
    public partial float SpotRadius { get; set; }

    /// <summary>
    /// What colour the light is.
    ///
    /// <para>
    /// ONE FIELD, AND IT WAS THREE. This was <c>Red</c>, <c>Green</c> and
    /// <c>Blue</c>, each an <c>[NetField(Bits = 8)] byte</c>. The wire cost is
    /// unchanged and so is the size of this class — see the note below on where
    /// the word went — and what changed is that the three are one thing, which
    /// they always were: the only writer parses them out of a single packed
    /// number and the only reader hands all three to the same call.
    /// </para>
    ///
    /// <para>
    /// THE ALPHA IS THE TYPE'S, NOT THE LIGHT'S, and it is held at opaque. A
    /// dynamic light has no opacity — how bright it is, is
    /// <see cref="Exponent"/>. The <c>_light</c> key a mapper writes does carry
    /// a fourth number and vrad does use it as a brightness, and this entity has
    /// always dropped it for that reason; putting it in the alpha would network
    /// a value the client must not read and make the inspector show a
    /// transparency that does not exist. 255 says what is true: all of this
    /// colour, all of the time.
    /// </para>
    ///
    /// <para>
    /// WHERE THE WORD WENT. The three channels used to share a word with
    /// <see cref="Style"/>, which took it to 30 of 32 bits — so
    /// <see cref="Flags"/>, needing four more, could not fit and was padded into
    /// a word of its own with 28 bits spare. Merging the channels frees that:
    /// Style and Flags now share one word and the colour takes another, which is
    /// two packed words before and one after, and one more whole word. 52 bytes
    /// either way, and the same 64-byte tier.
    /// </para>
    /// </summary>
    public partial Color32 Colour { get; set; }

    /// <summary>Which of the engine's 64 lightstyle slots animates it.</summary>
    [NetField(Bits = 6)] public partial byte Style { get; set; }

    /// <summary>
    /// The engine's DLIGHT_* bits: whether this lights the world, models, or
    /// both.
    ///
    /// <para>
    /// This is also how the light is switched OFF, which is stock and is worth
    /// keeping rather than adding a flag: turning it off sets both "no world"
    /// and "no model", so the light still exists and illuminates nothing. The
    /// mapper's original bits are kept on the server so TurnOn can restore
    /// them.
    /// </para>
    ///
    /// <para>
    /// THE ENUM ITSELF, since plan_nettable_generic.md Step 11. It is backed by
    /// a byte and narrowed to four bits, so the wire is exactly what it was when
    /// this was a <c>byte</c> and the domain is now named where the value lives
    /// rather than cast at each of the six places that read it. The width is
    /// still declared: the packer does not read the member list, because an enum
    /// variable may legally hold a value no member names — see WidthOf in
    /// NetStateGenerator.
    /// </para>
    /// </summary>
    [NetField(Bits = 4)] public partial DynamicLightFlags Flags { get; set; }
}

/// <summary>
/// What a <c>spotlight_end</c> tells the client: how big a pool of light to
/// cast where a spotlight lands, and what colour.
///
/// <para>
/// Much smaller than a <see cref="DynamicLightNetState"/> because it is a much
/// smaller thing — the bright patch on the floor under a spotlight, and nothing
/// else. It has no cone, no style and no flags; it is a plain round light whose
/// only variables are how big and how bright.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SpotlightEnd)]
public partial class SpotlightEndNetState
{
    /// <summary>
    /// How big the pool is. Stock multiplies this by three to get the light's
    /// radius, and by 1.8 from the beam's width to get this — both magic
    /// numbers are the sprite's, and both are kept.
    /// </summary>
    public partial float LightScale { get; set; }

    /// <summary>
    /// The colour of the pool, and how much of it to use.
    ///
    /// <para>
    /// ONE FIELD, AND IT WAS FOUR. This was <c>Red</c>, <c>Green</c>,
    /// <c>Blue</c> and <c>Alpha</c>, each an <c>[NetField(Bits = 8)] byte</c>,
    /// and the packer put all four in one word — so the wire cost has not
    /// changed and neither has the size of this class. What changed is that the
    /// four are now one thing: one name, one range check, one row in the schema
    /// pane, and no way to set three channels and leave the fourth holding what
    /// the last spotlight put there.
    /// </para>
    ///
    /// <para>
    /// The alpha is Stock's: it stores this in the render alpha and multiplies
    /// the three components by it, which is how a spotlight fades out as it
    /// stretches past its useful length. It is a channel of this colour rather
    /// than a field beside it for the same reason the other three are — nothing
    /// reads it that is not also reading them.
    /// </para>
    ///
    /// <para>
    /// THE FIELDS MOVED, though the class did not grow. Four narrow channels
    /// were hoisted into the packer's first pass and placed at offset 0, ahead
    /// of the float declared before them; a Color32 takes its word whole and
    /// stays where it is declared. So the layout is 8 bytes either way and the
    /// two fields have swapped places, which moves the schema hash — correctly,
    /// and that is what the hash is for.
    /// </para>
    /// </summary>
    public partial Color32 Tint { get; set; }
}
