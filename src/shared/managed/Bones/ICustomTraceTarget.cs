namespace SourceSharp.Shared.Bones;

/// <summary>
/// An entity that answers the engine's traces against it itself: stock's entity whose solid flags
/// carry <c>FSOLID_CUSTOMRAYTEST</c> or <c>FSOLID_CUSTOMBOXTEST</c>, whose <c>TestCollision</c> the
/// engine calls (enginetrace.cpp's ClipRayToCustom) and which answers through <c>TestHitboxes</c>.
/// The shells route that call to managed code (IManagedBones / IManagedBonesClient::TestHitboxes);
/// the skeletons ask the entity first, so one that implements this answers instead of its
/// hitboxes. Implemented on a server <c>ScriptedEntity</c> or a client standalone entity.
/// </summary>
public interface ICustomTraceTarget
{
    /// <summary>
    /// <paramref name="ray"/> (a ray, or a box swept) against the entity: write the hit into
    /// <paramref name="tr"/> (its fraction, end, plane, contents, hitbox) and answer true when the
    /// entity tested it, whether or not it hit (stock's TestHitboxes contract); false leaves the
    /// trace to the entity's box.
    /// </summary>
    bool TestTrace(in BoneRay ray, int contentsMask, BoneTrace tr);
}
