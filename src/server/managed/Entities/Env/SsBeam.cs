using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// ss_beam: a line drawn between two points, and the first entity here whose
/// entire client-side presence is a drawing.
///
/// <para>
/// It replaces a native CBeam. What made that necessary was not the renderer —
/// the client's own beam renderer is reachable — but the lack of a shell kind
/// that could describe a thing with no model and no collision that still has to
/// reach the client. <see cref="ScriptedEntityKind.Effect"/> is that kind, and
/// this is what it was added for.
/// </para>
///
/// <para>
/// Nothing about a beam is decided here. It is told where its ends are and what
/// it looks like, and it networks that; the deciding is done by whatever owns
/// one — <c>env_laser</c> being the first.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.SsBeam, ScriptedEntityKind.Effect)]
public sealed class SsBeam : ScriptedEntity
{
    /// <summary>
    /// The same declaration the client half reads. Written here, decoded there;
    /// there is no second copy of the layout.
    /// </summary>
    [NetState] public BeamNetState State = new();

    /// <summary>
    /// Points it at two places in the world.
    ///
    /// <para>
    /// The entity's own origin is set to the START as well, and that is not
    /// redundant: the shell transmits by PVS from the origin, so a beam left at
    /// the world origin would be culled for every player in the map. It is also
    /// the reason a beam spanning a long distance is a known limitation — see
    /// docs/divergences.md.
    /// </para>
    /// </summary>
    public void SetPoints(Vector start, Vector end)
    {
        State.Start = start;
        State.End   = end;
        Origin      = start;
    }

    /// <summary>
    /// How it is drawn. Applied once by whoever built it; none of it changes
    /// per frame, which is why it is set apart from <see cref="SetPoints"/>.
    /// </summary>
    public void SetLook(int spriteIndex, float width, float endWidth, float noise,
                        float scrollRate, byte r, byte g, byte b, byte brightness)
    {
        State.SpriteIndex = spriteIndex;
        State.Width       = width;
        State.EndWidth    = endWidth;
        State.Noise       = noise;
        State.ScrollRate  = scrollRate;
        State.Red         = r;
        State.Green       = g;
        State.Blue        = b;
        State.Brightness  = brightness;
    }

    /// <summary>
    /// Whether to draw. A flag rather than removal, because a laser is switched
    /// far more often than it is built.
    /// </summary>
    public bool Visible
    {
        get => State.Visible;
        set => State.Visible = value;
    }
}
