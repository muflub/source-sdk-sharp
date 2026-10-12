extern alias client;

using SourceSharp.Abi;
using client::SourceSharp.Client.Ragdolls;
using client::SourceSharp.Shared.Bones;

namespace SourceSharp.Tests;

/// <summary>
/// The client's ragdolls, recorded into a <see cref="RecordingEngine"/> under
/// the member names they had when they were engine slots
/// ("EntityRender.BecomeRagdoll", "EntityRender.RagdollState",
/// "EntityRender.RagdollApplyForceCenter"), so a body's conversation with them
/// still reads as one ordered log beside its engine calls
/// (plan_physics_bones_native_zero Phase 3: the bodies are managed's own now,
/// and the double's assembly cannot see them).
/// </summary>
public sealed class RecordingRagdolls(RecordingEngine engine) : IClientRagdolls
{
    public int Count => 0;

    public bool Become(EntityHandle owner, Vector force, int forceBone, out RagdollRefusal refusal)
    {
        bool became = engine.RecordAnswer<bool>("EntityRender.BecomeRagdoll", owner, force, forceBone);
        refusal = became ? RagdollRefusal.None : RagdollRefusal.NotJointed;
        return became;
    }

    public bool IsRagdoll(EntityHandle owner)
        => (engine.Answer<RagdollState>("EntityRender.RagdollState") & RagdollState.Present) != 0;

    public uint Serial(EntityHandle owner) => IsRagdoll(owner) ? 1u : 0u;

    public RagdollState State(EntityHandle owner, out Vector origin)
    {
        origin = engine.Answer<Vector>("EntityRender.RagdollState.origin");
        return engine.RecordAnswer<RagdollState>("EntityRender.RagdollState", owner);
    }

    public bool ApplyForceCenter(EntityHandle owner, Vector force)
        => engine.RecordAnswer<bool>("EntityRender.RagdollApplyForceCenter", owner, force);

    public int WriteBones(EntityHandle owner, Span<BoneMatrix3x4> bones, Span<int> owned) => -1;

    public bool Destroy(EntityHandle owner) => false;

    public int DestroyAll() => 0;

    public int StepPoses() => 0;
}
