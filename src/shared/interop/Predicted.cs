namespace SourceSharp.Abi;

/// <summary>
/// How a misprediction of this networked field is judged (Phase 9c3,
/// plan_baseentity.md).
///
/// <para>
/// Prediction's error check is a byte compare of the declared blob, and for a
/// float that is too strict: a position that differs in the last bit is not an
/// error, it is two runs of the same arithmetic. This attribute carries the
/// tolerance, per field, on the SAME declaration the send table is built from —
/// one list, not a second one, which is the rule the whole nettable design
/// lives by.
/// </para>
///
/// <para>
/// THE DEFAULT IS ZERO, WHICH MEANS EXACT, because that is stock's default:
/// <c>predictioncopy.cpp:736</c> engages a tolerance only when it is positive
/// (<c>bool usetolerance = tolerance &gt; 0.0f</c>), and a field declared with
/// plain <c>DEFINE_PRED_FIELD</c> has none. The fields stock does tolerate are
/// the precedent for choosing a number here: punch angle 0.125, fall velocity
/// and max speed 0.5, base velocity 0.05 (<c>c_baseplayer.cpp:336-380</c>).
/// </para>
///
/// <para>
/// Meaningful on float, Vector and QAngle fields — the compare applies it per
/// component, exactly as stock's <c>CompareVector</c> does. On any other kind
/// it is carried but ignored: an int either matches or it does not.
/// </para>
///
/// <para>
/// This attribute is also the declaration side of "is this predictable", whose
/// default is NO (the plan's sizing rule: the ring is 91 copies of the blob,
/// affordable only because the predictable set is the local player, its weapon
/// and its viewmodel). A field without it still gets NAMED by the misprediction
/// meter — naming needs no opt-in — but only declared tolerances change what
/// counts as an error.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PredictedAttribute : Attribute
{
    public PredictedAttribute(float tolerance = 0f)
    {
        Tolerance = tolerance;
    }

    /// <summary>
    /// The largest per-component difference that is not an error. Zero means
    /// exact, which is stock's default.
    /// </summary>
    public float Tolerance { get; }
}
