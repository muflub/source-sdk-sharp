using SourceSharp.Abi;
using SourceSharp.Server;

namespace SourceSharp.Ai.Server;

/// <summary>
/// A bot's client command (plan_bots_teams_tf2 C.3): a word a player's key would send, such as
/// <c>build 2 0</c> or <c>disguise 8 2</c>, run as that player's.
/// </summary>
public interface IBotClientCommand
{
    void Run(EntityHandle bot, string command);
}

/// <summary>
/// <see cref="IBotClientCommand"/> through the mods' command chain (<see cref="IClientCommandHook"/>),
/// the route a game's own verbs take when a player types them: the hooks in their order, the first
/// that claims the word owns it, one that throws is named and skipped. A word no hook claims is
/// named in the log: an ability row is a game's verb, never one of the core's.
/// </summary>
[SideService(Side.Server, As = [typeof(IBotClientCommand)])]
public sealed class HookedBotClientCommand(IHooks<IClientCommandHook> hooks, ILog log) : IBotClientCommand
{
    /// <summary>Words a hook claimed.</summary>
    public long Claimed { get; private set; }

    public void Run(EntityHandle bot, string command)
    {
        string[] parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;
        foreach (IClientCommandHook hook in hooks.All)
        {
            bool claimed;
            try
            {
                claimed = hook.Handle(bot, parts[0], parts.AsSpan(1));
            }
            catch (Exception e)
            {
                log.Warning($"[SourceSharp/Ai] client command hook {hook.GetType().FullName} threw on a bot's '{command}': {e.Message}\n");
                continue;
            }

            if (claimed)
            {
                Claimed++;
                return;
            }
        }

        log.Warning($"[SourceSharp/Ai] no game verb took a bot's '{command}'\n");
    }
}
