using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// What answers the engine's skeleton asks of a client standalone entity:
/// the client's managed skeletons (plan physics_bones_native_zero steps
/// 1.1-1.2, client half). Each ask hands over the entity's engine block, so it
/// costs its one crossing and no read back.
/// </summary>
public interface IClientBoneAnswers
{
    /// <summary>
    /// <c>IClientRenderable::SetupBones</c>: the skeleton written into the
    /// engine's array in place (12 floats a bone; every slot past the model's
    /// bones is the entity transform). An empty <paramref name="boneToWorld"/>
    /// only fills the skeleton and answers whether there are bones; otherwise
    /// the answer is true.
    /// </summary>
    bool SetupBones(EntityHandle entity, ReadOnlySpan<byte> block, Span<float> boneToWorld, int boneMask,
                    float currentTime, int frameCount);

    /// <summary>
    /// <c>ICollideable::TestHitboxes</c>: true means "tested", and only then is
    /// the flat trace (<see cref="BoneTraceFlat"/>) written back.
    /// </summary>
    bool TestHitboxes(EntityHandle entity, ReadOnlySpan<byte> block, ReadOnlySpan<float> ray, int contentsMask,
                      Span<float> traceFloats, Span<int> traceInts, float currentTime, int frameCount);

    /// <summary>
    /// <c>IClientRenderable::GetAttachment( number, matrix )</c>: a ONE-based
    /// attachment's world matrix (12 floats), or false with nothing written.
    /// </summary>
    bool GetAttachment(EntityHandle entity, ReadOnlySpan<byte> block, int attachment, Span<float> attachmentToWorld,
                       float currentTime, int frameCount);

    /// <summary><c>IClientRenderable::LookupAttachment</c>: ONE-based, 0 for no such name.</summary>
    int LookupAttachment(EntityHandle entity, ReadOnlySpan<byte> block, string name);

    /// <summary>
    /// A legacy temp entity's <c>IClientRenderable::SetupBones</c>: the model at
    /// sequence 0, cycle 0, its pose parameters ZERO (stock's C_BaseAnimating
    /// for a tempent that never set one), placed at <paramref name="originAngles"/>
    /// (x, y, z, pitch, yaw, roll); every slot the build did not write -- every
    /// slot, for a model that cannot be posed -- is
    /// <paramref name="renderToWorld"/>. False writes nothing.
    /// </summary>
    bool PoseModel(int modelIndex, ReadOnlySpan<float> originAngles, ReadOnlySpan<float> renderToWorld,
                   Span<float> boneToWorld, int boneMask, float currentTime, int frameCount);

    /// <summary>
    /// <c>BoneBatch</c> (plan_physics_bones_perf A2): asked once a frame, at the
    /// frame's first SetupBones ask. The skeletons the frame is expected to
    /// draw posed ahead into a table native answers the frame's later asks
    /// from (src/shared/native/standalone_bone_batch.h), and its address; zero
    /// for no table, when every ask crosses as before.
    /// </summary>
    nint BoneBatch(int frameCount, float currentTime);
}

/// <summary>
/// <see cref="IManagedBonesClient"/>, managed-built: the client's engine-facing
/// skeleton asks. Every body counts its crossing; a null pointer native should
/// never hand over answers "no skeleton", and a throw is logged and answers
/// the same.
/// </summary>
public sealed unsafe class BonesClientBinding(Crossings crossings, ILog log, IClientBoneAnswers bones)
    : IManagedBonesClient
{
    /// <summary>Floats per bone matrix (matrix3x4_t).</summary>
    public const int MatrixFloats = 12;

    private static ReadOnlySpan<byte> Block(void* p) => new(p, EngineBlock.SizeBytes);

    public int SetupBones(uint a0, void* a1, void* a2, int a3, int a4, float a5, int a6)
    {
        crossings.Count(CrossingOp.ClientBonesSetupBones);

        if (a1 == null)
            return 0;

        try
        {
            Span<float> output = a2 == null || a3 <= 0 ? Span<float>.Empty : new Span<float>(a2, a3 * MatrixFloats);
            return bones.SetupBones(new EntityHandle(a0), Block(a1), output, a4, a5, a6) ? 1 : 0;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedBonesClient.SetupBones", e);
            return 0;
        }
    }

    public int TestHitboxes(uint a0, void* a1, void* a2, int a3, void* a4, void* a5, float a6, int a7)
    {
        crossings.Count(CrossingOp.ClientBonesTestHitboxes);

        if (a1 == null || a2 == null || a4 == null || a5 == null)
            return 0;

        try
        {
            return bones.TestHitboxes(new EntityHandle(a0), Block(a1),
                                      new ReadOnlySpan<float>(a2, BoneTraceFlat.RayFloats), a3,
                                      new Span<float>(a4, BoneTraceFlat.TraceFloats),
                                      new Span<int>(a5, BoneTraceFlat.TraceInts),
                                      a6, a7) ? 1 : 0;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedBonesClient.TestHitboxes", e);
            return 0;
        }
    }

    public int GetAttachment(uint a0, void* a1, int a2, void* a3, float a4, int a5)
    {
        crossings.Count(CrossingOp.ClientBonesGetAttachment);

        if (a1 == null || a3 == null)
            return 0;

        try
        {
            return bones.GetAttachment(new EntityHandle(a0), Block(a1), a2, new Span<float>(a3, MatrixFloats),
                                       a4, a5) ? 1 : 0;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedBonesClient.GetAttachment", e);
            return 0;
        }
    }

    public int PoseModel(int a0, void* a1, void* a2, void* a3, int a4, int a5, float a6, int a7)
    {
        crossings.Count(CrossingOp.ClientBonesPoseModel);

        if (a1 == null || a2 == null || a3 == null || a4 <= 0)
            return 0;

        try
        {
            return bones.PoseModel(a0, new ReadOnlySpan<float>(a1, 6), new ReadOnlySpan<float>(a2, MatrixFloats),
                                   new Span<float>(a3, a4 * MatrixFloats), a5, a6, a7) ? 1 : 0;
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedBonesClient.PoseModel", e);
            return 0;
        }
    }

    public void* BoneBatch(int a0, float a1)
    {
        crossings.Count(CrossingOp.ClientBonesBatch);

        try
        {
            return (void*)bones.BoneBatch(a0, a1);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedBonesClient.BoneBatch", e);
            return null;
        }
    }

    public int LookupAttachment(uint a0, void* a1, byte* a2)
    {
        crossings.Count(CrossingOp.ClientBonesLookupAttachment);

        if (a1 == null || a2 == null)
            return 0;

        try
        {
            string? name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8((nint)a2);
            return string.IsNullOrEmpty(name) ? 0 : bones.LookupAttachment(new EntityHandle(a0), Block(a1), name);
        }
        catch (Exception e)
        {
            ManagedVtable.Fail(log, "IManagedBonesClient.LookupAttachment", e);
            return 0;
        }
    }
}

/// <summary>
/// The client's standalone engine blocks by handle (IShimStandaloneClient's
/// StandaloneEngineBlock): one crossing a read, empty for a handle that names
/// no standalone entity.
/// </summary>
public sealed unsafe class ClientStandaloneBlocks(ModuleEngine engine) : IStandaloneBlocks
{
    public Span<byte> Block(EntityHandle entity)
    {
        EngineSide api = engine.Api;

        if (!api.ClientAttached)
            return Span<byte>.Empty;

        int size;
        void* p = api.ClientShims.StandaloneEngineBlock(entity.Value, &size);

        return p == null || size < EngineBlock.SizeBytes ? Span<byte>.Empty : new Span<byte>(p, EngineBlock.SizeBytes);
    }
}

/// <summary><see cref="IClientSequenceStore"/> over the engine: one crossing a store.</summary>
public sealed class ClientSequenceStore(ModuleEngine engine) : IClientSequenceStore
{
    public void Store(EntityHandle entity, int sequence, float playbackRate, float animTime, float duration,
                      bool looping)
    {
        EngineSide api = engine.Api;

        if (api.ClientAttached)
            api.ClientShims.StandaloneStoreSequence(entity.Value, sequence, playbackRate, animTime, duration,
                                                    looping ? 1 : 0);
    }
}

/// <summary><see cref="IStandaloneRagdollStore"/> over the engine: one crossing a store.</summary>
public sealed class StandaloneRagdollStore(ModuleEngine engine) : IStandaloneRagdollStore
{
    public void SetRagdoll(EntityHandle entity, bool isRagdoll)
    {
        EngineSide api = engine.Api;

        if (api.ClientAttached)
            api.ClientShims.StandaloneSetRagdoll(entity.Value, isRagdoll ? 1 : 0);
    }
}
