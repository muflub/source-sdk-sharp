using SourceSharp.Abi;

namespace TF2.Client.Player;

/// <summary>
/// The client's answer to the sapper kills' stored crits
/// (<see cref="TF2.Shared.ITFRevengeCrits"/>): the bucket is the server's,
/// the client's boost comes on the condition state, so the replay grants and
/// spends nothing.
/// </summary>
[SideService(Side.Client, As = [typeof(TF2.Shared.ITFRevengeCrits)])]
public sealed class TFClientRevengeCrits : TF2.Shared.ITFRevengeCrits
{
    public void Grant(EntityHandle owner) { }

    public void SpendOnShot(int ownerIndex) { }
}
