namespace SourceSharp.Abi;

/// <summary>
/// How a value is blended between two samples, which is not a property of its
/// storage: <see cref="EngineBlockHead.Origin"/> and
/// <see cref="EngineBlockHead.Angles"/> are both three floats and only one of
/// them may be interpolated component by component.
/// </summary>
public enum InterpolationKind
{
    /// <summary>
    /// Three independent numbers. Blended componentwise, and with a cubic
    /// through three samples where there are three — Source's
    /// <c>Lerp&lt;Vector&gt;</c> and <c>Lerp_Hermite&lt;Vector&gt;</c>.
    /// </summary>
    Vector = 0,

    /// <summary>
    /// A FACING, and blending it componentwise is wrong: 359° to 1° would sweep
    /// the long way round and a pitch through the pole would tumble. Source
    /// converts both samples to quaternions, slerps, and converts back
    /// (<c>Lerp&lt;QAngle&gt;</c>, <c>mathlib.h:661</c>), and specialises
    /// <c>Lerp_Hermite&lt;QAngle&gt;</c> to that same two-sample slerp with the
    /// comment "can't do hermite with QAngles, get discontinuities".
    /// </summary>
    Angle = 1,
}

/// <summary>
/// DECLARES A BLOCK FIELD AS ONE THE CLIENT INTERPOLATES (plan_entity.md §3.9).
///
/// <para>
/// The server sends snapshots, not frames. An entity drawn at whatever the last
/// snapshot said advances in one jump per update and stands still in between —
/// at a 20Hz update rate a projectile crossing a room lands in 75-unit steps.
/// The average speed is right and the motion is not. So the networked value is
/// HISTORY, and what is drawn is a point in it, read slightly in the past.
/// </para>
///
/// <para>
/// <b>The attribute is the whole declaration.</b> There is no second list of
/// interpolated fields anywhere: <c>SourceSharp.Client.InterpolatedFields</c>
/// reads these off the block by reflection, once, and the ring table is sized
/// from what it finds. Interpolating a new field is this attribute and nothing
/// else — which is the point of putting it on the field rather than writing a
/// table beside it.
/// </para>
///
/// <para>
/// <b>Source and destination are different fields, and that is §3.2.</b> The
/// head carries where the entity is according to the last update; the client
/// tail carries where it is being DRAWN this frame. The attribute names the
/// tail field the drawn answer goes into, by name, so that a rename on either
/// side is a failed lookup with a message rather than an offset that quietly
/// addresses the neighbouring value.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class InterpolatedAttribute : Attribute
{
    public InterpolatedAttribute(InterpolationKind kind, string renderField)
    {
        Kind = kind;
        RenderField = renderField;
    }

    /// <summary>How the two samples either side of the target time are blended.</summary>
    public InterpolationKind Kind { get; }

    /// <summary>
    /// The <see cref="EngineBlockClientTail"/> field the drawn value is written
    /// into. A name and not an offset: see the class remarks.
    /// </summary>
    public string RenderField { get; }
}
