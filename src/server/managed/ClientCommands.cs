using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// A word a player's console typed, offered to mods before the core's own
/// commands see it (tf2 stage 0.6).
///
/// <para>
/// THE ONE ROUTE a mod has at a client command. The core's
/// <c>ServerModule.OnClientCommand</c> handles the stock verbs inline —
/// selection, cheats, observer movement, teams — and a mod could not put a
/// verb beside them without editing the core. TF2 needs exactly that
/// (<c>joinclass</c>, later stages), and Descent wants a couple of its own.
/// </para>
///
/// <para>
/// A CHAIN, and the first <c>true</c> wins: the command is one action, and
/// after a hook has acted the core must not also act. Order is
/// <see cref="IOrderedHook.HookOrder"/>'s, deterministic, asked in that order
/// and no further once one claims. A hook that throws is logged and skipped
/// — the next hook and then the core still get the word; a broken mod must
/// not swallow everyone's commands.
/// </para>
///
/// <para>
/// WHAT THE HOOK GETS: the player, the verb lower-cased-in-spelling but
/// compared as the mod likes, and the arguments WITHOUT the verb
/// (<c>joinclass scout</c> arrives as <c>("joinclass", ["scout"])</c>).
/// Reply to that one player through <see cref="IChat"/> —
/// <c>IChat.Print(entIndex, text)</c> is the SayText channel, and
/// <c>IEngineEntities.GetIndex(player)</c> turns the handle into the index
/// it takes. A second reply channel would be a second SayText.
/// </para>
/// </summary>
public interface IClientCommandHook
{
    /// <summary>
    /// Whether this hook acted. <c>true</c> ends the command here — the
    /// core's own switch never sees it; <c>false</c> passes it to the next
    /// hook, and finally to the core, unchanged.
    /// </summary>
    bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args);
}
