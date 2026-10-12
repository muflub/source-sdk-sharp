using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// What a func_lod tells the client: the distance at which it is gone.
///
/// <para>
/// One number, because that is genuinely the entity. Everything else about a
/// func_lod — where it is, what brushes it is made of, whether it is solid — is
/// either already on the wire for every entity or is a server-side decision the
/// client never needs.
/// </para>
///
/// <para>
/// The transition WIDTH is not here and is not networked in stock either: it is
/// <c>lod_TransitionDist</c>, a client convar, so how gradually the fade happens
/// is the player's setting and how far away it happens is the mapper's. That
/// split is worth keeping — it is the difference between a level design decision
/// and a graphics one.
/// </para>
/// </summary>
[StandaloneNetState(EntityNames.FuncLod)]
public partial class LodNetState
{
    /// <summary>Distance past which the brush is not drawn at all.</summary>
    public partial float DisappearDist { get; set; }
}
