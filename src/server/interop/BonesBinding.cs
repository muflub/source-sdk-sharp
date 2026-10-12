using SourceSharp.Abi;

namespace SourceSharp.Server.Interop;

/// <summary>
/// What answers an engine hitbox trace against a standalone entity: the
/// server's managed skeletons. The trace arrives flat (<see cref="BoneTraceFlat"/>)
/// and is written back in place; true means "tested" (stock's
/// ICollideable::TestHitboxes), and only then is anything written.
/// </summary>
public interface IStandaloneHitboxes
{
    bool TestHitboxes(EntityHandle entity, Span<byte> block, ReadOnlySpan<float> ray, int contentsMask,
                      Span<float> traceFloats, Span<int> traceInts);
}

/// <summary>
/// IManagedBones (plan physics_bones_native_zero step 1.1): what the engine
/// still has to ask a standalone entity's skeleton through C++ -- the
/// ICollideable::TestHitboxes its traces call. Native hands over the entity's
/// engine block with the ray, so the trace costs this one crossing and no read
/// back.
/// </summary>
public sealed unsafe class BonesBinding(Crossings crossings, IStandaloneHitboxes hitboxes) : IManagedBones
{
    public int TestHitboxes(uint a0, void* a1, void* a2, int a3, void* a4, void* a5)
    {
        crossings.Count(CrossingOp.ServerBonesTestHitboxes);

        if (a1 == null || a2 == null || a4 == null || a5 == null)
            return 0;

        return hitboxes.TestHitboxes(new EntityHandle(a0),
                                     new Span<byte>(a1, EngineBlock.SizeBytes),
                                     new ReadOnlySpan<float>(a2, BoneTraceFlat.RayFloats),
                                     a3,
                                     new Span<float>(a4, BoneTraceFlat.TraceFloats),
                                     new Span<int>(a5, BoneTraceFlat.TraceInts)) ? 1 : 0;
    }
}
