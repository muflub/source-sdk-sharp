namespace SourceSharp.Abi;

/// <summary>
/// Where in a client frame the engine currently is — the argument of
/// <c>IBaseClientDLL::FrameStageNotify</c>, mirrored.
///
/// <para>
/// <b>THE NUMBERS ARE THE ENGINE'S.</b> This is not a managed enum the shell
/// translates into; it is <c>ClientFrameStage_t</c> from
/// <c>public/cdll_int.h:135</c>, and the shell passes the engine's own value
/// through unchanged. So the values are a real ABI: reordering them here does
/// not renumber anything, it only makes this file lie about what the engine
/// sent. <c>ClientFrameStageTests</c> pins each one against the header rather
/// than against this comment.
/// </para>
///
/// <para>
/// <b>Why mirror it at all rather than take a bare int.</b> The port's client
/// work is hung off these stages — interpolation runs at
/// <see cref="NetUpdateEnd"/>, the render tail at <see cref="RenderStart"/> —
/// and a <c>switch (stage)</c> over named cases is checkable by the compiler
/// where a <c>switch (5)</c> is checkable by nobody. The cost of the mirror is
/// this file and one test; the cost of not having it is a magic number in every
/// later phase.
/// </para>
///
/// <para>
/// <b><see cref="Undefined"/> is -1 and is reachable.</b> It is the value
/// <c>g_CurFrameStage</c> holds before the first frame; the engine does not
/// raise it as a notification, but a managed reader that assumed the range
/// started at zero would be wrong about the one moment it matters.
/// </para>
/// </summary>
public enum ClientFrameStage
{
    /// <summary>No frame has run yet. <c>FRAME_UNDEFINED</c>.</summary>
    Undefined = -1,

    /// <summary>
    /// The top of a client frame. <c>FRAME_START</c> — stock opened the frame
    /// for client effect and beam additions here.
    /// </summary>
    Start = 0,

    /// <summary>
    /// A network packet is arriving. <c>FRAME_NET_UPDATE_START</c> — stock
    /// disabled absolute-transform recomputation across the update here.
    /// </summary>
    NetUpdateStart = 1,

    /// <summary>
    /// Data has arrived and <c>PostDataUpdate</c> is about to be called on its
    /// recipients. <c>FRAME_NET_UPDATE_POSTDATAUPDATE_START</c>.
    /// </summary>
    NetUpdatePostDataUpdateStart = 2,

    /// <summary>
    /// Every recipient has had <c>PostDataUpdate</c>.
    /// <c>FRAME_NET_UPDATE_POSTDATAUPDATE_END</c> — where prediction copies off
    /// pristine data.
    /// </summary>
    NetUpdatePostDataUpdateEnd = 3,

    /// <summary>
    /// Every packet for this frame has been processed; interpolation and
    /// prediction may run. <c>FRAME_NET_UPDATE_END</c>.
    /// </summary>
    NetUpdateEnd = 4,

    /// <summary>
    /// About to render. <c>FRAME_RENDER_START</c> — the last thing before the
    /// scene, and where the client's simulation runs.
    /// </summary>
    RenderStart = 5,

    /// <summary>The scene is drawn. <c>FRAME_RENDER_END</c>.</summary>
    RenderEnd = 6,
}
