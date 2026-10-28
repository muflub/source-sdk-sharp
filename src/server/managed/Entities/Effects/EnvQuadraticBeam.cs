using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// env_quadraticbeam: a beam that bends.
///
/// <para>
/// The one entity in the sprite-and-smoke batch whose entire client half is
/// five lines — bind the sprite, read the colour, call the engine's own
/// <c>DrawBeamQuadratic</c>. That is why it moved and the particle systems
/// beside it did not: there is nothing to reimplement, only something to
/// decide.
/// </para>
///
/// <para>
/// It is worth being honest about what a MAP gets from it, because the answer
/// is the same as stock's: nothing. <c>CEnvQuadraticBeam</c> declares no
/// keyvalues and no inputs — every field is set by <c>CreateQuadraticBeam</c>,
/// which nothing in this build calls. A mapper placing one has always got an
/// invisible entity, and still does.
/// </para>
///
/// <para>
/// What is new is that managed code can now build one, the way
/// <c>env_laser</c> builds a <c>beam</c>: create the entity, find its
/// managed half, and set the spline. A deliberately un-wrapped factory — there
/// is no caller for one yet, and a helper written for nobody is a helper
/// written against a guess about who.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.EnvQuadraticbeam, ScriptedEntityKind.Effect)]
public sealed class EnvQuadraticBeam : ScriptedEntity
{
    /// <summary>
    /// The same declaration the client half reads. Written here, decoded there;
    /// there is no second copy of the layout.
    /// </summary>
    [NetState] public QuadraticBeamNetState State = new();

    public override void OnSpawn()
    {
        // Stock's, and both halves matter. Additive is what makes a beam look
        // like light rather than like a painted tube, and white is the neutral
        // the sprite's own colours are multiplied by.
        State.Red = State.Green = State.Blue = 255;
        State.Visible = true;
    }

    /// <summary>
    /// The three points the curve is built from.
    ///
    /// <para>
    /// The entity's own origin is set to the START as well, and that is not
    /// redundant: the shell transmits by PVS from the origin, so a beam left at
    /// the world origin would be culled for every player in the map. The same
    /// arrangement <c>beam</c> uses, and it records the same trap.
    /// </para>
    /// </summary>
    public void SetSpline(Vector start, Vector control, Vector target)
    {
        State.Start = start;
        State.Control = control;
        State.Target = target;
        Origin = start;
    }

    public void SetWidth(float width) => State.Width = width;

    public void SetScrollRate(float rate) => State.ScrollRate = rate;

    /// <summary>The sprite it is drawn with, as a precached model index.</summary>
    public void SetSprite(int spriteIndex) => State.SpriteIndex = spriteIndex;

    public void SetColour(byte r, byte g, byte b)
    {
        State.Red = r;
        State.Green = g;
        State.Blue = b;
    }

    public bool Visible
    {
        get => State.Visible;
        set => State.Visible = value;
    }

}
