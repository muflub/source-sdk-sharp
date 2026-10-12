using System.Runtime.InteropServices;

namespace SourceSharp.Abi;

/// <summary>
/// A pooled temp entity's transform — the state C# OWNS while it simulates one
/// (behaviour ledger row C4).
///
/// <para>
/// THIS IS THE DECISION THE ROW TURNED ON. <c>C_LocalTempEntity::Frame</c>
/// integrates a place, a velocity and a spin every tick, and none of that had
/// ever crossed this boundary: <see cref="TempEntLife"/> carries
/// <c>Die</c>, <c>Flags</c>, <c>FadeSpeed</c>, <c>RenderAmt</c>,
/// <c>Priority</c>, <c>ClientIndex</c>, <c>RenderMode</c> and <c>Alpha</c>, and
/// nothing else. So porting <c>Frame</c> meant choosing where the transform
/// lives, and it lives here.
/// </para>
///
/// <para>
/// WHAT THAT RULES OUT, deliberately: a per-field accessor. <c>Frame</c> reads
/// and writes the origin, the angles and three vectors several times each, so
/// <c>GetLocalOrigin</c>/<c>SetLocalOrigin</c> as slots would be a dozen
/// crossings per tempent per frame, for hundreds of tempents. This is ONE
/// struct, read ONCE when the managed pool takes a tempent over and written
/// ONCE per frame after it has integrated. Native holds no opinion about where
/// a C#-simulated tempent is; it renders what it is told.
/// </para>
///
/// <para>
/// WHAT IS DELIBERATELY ABSENT, and this was measured rather than assumed.
/// <c>m_vecPrevLocalOrigin</c>, <c>m_vLastCollisionOrigin</c> and
/// <c>m_iLastCollisionFrame</c> look like shared state and are not: grepping
/// the client for all three finds readers and writers ONLY inside <c>Frame</c>.
/// Once C# owns <c>Frame</c> they are entirely its own working state, so
/// carrying them here would be moving a function's locals across an ABI twice a
/// frame for nothing. <c>m_vecPrevLocalOrigin</c> is also private with no
/// accessor, so crossing it would have meant editing a stock header to export
/// something nothing outside that one function reads.
/// </para>
///
/// <para>
/// Sequential and blittable, mirroring
/// <c>SourceSharpTempEntTransform_t</c> field for field —
/// <c>AbiParityTests</c> is what holds the two in step.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct TempEntTransform
{
    /// <summary>Where it is. Stock's <c>GetLocalOrigin</c>/<c>SetLocalOrigin</c>.</summary>
    public Vector Origin;

    /// <summary>Which way it faces, as pitch/yaw/roll.</summary>
    public QAngle Angles;

    /// <summary>Units per second.</summary>
    public Vector Velocity;

    /// <summary>Units per second squared, before gravity and wind.</summary>
    public Vector Acceleration;

    /// <summary>Degrees per second.</summary>
    public QAngle AngVelocity;

    /// <summary>
    /// The animating sprite's scale, which <c>Frame</c> advances alongside the
    /// place — a smoke puff grows as it rises.
    /// </summary>
    public float SpriteScale;

    /// <summary>
    /// The animating sprite's current frame. Running off the last one is what
    /// makes <c>Frame</c> answer "this asked to die".
    /// </summary>
    public float Frame;

    /// <summary>
    /// <c>FTENT_SINEWAVE</c>'s horizontal accumulator — stock's <c>x</c>.
    ///
    /// <para>
    /// IN THE TRANSFORM AND NOT ONLY IN THE SPAWN RECORD, because <c>Frame</c>
    /// ADVANCES it every tick. The spawn record is written once; this moves with
    /// the place. A sine-wave tempent whose accumulators did not cross back
    /// would restart its wobble from the spawn value every frame.
    /// </para>
    /// </summary>
    public float SineX;

    /// <summary>The vertical one — stock's <c>y</c>.</summary>
    public float SineY;
}
