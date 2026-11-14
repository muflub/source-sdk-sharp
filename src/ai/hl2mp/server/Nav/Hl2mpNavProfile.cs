using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.Hl2mp.Nav;

/// <summary>
/// HL2MP's navigation profile (<see cref="INavProfile"/>): the core's default
/// movement with the file's crouch-jump height (<see cref="NavNumbers.Stock"/>),
/// the numbers the derivation used before profiles, so HL2MP's graphs are the
/// same graphs; no player reaches past a crouch jump, and nothing is solid to one team alone.
/// </summary>
[SideService(Side.Server, As = [typeof(INavProfile)])]
public sealed class Hl2mpNavProfile : INavProfile
{
    public NavNumbers Numbers(float fileJumpHeight) => NavNumbers.Stock(fileJumpHeight);

    public JumpReach Reach(EntityHandle player) => JumpReach.None;

    /// <summary>HL2MP has no entity solid to one team alone.</summary>
    public IReadOnlyList<TeamSolid> TeamSolids(MapEntities entities) => [];
}
