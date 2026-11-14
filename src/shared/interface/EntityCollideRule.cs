using SourceSharp.Abi;

namespace SourceSharp.Shared;

/// <summary>
/// One mod's per-entity exception to the trace filter's answer, asked as a
/// chain from <c>TraceFilterRules.ShouldHitEntity</c> after the pass checks
/// and before the collision-group matrix: stock's
/// <c>CTraceFilterSimple::ShouldHitEntity</c> closing on the touched entity's
/// own <c>ShouldCollide(collisionGroup, contentsMask)</c>
/// (baseentity.cpp), which this server's hierarchy answers with nothing. A
/// mod that registers no rule leaves the filter exactly as it was; every
/// registered rule must agree for the candidate to be hit.
///
/// <para>Game entities only: the filter asks nothing else, having already
/// answered for the world, static props and non-entities.</para>
///
/// <para>
/// THE MOVER IS PART OF THE QUESTION, where stock's virtual has only the
/// touched entity and carries the mover's team in the contents mask
/// (<c>CTFGameMovement::PlayerSolidMask</c> adds the other team's bit and
/// <c>CTFPlayer::ShouldCollide</c> refuses a mask without its own). The mask
/// has two team bits (<c>CONTENTS_TEAM1</c>/<c>CONTENTS_TEAM2</c>,
/// bspflags.h) and a team mode may have four teams, so a rule can compare the
/// two entities instead; the mask is still handed over for a rule that keeps
/// stock's arrangement.
/// </para>
///
/// <para>
/// Asked on the server by the trace filter and on the client by the
/// prediction's player clip (<c>PredictionPlayerClip</c>, the client's engine
/// trace hitting no entity), so a rule registered on both sides moves the
/// predicted player as the server moves it.
/// </para>
/// </summary>
public interface IEntityCollideRule
{
    /// <summary>Whether <paramref name="entity"/> (a game entity) is hit by a trace whose pass entity is <paramref name="mover"/> (<see cref="EntityHandle.None"/> for a trace with none: a movement trace's is the player it moves), in collision group <paramref name="group"/> (the trace's own group, stock's <c>m_collisionGroup</c>, never the entity's), whose mask is <paramref name="contentsMask"/>.</summary>
    bool ShouldCollide(EntityHandle mover, EntityHandle entity, CollisionGroup group, int contentsMask);
}
