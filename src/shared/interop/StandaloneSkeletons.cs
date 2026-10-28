namespace SourceSharp.Abi;

/// <summary>What a skeleton did with a model scale.</summary>
public enum SkeletonScaleChange
{
    /// <summary>Not a standalone entity: nothing answered.</summary>
    NotStandalone,

    /// <summary>Zero or less: refused, nothing changed, and said so.</summary>
    Refused,

    /// <summary>The scale already held: nothing changed.</summary>
    Unchanged,

    /// <summary>Stored, and the skeleton's cache invalidated; the caller stores
    /// the number where native code reads it (the player's movement).</summary>
    Changed,
}

/// <summary>
/// One side's standalone skeletons, answered in managed code. Every member
/// answers "not mine" (false, 0, null, empty) for a handle that names no
/// standalone entity, which is the same answer the native slots gave.
/// </summary>
public interface IStandaloneSkeletons
{
    /// <summary>
    /// A ONE-based attachment's world frame. <paramref name="standalone"/> is
    /// false when the handle names no standalone entity (and nothing is
    /// written); otherwise the out values are written either way -- the
    /// entity's own place on failure -- and the answer is whether the model
    /// carries that attachment.
    /// </summary>
    bool GetAttachment(EntityHandle entity, int attachment, out Vector origin, out QAngle angles,
                       out bool standalone);

    /// <summary>A name to its ONE-based attachment; 0 for none, and for a
    /// handle that names no standalone entity.</summary>
    int LookupAttachment(EntityHandle entity, string name);

    /// <summary>How many bones the skeleton holds right now (0 until something
    /// asked it to pose).</summary>
    int CachedBoneCount(EntityHandle entity);

    /// <summary>The skeleton, posed for everything a renderer would ask, or
    /// null when there is none to pose.</summary>
    StandaloneBones? BoneMatrices(EntityHandle entity);

    /// <summary>Whether the model worn can be posed at all.</summary>
    bool IsPoseable(EntityHandle entity);

    /// <summary><c>studiohdr_t::name</c> of the model worn, or empty.</summary>
    string StudioName(EntityHandle entity);

    /// <summary>
    /// The sequence the skeleton poses from. False when the handle names no
    /// standalone entity.
    /// </summary>
    bool SetSequence(EntityHandle entity, int sequence, float playbackRate, bool restart);

    /// <summary>
    /// <c>SetCycle</c> on a sequence nothing advances: the pose holds that
    /// cycle until the next sequence starts. False when the handle names no
    /// standalone entity.
    /// </summary>
    bool SetCycle(EntityHandle entity, float cycle);

    /// <summary>How big the skeleton is built.</summary>
    SkeletonScaleChange SetModelScale(EntityHandle entity, float scale);
}

/// <summary>
/// The client's skeleton asks beyond the shared ones (plan
/// physics_bones_native_zero 1.2, client half): the layers, the pose
/// parameters, the merge source and the sequence clock, which only the client
/// poses from. Each answers as the native slot it replaces answered for a
/// handle that names no standalone entity (0, -1, false, empty).
/// </summary>
public interface IClientStandaloneSkeletons : IStandaloneSkeletons
{
    /// <summary>One layer set or switched off (a negative sequence).</summary>
    void SetAnimLayer(EntityHandle entity, int slot, int sequence, float weight, float playbackRate, int order,
                      bool restart);

    /// <summary>What is in a slot; answers how many slots carry something.</summary>
    int AnimLayer(EntityHandle entity, int slot, out int sequence, out float weight, out int order, out float cycle);

    /// <summary>The playing sequence, its cycle now, its duration, loop flag and start count.</summary>
    bool SequenceState(EntityHandle entity, out int sequence, out float cycle, out float duration, out bool looping,
                       out int starts);

    /// <summary>The playing sequence's duration and loop flag, as read when it started.</summary>
    bool SequenceInfo(EntityHandle entity, out float duration, out bool looping);

    /// <summary>How fast the sequence's root moves, at this entity's pose (0 for none).</summary>
    float SequenceGroundSpeed(EntityHandle entity, int sequence);

    /// <summary>How many pose parameters the model declares.</summary>
    int PoseParameterCount(EntityHandle entity);

    /// <summary>A pose parameter by name (case-insensitive), or -1.</summary>
    int LookupPoseParameter(EntityHandle entity, string name);

    /// <summary>A pose parameter's name, or empty.</summary>
    string PoseParameterName(EntityHandle entity, int parameter);

    /// <summary>A pose parameter's declared range.</summary>
    bool PoseParameterRange(EntityHandle entity, int parameter, out float start, out float end);

    /// <summary>Set in model units; answers the value stored, in model units.</summary>
    float SetPoseParameter(EntityHandle entity, int parameter, float value);

    /// <summary>The value held, in model units.</summary>
    float GetPoseParameter(EntityHandle entity, int parameter);

    /// <summary>EF_BONEMERGE's follower half: the source by client entity index, -1 to stop.</summary>
    void SetBoneMergeSource(EntityHandle entity, int sourceEntityIndex);

    /// <summary>The model's procedural bones by kind; answers the bone count.</summary>
    int ProceduralBones(EntityHandle entity, out int computed, out int jiggle, out int ikChains);
}

/// <summary>
/// Where a side's engine bindings find its <see cref="IStandaloneSkeletons"/>:
/// registered before the engine areas are built and filled once the skeleton
/// exists.
/// </summary>
public sealed class StandaloneSkeletonRoute
{
    /// <summary>The skeletons, or null for "ask native".</summary>
    public IStandaloneSkeletons? Skeletons { get; set; }
}
