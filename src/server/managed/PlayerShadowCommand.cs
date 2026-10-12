#if DEBUG
using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>cs_sv_playershadow</c>: the player shadows' switch and counters, and the
/// two native keeps (plan_physics_bones_native_zero 2.5: the counters are
/// PlayerShadowBodies'; it was a native debug command over native's). The same
/// two lines it printed; the dev API's <c>/counters?group=playershadow</c>
/// reads the same objects (<see cref="DebugCounterGroups.PlayerShadow"/>).
/// </summary>
[ConCommand("cs_sv_playershadow",
    Help = "SourceSharp: the player's VPhysics shadow -- how many were built, how many updates, how many of those were in contact, how many snapped (plan_entity.md step 3f)")]
public sealed class PlayerShadowCommand(PlayerShadowBodies bodies, IEngineDiagnostics diagnostics, IEngineConsole console)
    : IConCommand
{
    public void Execute(string args)
    {
        Span<int> native = stackalloc int[2];
        int read = diagnostics.PlayerShadowCounts(native);
        int live = read > 0 ? native[0] : 0;
        int outlived = read > 1 ? native[1] : 0;

        console.Msg($"[SourceSharp] playershadow: enabled={(bodies.Enabled ? 1 : 0)} built={bodies.Built} " +
                    $"updates={bodies.Updates} contacts={bodies.Contacts} touches={bodies.Touches} snaps={bodies.Snaps}\n");
        console.Msg($"[SourceSharp] playershadow: live={live} outlivedTheirEnvironment={outlived}\n");
    }
}
#endif
