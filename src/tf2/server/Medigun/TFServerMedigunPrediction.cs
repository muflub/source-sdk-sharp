using SourceSharp.Abi;

using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Medigun;

/// <summary>
/// The server's answer to the medigun's replayed half
/// (<see cref="TF2.Shared.ITFMedigunPrediction"/>): nothing. Stock compiles one
/// <c>ItemPostFrame</c> for both sides, and the split gives the server the
/// whole frame — the effects arm (<c>TFMedigunWeapon</c>) and the behaviour's
/// own clocks — so there is no replay for the server to run, and its answer
/// holds none.
/// </summary>
[SideService(Side.Server, As = [typeof(TF2.Shared.ITFMedigunPrediction)])]
public sealed class TFServerMedigunPrediction : TF2.Shared.ITFMedigunPrediction
{
    public void Frame(in WeaponAttackContext attack, TFWeaponScript script, bool held) { }
}
