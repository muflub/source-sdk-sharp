#if DEBUG
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_areaportals</c>: the area portal records the per-client visibility
/// pass walks, one line each, in creation order. It was a native debug command
/// over g_AreaPortals (shell_areaportal_debug.cpp) until the records moved to
/// <see cref="AreaPortals"/> (plan_server_native_v2 T4d); the lines are the
/// same words.
///
/// <para>
/// The question it answers is "is the portal in the list, with the number vbsp
/// gave it, and is the map's switch reaching it": a portal absent from the list
/// is walked by nothing, its bit is never set, and the room beyond it stops
/// drawing with no error anywhere.
/// </para>
/// </summary>
[ConCommand("cs_sv_areaportals",
    Help = "SourceSharp: print the area-portal list the per-client visibility pass walks")]
public sealed class AreaPortalsCommand(AreaPortals portals, IEngineConsole console) : IConCommand
{
    public void Execute(string args)
    {
        console.Msg($"[SourceSharp] area portals: {portals.Count} in the list\n");

        foreach (string line in portals.Describe())
            console.Msg($"[SourceSharp]   {line}\n");
    }
}
#endif
