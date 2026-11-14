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
/// </summary>
public interface IEntityCollideRule
{
    /// <summary>Whether <paramref name="entity"/> (a game entity) is hit by a trace in collision group <paramref name="group"/> (the trace's own group, stock's <c>m_collisionGroup</c>, never the entity's) whose mask is <paramref name="contentsMask"/>.</summary>
    bool ShouldCollide(EntityHandle entity, CollisionGroup group, int contentsMask);
}
