//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;
using SourceSharp.Shared;

using TF2.Shared;

namespace TF2.Server.Player;

/// <summary>
/// The <c>disguise &lt;class&gt; &lt;team&gt;</c> client command
/// (<c>cc_tf_player_disguise</c>, c_tf_player.cpp:9637-9679, over the
/// server's <see cref="ITFSpyState"/>). Stock's client intercepts the team
/// value because the menu's numbers do not match the defines: -1 is the
/// enemy team, -2 the player's own, 1 is BLUE and anything else RED.
/// </summary>
[SideService(Side.Server)]
public sealed class TFSpyDisguiseCommand(ITFSpyState spies, IEnginePlayers players) : IClientCommandHook, IOrderedHook
{
    public const string Verb = "disguise";

    /// <summary>Stage 2.12's band, after the component source.</summary>
    public const int Order = 1_000_000 - 1_000 + 981;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command != Verb || args.Length < 2)
            return false;

        if (!int.TryParse(args[0], out int nClass) || !int.TryParse(args[1], out int nTeam))
            return true;

        var localTeam = (TFTeam)players.GetTeam(player);
        var team = nTeam switch
        {
            -1 => localTeam == TFTeam.Blue ? TFTeam.Red : TFTeam.Blue,
            -2 => localTeam,
            _ => nTeam == 1 ? TFTeam.Blue : TFTeam.Red,
        };

        spies.Disguise(player.EntryIndex, (TFClass)nClass, team);
        return true;
    }
}

/// <summary>
/// The <c>lastdisguise</c> client command (<c>cc_tf_player_lastdisguise</c>,
/// c_tf_player.cpp:9594-9633).
/// </summary>
[SideService(Side.Server)]
public sealed class TFSpyLastDisguiseCommand(ITFSpyState spies) : IClientCommandHook, IOrderedHook
{
    public const string Verb = "lastdisguise";

    /// <summary>Stage 2.12's band, after <see cref="TFSpyDisguiseCommand"/>.</summary>
    public const int Order = 1_000_000 - 1_000 + 982;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command != Verb)
            return false;

        // The optional class argument, used when it names a class
        // (c_tf_player.cpp:9611-9615); anything else falls to the stored one.
        var desired = TFClass.Undefined;
        if (args.Length > 0 && int.TryParse(args[0], out int nClass)
            && nClass >= (int)TFClass.Undefined && nClass <= (int)TFClass.CountAll)
            desired = (TFClass)nClass;

        spies.LastDisguise(player.EntryIndex, desired);
        return true;
    }
}

/// <summary>
/// The cloak's toggle as a command. Stock arms it on the special-skill key
/// (<c>IN_ATTACK2</c> → <c>DoClassSpecialSkill</c>, tf_player_shared.cpp:12359),
/// which the weapon machine does not carry to an inactive weapon yet — the
/// command stands in until the button path lands (docs/plans/tf2/deferred.tsv).
/// </summary>
[SideService(Side.Server)]
public sealed class TFSpyCloakCommand(ITFSpyState spies) : IClientCommandHook, IOrderedHook
{
    public const string Verb = "cloak";

    /// <summary>Stage 2.12's band, after <see cref="TFSpyLastDisguiseCommand"/>.</summary>
    public const int Order = 1_000_000 - 1_000 + 983;

    public int HookOrder => Order;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        if (command != Verb)
            return false;

        spies.ActivateInvisibilityWatch(player.EntryIndex);
        return true;
    }
}
