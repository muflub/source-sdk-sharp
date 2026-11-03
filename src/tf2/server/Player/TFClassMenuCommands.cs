//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Server;

namespace TF2.Server.Player;

/// <summary>
/// The class menu's server half: the <c>menuopen</c> and <c>menuclosed</c>
/// arms of <c>CTFPlayer::ClientCommand</c> (tf_player.cpp:7482-7491). The
/// client's class menu sends each as its visibility changes
/// (tf_classmenu.cpp:1182, :1199) and each is stock's
/// <c>SetClassMenuOpen</c> (:8073-8076) on <see cref="TFPlayerClass.MenuOpen"/>.
/// </summary>
[SideService(Side.Server)]
public sealed class TFClassMenuCommands(TFPlayerCarriers carriers) : IClientCommandHook, IOrderedHook
{
    /// <summary>The menu shown (tf_classmenu.cpp:1182).</summary>
    public const string Open = "menuopen";

    /// <summary>The menu hidden (tf_classmenu.cpp:1199).</summary>
    public const string Closed = "menuclosed";

    /// <summary>
    /// On +75 (see <see cref="TFPlayerOrders.ClassMenu">TFPlayerOrders</see>):
    /// the commands arrive between the lives, from a menu the server's own
    /// shows and closes raise.
    /// </summary>
    public int HookOrder => TFPlayerOrders.ClassMenu;

    public bool Handle(EntityHandle player, string command, ReadOnlySpan<string> args)
    {
        bool open;
        if (string.Equals(command, Open, StringComparison.OrdinalIgnoreCase))
            open = true;
        else if (string.Equals(command, Closed, StringComparison.OrdinalIgnoreCase))
            open = false;
        else
            return false;

        if (carriers.Part<TFPlayerClass>(player.EntryIndex) is { } held)
            held.MenuOpen = open; // SetClassMenuOpen (tf_player.cpp:8073-8076)

        return true;
    }
}
