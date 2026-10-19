using SourceSharp.Abi;

namespace SourceSharp.Client.Interop;

/// <summary>
/// Where native keeps the sequence clock the managed skeleton chose
/// (IShimStandaloneClient's StandaloneStoreSequence): the block's sequence,
/// rate and start, and the duration and loop flag the native per-frame cycle
/// reads. A store; managed decides.
/// </summary>
public interface IClientSequenceStore
{
    void Store(EntityHandle entity, int sequence, float playbackRate, float animTime, float duration, bool looping);
}

/// <summary>
/// Where native keeps the one fact about a managed ragdoll it needs
/// (IShimStandaloneClient's StandaloneSetRagdoll): the solver owns the
/// entity's pose. A store; managed decides (plan_physics_bones_native_zero Phase 3).
/// </summary>
public interface IStandaloneRagdollStore
{
    void SetRagdoll(EntityHandle entity, bool isRagdoll);
}
