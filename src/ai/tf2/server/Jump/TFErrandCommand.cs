using System.Globalization;
using SourceSharp.Abi;
using SourceSharp.Ai.Server;
using SourceSharp.Server;
using SourceSharp.Shared;

namespace SourceSharp.Ai.TF2.Jump;

/// <summary>
/// <c>ai_tf_errand &lt;name&gt; x y z | off</c> (lane G5c-2b): sends a bot, by its own tree, to a place, and keeps it
/// going there until called off. Unlike <c>ai_bot_goto</c>, which walks the bot with its brain set aside, the tree
/// still decides (the goal is the only thing set by hand), so the route's links, the jump's rule and the program
/// are the brain's own: what <c>tools/ai-tf-jump links</c> proves a blast link with. Nobody in sight and nothing
/// carried, as for any goal the tree would give way on (<see cref="Trees.TFDecider"/>).
/// </summary>
[ConCommand("ai_tf_errand", Help = "ai_tf_errand <name> <x y z | off>: send a bot by its own tree to a place, until called off (proving a blast link)", Flags = ConVarFlags.Cheat)]
public sealed class TFErrandCommand(AiBots bots, TFJumps jumps, ILog log)
{
    private IEnumerable<string> BotNames() => bots.Bots.Select(b => b.Name);

    public void Execute([Complete(From = nameof(BotNames))] string name, string xOrOff, float? y = null, float? z = null)
    {
        AiBot[] named = [.. bots.Bots.Where(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))];
        if (named.Length == 0)
        {
            log.Warning($"[SourceSharp/Ai] ai_tf_errand refused: no bot called '{name}'\n");
            return;
        }

        int slot = named[0].Id.Slot;
        if (string.Equals(xOrOff, "off", StringComparison.OrdinalIgnoreCase))
        {
            jumps.Book.SetErrand(slot, null);
            log.Msg($"[SourceSharp/Ai] ai_tf_errand: {named[0].Name} goes about its own business\n");
            return;
        }

        if (y is not { } atY || z is not { } atZ || !float.TryParse(xOrOff, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
        {
            log.Warning("[SourceSharp/Ai] usage: ai_tf_errand <name> <x y z | off>\n");
            return;
        }

        jumps.Book.SetErrand(slot, new AiVector(x, atY, atZ));
        log.Msg(string.Create(CultureInfo.InvariantCulture, $"[SourceSharp/Ai] ai_tf_errand: {named[0].Name} goes to ({x:F0} {atY:F0} {atZ:F0})\n"));
    }
}
