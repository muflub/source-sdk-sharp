using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Client.Buildings;

/// <summary>
/// The client's answer to the builder's placement
/// (<see cref="TF2.Shared.ITFBuilderPlacement"/>): the blueprint is the
/// server's, so the replay asks nothing of it. Every arm is the behaviour's
/// own state write beside <c>attack.EffectsLive</c> — the server's — and the
/// holster's stop has no placement to take back here.
/// </summary>
[SideService(Side.Client, As = [typeof(TF2.Shared.ITFBuilderPlacement)])]
public sealed class TFClientBuilderPlacement : TF2.Shared.ITFBuilderPlacement
{
    public void StartPlacement(int ownerIndex) { }

    public void StartAttachmentPlacement(int ownerIndex) { }

    public void UpdatePlacement(int ownerIndex) { }

    public bool Place(int ownerIndex) => false;

    public void Rotate(int ownerIndex) { }

    public void StopPlacement(int ownerIndex) { }

    public bool ArmBuilder(int ownerIndex) => false;

    public bool BeginOrder(EntityHandle player, TFObjectType type, int mode) => false;
}
