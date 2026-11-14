using SourceSharp.Abi;
using SourceSharp.Ai.Server.Experience;
using SourceSharp.Server;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>
/// HL2MP's answer to the experience feed (<see cref="IExperienceGame"/>): it
/// keeps none (plan_bots_teams_tf2 Part G, Q-G2: the store is TF2's only). No
/// file is opened and no player is sampled on an HL2MP server, and its bots
/// read the empty experience.
/// </summary>
[SideService(Side.Server, As = [typeof(IExperienceGame)])]
public sealed class Hl2mpExperienceGame : IExperienceGame
{
    public bool Keeps => false;

    public string Mode => "";

    public int Stage => 0;

    public (int Role, int Class) Of(EntityHandle player) => (0, 0);

    public int WeaponClass(EntityHandle killer) => 0;

    public bool RoundOver => false;

    public int WinningRole => 0;
}
