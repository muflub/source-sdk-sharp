using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What the client needs in order to draw a <c>prop_dynamic_ornament</c> on the
/// right bones.
///
/// <para>
/// ONE FIELD, and the reason it exists at all is stock's <c>FollowEntity</c>.
/// That call is two operations: <c>SetParent</c>, which places the ornament and
/// is entirely the server's business, and <c>EF_BONEMERGE</c>, which is a
/// RENDERING instruction and is entirely the client's. Stock sends the second as
/// an effect flag on <c>m_fEffects</c> and lets the client work out what to merge
/// onto by walking <c>m_hMoveParent</c> — which this port has no equivalent of,
/// because the parallel hierarchy composes children on the server and sends
/// positions rather than links.
/// </para>
///
/// <para>
/// So the merge SOURCE is named directly instead of being derived from a
/// hierarchy the client does not have. That is a smaller wire cost than stock's
/// — one int against an effect flag plus a networked handle — and it removes the
/// step where the two sides could disagree about who the parent is.
/// </para>
///
/// <para>
/// An ENTINDEX rather than a handle, for the reason every cross-wire entity
/// reference here uses one: an index means the same thing on both sides without
/// the two having to agree about serial numbers. -1 is detached.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.PropDynamicOrnament)]
public partial class OrnamentNetState
{
    /// <summary>
    /// Entindex of the entity this ornament rides, or -1 when it rides nothing.
    ///
    /// <para>
    /// NOT predicted. An ornament is scenery: nothing a player does moves it, so
    /// there is no input to predict from and a mispredict would be a decoration
    /// snapping between two bones for a frame.
    /// </para>
    /// </summary>
    public partial int AttachedToIndex { get; set; }
}
