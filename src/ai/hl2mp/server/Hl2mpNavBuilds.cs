using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.Hl2mp;

/// <summary>HL2MP's maps have their navigation built when they have none (plan_bots_teams_tf2 E.3, ruling Q10): every map.</summary>
[SideService(Side.Server, As = [typeof(INavBuildPolicy)])]
public sealed class Hl2mpNavBuilds : INavBuildPolicy
{
    public bool Builds(string map) => true;
}
