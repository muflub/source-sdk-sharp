using SourceSharp.Abi;
using SourceSharp.Ai.Server.Nav;

namespace SourceSharp.Ai.TF2.Nav;

/// <summary>
/// TF2's maps have their navigation built in the server when they have none (plan_bots_teams_tf2 E.3, P6): every map,
/// as HL2MP's are. The build reads the props' models through the engine's file seam, which mounts what
/// <c>make ai-nav-tf</c> mounts for ssmap (the TF2 content and the engine's hl2), so it writes the same file. A map too
/// big for a <c>.nav3d</c> is refused by the build, naming why; a hull navigation is built only offline.
/// </summary>
[SideService(Side.Server, As = [typeof(INavBuildPolicy)])]
public sealed class TFNavBuilds : INavBuildPolicy
{
    public bool Builds(string map) => true;
}
