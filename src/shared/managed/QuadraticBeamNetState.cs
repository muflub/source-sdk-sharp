using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a curved beam tells the client about itself.
///
/// <para>
/// Nearly <see cref="BeamNetState"/>, and the differences are the argument for
/// it being its own table rather than fields bolted onto that one. A quadratic
/// beam has a CONTROL point and no noise, no end width, no taper and no
/// per-frame life — the renderer draws it immediately rather than registering
/// it — so half of a shared table would be dead weight on every straight beam
/// in the map, and the other half meaningless on every curved one.
/// </para>
///
/// <para>
/// The start IS carried here as well as being the entity's origin, which looks
/// redundant and is not. <see cref="BeamNetState"/> does the same and for the
/// same reason: the client shell has no origin accessor on the managed side, so
/// a curve that read its start from the entity would have nothing to read. The
/// origin is set to the same point on the server because that is what the PVS
/// check uses — a beam left at the world origin is culled for everybody.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.EnvQuadraticbeam)]
public partial class QuadraticBeamNetState
{
    /// <summary>Where it begins. See the note on the class for why it is here as well as being the origin.</summary>
    public partial Vector Start { get; set; }

    /// <summary>
    /// Where the curve bends TOWARD. Not a point on the curve — a quadratic
    /// Bezier leaves the start aiming at this and arrives at the end coming from
    /// it, and passes through neither.
    /// </summary>
    public partial Vector Control { get; set; }

    /// <summary>Where it ends.</summary>
    public partial Vector Target { get; set; }

    public partial float Width { get; set; }

    /// <summary>
    /// How fast the texture runs along the curve, in texture units per second.
    ///
    /// A RATE here and an offset by the time it reaches the renderer: the
    /// client multiplies by its own clock, so the scroll stays smooth between
    /// server updates instead of stepping once per snapshot.
    /// </summary>
    public partial float ScrollRate { get; set; }

    /// <summary>The precached sprite, as the index both sides already share.</summary>
    public partial int SpriteIndex { get; set; }

    [NetField(Bits = 8)] public partial byte Red { get; set; }
    [NetField(Bits = 8)] public partial byte Green { get; set; }
    [NetField(Bits = 8)] public partial byte Blue { get; set; }

    /// <summary>
    /// Whether to draw at all.
    ///
    /// A flag rather than removing the entity, for the reason
    /// <see cref="BeamNetState"/> gives: switching is far more common than
    /// building, and on this hierarchy the alternative means the client tearing
    /// its half down and rebuilding it each time.
    /// </summary>
    public partial bool Visible { get; set; }
}
