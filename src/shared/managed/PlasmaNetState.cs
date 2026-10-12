using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a plasma flame tells the client about itself.
///
/// <para>
/// Longer than <see cref="FireSmokeNetState"/> because the client half really
/// does read all of it. Where <c>C_FireSmoke</c> had been hollowed out to a
/// single particle-system call, <c>C_Plasma</c> is intact: six sprites in a
/// ring whose radius follows the flame's scale, a glow beneath them that rises
/// as it grows, all seven animated frame by frame, and a decal dropped on the
/// floor twenty times a second. Every field here feeds one of those.
/// </para>
///
/// <para>
/// THE SPRITES TRAVEL AS MODEL INDICES, the way <see cref="BeamNetState"/>'s
/// does: the server precached them and the index is what both sides already
/// agree on. Two flame sprites rather than one because stock alternates between
/// them around the ring — they are the same material in this build, which is
/// stock's own <c>//&lt;&lt;TEMP&gt;&gt;</c> comment, and both are sent rather
/// than assumed equal so that a mod parting them needs no wire change.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.SsPlasma)]
public partial class PlasmaNetState
{
    /// <summary>
    /// The size the flame is being drawn at right now, before this frame's ramp
    /// is applied — stock's <c>m_flStartScale</c>.
    ///
    /// It is the size the client's own interpolation starts from when a new
    /// destination arrives, which is why the server sends it at all rather than
    /// letting the client remember: a client that has just entered the flame's
    /// PVS has nothing to remember.
    /// </summary>
    public partial float StartScale { get; set; }

    /// <summary>
    /// The size the flame is heading for — stock's <c>m_flScale</c>.
    /// </summary>
    public partial float Scale { get; set; }

    /// <summary>
    /// How long it takes to get there, in seconds — stock's
    /// <c>m_flScaleTime</c>.
    ///
    /// <para>
    /// A DURATION rather than a deadline, which is stock's choice and is kept
    /// because the two clocks are the same one: the client turns it into a pair
    /// of instants on receipt, exactly as <c>RecvProxy_PlasmaScaleTime</c> did.
    /// </para>
    ///
    /// <para>
    /// Zero is the common value, not an exotic one: <c>env_fire</c> asks for the
    /// flame's starting size immediately. That is the value stock divided by.
    /// See <see cref="PlasmaScaleRamp"/>.
    /// </para>
    /// </summary>
    public partial float ScaleTime { get; set; }

    /// <summary>
    /// Whether the flame is lit — stock's <c>bitsFIRE_ACTIVE</c>.
    ///
    /// <para>
    /// Unlike the natural fire's, this one is REAL: <c>C_Plasma::AddEntity</c>
    /// tests it and returns without drawing or updating anything when it is
    /// clear, so a plasma flame that has been told to switch off actually does.
    /// That asymmetry is stock's, and it is why one of these two classes
    /// networks the bit and the other does not.
    /// </para>
    /// </summary>
    public partial bool Active { get; set; }

    /// <summary>The flame sprite, precached. Stock's <c>m_nPlasmaModelIndex</c>.</summary>
    public partial int PlasmaModelIndex { get; set; }

    /// <summary>
    /// The flame sprite used by every other member of the ring. Stock's
    /// <c>m_nPlasmaModelIndex2</c>.
    /// </summary>
    public partial int PlasmaModelIndex2 { get; set; }

    /// <summary>The glow beneath. Stock's <c>m_nGlowModelIndex</c>.</summary>
    public partial int GlowModelIndex { get; set; }

    /// <summary>
    /// How many frames the flame sprite has, which is what its animation wraps
    /// at.
    ///
    /// <para>
    /// Networked rather than looked up, because it CANNOT be looked up on the
    /// side that needs it: <c>ModelFrameCount</c> is wired inside the
    /// gamesystem's <c>#ifndef CLIENT_DLL</c>, so the client's pointer is null
    /// and the managed wrapper answers a silent -1. The server has the index in
    /// hand the moment it precaches, so it asks then and sends the answer.
    /// </para>
    ///
    /// <para>
    /// It is also the number the wrap divides by. See
    /// <see cref="SpriteFrameCursor"/> for why a zero here is the same fault as
    /// a zero denominator anywhere else in this family.
    /// </para>
    /// </summary>
    public partial int MaxFrames { get; set; }
}
