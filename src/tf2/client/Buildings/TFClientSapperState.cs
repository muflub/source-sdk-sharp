using SourceSharp.Abi;

using SourceSharp.Shared;

namespace TF2.Client.Buildings;

/// <summary>
/// The client's answer to the sapper's use
/// (<see cref="TF2.Shared.ITFSapperState"/>): the sap and the sapper's wound
/// are the server's, so the replay's asks — the placement, the finds, the
/// wrench's and the melee's building hits — all answer none.
/// </summary>
[SideService(Side.Client, As = [typeof(TF2.Shared.ITFSapperState)])]
public sealed class TFClientSapperState : TF2.Shared.ITFSapperState
{
    public bool PlaceSapper(int ownerIndex) => false;

    public bool FindSappable(int ownerIndex, out EntityHandle building, out Vector point)
    {
        building = EntityHandle.None;
        point = default;
        return false;
    }

    public bool WrenchSmack(int ownerIndex, float range) => false;

    public bool MeleeSmack(int ownerIndex, float range, float amount) => false;
}
