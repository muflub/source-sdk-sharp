using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Client.Standalone;

/// <summary>
/// The client half of <c>prop_dynamic_ornament</c>: the half stock spells
/// <c>EF_BONEMERGE</c>.
///
/// <para>
/// The server has already PLACED this entity — the parallel hierarchy composes a
/// child at the instant its parent moves and sends the result as an ordinary
/// origin, so an ornament arrives here looking exactly like something that moved
/// by itself. What does not arrive, and cannot, is the POSE: a bone merge is a
/// per-viewer rendering decision made against a skeleton only this side has.
/// </para>
///
/// <para>
/// So this class does the one thing the server could not: it points the bone
/// merge at whatever the ornament is riding. On a model with no skeleton this
/// changes nothing and the origin the server sent is the whole answer; on one
/// that animates, the ornament now follows the bones instead of sliding around
/// on the entity's origin, which is what stock's effect flag was for.
/// </para>
/// </summary>
[ClientStandaloneEntity(EntityNames.PropDynamicOrnament)]
public sealed class ClientPropDynamicOrnament : ClientStandaloneEntity
{
    public ClientPropDynamicOrnament(IEngineEntityRender render)
        : base(render)
    {
    }

    public override string ClassName => EntityNames.PropDynamicOrnament;

    /// <summary>What the server says this is riding. See <see cref="OrnamentNetState"/>.</summary>
    [NetState] public OrnamentNetState State = new();

    // What the merge is CURRENTLY pointed at, so an update that did not change
    // it does not re-ask. ClientSsWeapon keeps the same latch for the same
    // reason: SetBoneMergeSource invalidates the merge cache, so calling it
    // every update would rebuild the bone map every frame and answer the same
    // thing each time.
    private int _mergedOnto = -1;

    public override void OnDataChanged(ReadOnlySpan<byte> blob, DataUpdateType updateType)
    {
        int attached = State.AttachedToIndex;

        // A detached ornament is not drawn at all — the server has made it
        // invisible — but the merge is still released rather than left pointing
        // at an entity this one no longer follows. A stale merge source is a
        // reference the client would keep revalidating for nothing, and it would
        // come back wrong the moment the ornament were re-attached to something
        // else while the latch still said the old index.
        if (attached <= 0)
        {
            if (_mergedOnto >= 0)
            {
                Render.SetBoneMergeSource(Handle, -1);
                _mergedOnto = -1;
            }

            return;
        }

        if (attached == _mergedOnto)
            return;

        Render.SetBoneMergeSource(Handle, attached);
        _mergedOnto = attached;
    }

    public override void OnRemoved()
    {
        // The latch, not the merge. The entity is going, so there is nothing left
        // to release the source ON; what matters is that a recycled instance does
        // not start life believing it is already merged onto the index the last
        // one used.
        _mergedOnto = -1;
    }
}
