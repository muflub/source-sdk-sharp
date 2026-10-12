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
/// shadow_control: which way the sun is, and what its shadows look like.
///
/// <para>
/// The first of a small family of entities that are a networked STRUCT rather
/// than a behaviour. The client's shadow manager reads four things and every
/// shadow in the map follows; there is no thinking underneath. What owning it
/// in C# buys is the two parts that ARE decisions — what the map's keys mean,
/// and what its inputs do to them.
/// </para>
///
/// <para>
/// The angles-to-direction conversion happens HERE, once, on the way in, rather
/// than on the wire or on the client. A mapper points a compass; the renderer
/// wants a forward vector. Stock's <c>CShadowControl::KeyValue</c> makes the
/// same split for the same reason.
/// </para>
///
/// <para>
/// Always transmitted, and that is not an optimisation being skipped. The
/// entity exists to be read by every client and it sits wherever the mapper
/// dropped it, so a PVS check would answer "not visible" for a player in the
/// next room and leave the shadows pointing wherever the last map left them.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.ShadowControl, ScriptedEntityKind.Effect)]
public sealed class ShadowControl : ScriptedEntity
{
    public ShadowControl(IEngineEntityRender render)
    {
        Render = render;
    }

    /// <summary>what this entity looks like.</summary>
    private readonly IEngineEntityRender Render;
    [NetState] public ShadowControlNetState State = new();

    /// <summary>
    /// Stock's fallback when the map wrote no angles, or wrote all zeroes: 80
    /// down and 30 round, which is an afternoon sun rather than a horizontal
    /// one. A map that genuinely wants a horizontal light writes "0 0 0.01".
    /// </summary>
    private static readonly Vector DefaultAngles = new(80f, 30f, 0f);

    /// <summary>What a map may set on this entity. See <see cref="ShadowControlVars"/>.</summary>
    [ScriptVars] public ShadowControlVars Vars = new();

    public override void OnSpawn()
    {
        // Everything a client needs, from anywhere in the map.
        Render.SetTransmitAlways(Handle, true);

        Vector angles = LengthSquared(Vars.Angles) == 0f ? DefaultAngles : Vars.Angles;
        Vector forward = Angles.Forward(angles);

        // The backward-compatible key, under stock's own condition: used only
        // when the angles did not already give a usable direction. A map that
        // wrote both gets the angles, which is what the newer key means.
        if (Vars.Direction.Length != 0 && MathF.Abs(LengthSquared(forward) - 1f) > 1e-3f)
            forward = ParseVector(Vars.Direction, forward);

        State.Direction = forward;
        Apply();
    }

    private static float LengthSquared(Vector v) => (v.X * v.X) + (v.Y * v.Y) + (v.Z * v.Z);

    /// <summary>
    /// Copies the three fields that are not the direction into the networked
    /// state. Called from spawn and from every input that changes one of them —
    /// the alternative is three assignments repeated at five call sites.
    /// </summary>
    private void Apply()
    {
        var (r, g, b, _) = GameText.ParseColor(Vars.Colour, 64, 64, 64, 0);

        State.Red         = r;
        State.Green       = g;
        State.Blue        = b;
        State.MaxDistance = Vars.Distance;
        State.Disabled    = Vars.Disabled;
    }

    /// <summary>The map's "color" input. Stock spells it as a DEFINE_INPUT on the field.</summary>
    [ScriptedEvent("color")]
    private void OnColour(string value)
    {
        Vars.Colour = value;
        Apply();
    }

    /// <summary>
    /// The "direction" INPUT, which unlike the keyvalue of the same name is not
    /// a fallback for anything: it is a direct set of the vector.
    /// </summary>
    [ScriptedEvent("direction")]
    private void OnDirection(string value) => State.Direction = ParseVector(value, State.Direction);

    [ScriptedEvent("SetDistance")]
    private void OnSetDistance(float value)
    {
        Vars.Distance = value;
        Apply();
    }

    [ScriptedEvent("SetShadowsDisabled")]
    private void OnSetShadowsDisabled(float value)
    {
        Vars.Disabled = value != 0f;
        Apply();
    }

    /// <summary>
    /// Angles rather than a direction, and the only input here that does
    /// arithmetic. A map switching between two lighting setups writes compass
    /// headings, not vectors.
    /// </summary>
    [ScriptedEvent("SetAngles")]
    private void OnSetAngles(string value) => State.Direction = Angles.Forward(ParseVector(value, Vars.Angles));
}

/// <summary>
/// The map keys <see cref="ShadowControl"/> declares.
/// </summary>
[ScriptVars]
public partial class ShadowControlVars
{
    public Vector Angles { get; set; }

    /// <summary>
    /// The pre-<c>angles</c> spelling, which stock still honours. It wins only
    /// when the angles did not already produce a unit-length direction — the
    /// same test stock makes, and the reason both are kept rather than one
    /// being dropped as obsolete.
    /// </summary>
    public string Direction { get; set; } = string.Empty;

    /// <summary>
    /// Four components, so a string rather than a Vector: a map writes a colour
    /// as "64 64 64 0" and a three-component parse rejects it outright.
    /// </summary>
    [VarName("color")]
    public string Colour { get; set; } = "64 64 64 0";

    public float Distance { get; set; } = 50f;

    [VarName("disableallshadows")]
    public bool Disabled { get; set; }
}
