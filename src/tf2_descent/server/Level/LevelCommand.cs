using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Descent.Server.Level;

/// <summary>
/// <c>tf2_descent_level</c>: prints what the server read of the level it is on (WO1): the load
/// line, then every room, door, marker and arrival point.
/// </summary>
[ConCommand("tf2_descent_level", Help = "tf2_descent_level: list the current Descent level's rooms, doors, markers and arrival points, as the server read them.",
            Flags = ConVarFlags.Cheat)]
public sealed class LevelCommand(ILevelData level, ILog log)
{
    public void Execute()
    {
        if (level.Current is not { } current)
        {
            log.Msg("[TF2Descent/Level] this map is not a Descent level: no level data\n");
            return;
        }

        foreach (string line in LevelSummary.Listing(current))
            log.Msg(line);
    }
}
