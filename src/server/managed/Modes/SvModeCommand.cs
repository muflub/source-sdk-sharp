using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// The dev switch (plan 0.7's generic names, in the core): <c>sv_mode &lt;id&gt;</c>
/// performs the D6 switch-with-warmup through <see cref="GameModes.SwitchTo"/>
/// — flush, keep the players, set up, warm up, start. Bare <c>sv_mode</c>
/// reports the active mode and the registered ids rather than switching to
/// nothing, because a typed command that silently did nothing is the silent
/// no-op the rules refuse.
/// </summary>
[ConCommand("sv_mode", Help = "sv_mode <id>: switch the game mode (D6 warmup switch); bare lists")]
public sealed class SvModeCommand(ILog log, IServiceLocator services) : IConCommand
{
    public void Execute(string args)
    {
        string id = args.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";

        // Resolved at execute, not constructed with: the command is bound at
        // convar registration, before the module has built the proxy.
        if (services.Resolve<GameModes>() is not { } modes)
        {
            log.Warning("[SourceSharp/Modes] sv_mode: the mode proxy is not up yet\n");
            return;
        }

        if (id.Length == 0)
        {
            log.Msg($"[SourceSharp/Modes] active: '{modes.ActiveId ?? "(none — the core's own rules)"}'; " +
                    $"registered: {string.Join(", ", modes.Registered.Select(m => m.Id))}\n");
            return;
        }

        // SwitchTo says out loud what it did, including the refused unknown
        // id (log + unchanged game — the plan's arm); the command adds only
        // the request line so the live gate can see the stimulus.
        log.Msg($"[SourceSharp/Modes] sv_mode {id}\n");
        modes.SwitchTo(id);
    }
}
