//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// point_worldtext: a message written in the world.
///
/// <para>
/// The server half is a string, a size, two spacings, a colour, an orientation,
/// a font and a rainbow flag — eight values a mapper typed, and not one line
/// that draws. The drawing is <c>c_point_worldtext.cpp</c>, nine hundred lines
/// of per-glyph quads on a font page. Nothing in this class is on the wrong
/// side of that line, which is why it moved whole where its neighbours in
/// report.md's Moderate tier could not: <c>env_glow</c> is a renderer with a
/// keyvalue attached, and <c>info_particle_system</c> networks sixty-three
/// entity handles the managed wire has no type for.
/// </para>
///
/// <para>
/// It still needed a client half written before the classname could move, for
/// the reason the whole presentation group turns on. Stock's text draws because
/// the server networks a <c>CPointWorldText</c> and the engine builds the
/// <c>C_PointWorldText</c> paired with <c>DT_PointWorldText</c>. A managed
/// entity is never on the wire as that class — every one of them arrives
/// through one of the six tier tables in <c>generic_nettable.h</c> — so moving
/// the name alone would have delivered the entity perfectly and drawn nothing.
/// See <see cref="Client.Standalone.ClientWorldText"/> and
/// <c>shell_worldtext_render.cpp</c>.
/// </para>
///
/// <para>
/// An <see cref="ScriptedEntityKind.Effect"/>, which is the kind for something
/// the client draws and nothing touches. Stock says the same thing the long way
/// round: <c>SetSolid( SOLID_NONE )</c> in <c>Spawn</c>, and an
/// <c>UpdateTransmitState</c> override whose only job is "send even though we
/// don't have a model".
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointWorldtext, ScriptedEntityKind.Effect)]
public sealed class PointWorldText : ScriptedEntity
{
    /// <summary>What a map may set on this entity. See <see cref="PointWorldTextVars"/>.</summary>
    [ScriptVars] public PointWorldTextVars Vars = new();

    /// <summary>
    /// The same declaration the client half reads. Written here, decoded there;
    /// there is no second copy of the layout.
    /// </summary>
    [NetState] public WorldTextNetState State = new();

    public override void OnSpawn()
    {
        State.Text        = Vars.Message;
        State.TextSize    = Vars.TextSize;
        State.SpacingX    = Vars.TextSpacingX;
        State.SpacingY    = Vars.TextSpacingY;
        State.Color       = Pack(Vars.Color);
        State.Orientation = (uint)Vars.Orientation;
        State.Font        = (uint)Vars.Font;
        State.Rainbow     = Vars.Rainbow;
    }

    //-------------------------------------------------------------------------
    // Stock's eight inputs, one per networked field. There is no behaviour in
    // any of them — each is an assignment, and stock's are too.
    //-------------------------------------------------------------------------

    [ScriptedEvent("SetText")]        private void OnSetText(string v)     => State.Text = v;
    [ScriptedEvent("SetTextSize")]    private void OnSetTextSize(float v)  => State.TextSize = v;
    [ScriptedEvent("SetTextSpacingX")] private void OnSetSpacingX(float v) => State.SpacingX = v;
    [ScriptedEvent("SetTextSpacingY")] private void OnSetSpacingY(float v) => State.SpacingY = v;
    [ScriptedEvent("SetOrientation")] private void OnSetOrientation(float v) => State.Orientation = (uint)(int)v;
    [ScriptedEvent("SetFont")]        private void OnSetFont(float v)      => State.Font = (uint)(int)v;
    [ScriptedEvent("SetRainbow")]     private void OnSetRainbow(float v)   => State.Rainbow = v != 0f;

    /// <summary>
    /// Stock's <c>SetColor</c>, which takes a <c>FIELD_COLOR32</c>.
    ///
    /// <para>
    /// It arrives here as the text a map wrote, which is what a colour variant
    /// has always been on the way in — stock's own <c>KeyValue</c> reads the
    /// same three or four numbers out of a string. A value that does not parse
    /// leaves the colour alone rather than blanking it; stock's <c>sscanf</c>
    /// would have filled the missing channels with 255, and silently turning a
    /// mistyped input white is worse than ignoring it. See docs/divergences.md.
    /// </para>
    /// </summary>
    [ScriptedEvent("SetColor")]
    private void OnSetColor(string v)
    {
        if (ScriptedVarParse.Color(v, out Color32 parsed))
            State.Color = Pack(parsed);
    }

    /// <summary>
    /// A colour as one number, red in the LOW byte.
    ///
    /// <para>
    /// That is <c>color32</c>'s own layout in memory, which is what stock's
    /// single 32-bit send prop carries and what the ABI hands back to native.
    /// Packing here rather than at the boundary means the same number is on the
    /// wire, in the blob and in the call, so there is one place a channel order
    /// could be wrong instead of three.
    /// </para>
    /// </summary>
    internal static uint Pack(Color32 color)
        => (uint)(Byte(color.R)
                | (Byte(color.G) <<  8)
                | (Byte(color.B) << 16)
                | (Byte(color.A) << 24));

    private static int Byte(int value) => Math.Clamp(value, 0, 255);
}

/// <summary>
/// The map keys <see cref="PointWorldText"/> declares.
/// </summary>
[ScriptVars]
public partial class PointWorldTextVars
{
    /// <summary>The text itself. Stock reads it in <c>KeyValue</c> rather than through a keyfield.</summary>
    public string Message { get; set; } = "";

    /// <summary>Height of a line in world units. Stock's constructor default, before any key is read.</summary>
    [VarName("textsize")]
    public float TextSize { get; set; } = 10.0f;

    [VarName("textspacingX")]
    public float TextSpacingX { get; set; }

    [VarName("textspacingY")]
    public float TextSpacingY { get; set; }

    /// <summary>0 draws along the entity's angles, 1 faces the viewer, 2 faces the viewer upright.</summary>
    public int Orientation { get; set; }

    /// <summary>Index into the client's font table. The client clamps it.</summary>
    public int Font { get; set; }

    /// <summary>Cycles each glyph's hue instead of using the colour.</summary>
    public bool Rainbow { get; set; }

    /// <summary>Stock's <c>KeyValue</c> case, and it defaults to opaque white the same way.</summary>
    public Color32 Color { get; set; } = new(255, 255, 255, 255);
}
