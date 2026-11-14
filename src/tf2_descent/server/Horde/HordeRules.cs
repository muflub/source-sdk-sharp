using SourceSharp.Abi;

using TF2.Descent.Rules;

namespace TF2.Descent.Server.Horde;

/// <summary>The rules the horde plays by: TF2 Descent's own, the one instance the server registered.</summary>
public interface IHordeRules
{
    TF2DescentRules Rules { get; }
}

/// <summary>
/// The rules module the server registered under the host's contract
/// (<c>TF2DescentServerRegistrar</c>), seen as what it is. One instance for
/// the side: a second <see cref="TF2DescentRules"/> would load the content a
/// second time and could disagree with the first about nothing but its hash.
/// </summary>
[SideService(Side.Server, As = [typeof(IHordeRules)])]
public sealed class HordeRules(SourceSharp.Host.Contracts.IGameRules rules) : IHordeRules
{
    public TF2DescentRules Rules => (TF2DescentRules)rules;
}
