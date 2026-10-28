namespace SourceSharp.Abi;

/// <summary>
/// One piece a model breaks into, as the modeller compiled it.
///
/// <para>
/// Mirrors <c>SourceSharpBreakPiece</c> in sourcesharp.h field for field, which
/// in turn is stock's <c>breakmodel_t</c> minus the two fields nothing on this
/// hierarchy can act on. It carries no model name: the name is a string and
/// comes back beside the struct rather than inside it, so that neither this
/// type nor any caller has to be <c>unsafe</c>.
/// </para>
///
/// <para>
/// Where the list comes from is worth stating, because there are two prop
/// KeyValue blobs and this is the OTHER one. Health, the damage modifiers and
/// the explosion live in <c>prop_data</c>, which is text compiled into the .mdl
/// and is what <see cref="IEngineEntities.GetPropData"/> reads. The pieces live
/// in the .phy — the compiled collision data — behind a binary parser, and are
/// the reason there is an ABI slot for them at all.
/// </para>
/// </summary>
public struct BreakPiece
{
    /// <summary>
    /// Where the piece sits in the breaking prop's own frame, before that
    /// prop's rotation.
    ///
    /// <para>
    /// <see cref="BreakModels"/> has already applied the "placementOrigin"
    /// correction, from the two models' descriptors. What is left is
    /// rotate-and-add, which is <see cref="Transform"/>'s job.
    /// </para>
    /// </summary>
    public Vector Offset;

    /// <summary>
    /// The block's own <c>velocity</c> key, in the same frame. Almost never
    /// written; zero when it is not, which is not the same as "no opinion" —
    /// stock adds it to whatever the prop was already doing.
    /// </summary>
    public Vector Velocity;

    /// <summary>How much the piece itself takes before breaking again.</summary>
    public float Health;

    /// <summary>Seconds before the piece fades and goes. Stock defaults to 20.</summary>
    public float FadeTime;

    /// <summary>
    /// The distance band the piece is drawn in. Both zero means "always",
    /// which is what a block that writes neither leaves behind.
    /// </summary>
    public float FadeMinDist;
    public float FadeMaxDist;

    /// <summary>
    /// How hard this piece is thrown away from the break, overriding the
    /// caller's default where the block wrote a <c>burst</c>.
    /// </summary>
    public float BurstScale;

    /// <summary>What it collides with. Debris unless the block said otherwise.</summary>
    public int CollisionGroup;

    /// <summary>A jointed body rather than a lump.</summary>
    public int IsRagdoll;

    /// <summary>Bolted where it lands rather than simulated.</summary>
    public int IsMotionDisabled;

    /// <summary>
    /// Stock's <c>mp_break_t</c>: 0 default, 1 server, 2 client, 3 both. In
    /// multiplayer it decides which DLL makes the piece (PropBreak's
    /// BreakPieceSide on the server, ClientPhysicsProps on the client).
    /// </summary>
    public int MultiplayerBreakMode;
}
