using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// <c>changeteam</c>, the client's "change team" key (plan_bots_teams_tf2 A.3
/// item 7): under a mode that plays with teams the server shows that player
/// the team menu again (<c>VGUIMenu team</c>, <see cref="TeamMenuOnFirstSpawn.Panel"/>),
/// where a choice comes back as <c>jointeam N</c>. A mode without teams does
/// not claim the word, so deathmatch answers it as it always has.
/// </summary>
public sealed class TeamMenuCommand(GameModes modes, IUserMessageSender messages) : IClientCommandHook, IOrderedHook
{
    /// <summary>The word the client's key sends.</summary>
    public const string Command = "changeteam";

    /// <summary>Below the mods' band (TF2's 999,000-999,999 and <see cref="ModStages.First"/>).</summary>
    public int HookOrder => 998_900;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (!string.Equals(command, Command, StringComparison.OrdinalIgnoreCase) || modes.Active?.Teamplay is not true)
            return false;

        TeamMenuOnFirstSpawn.Show(messages, player);
        return true;
    }
}
