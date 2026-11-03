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
/// TF2's rename gate (<see cref="INameChangeGate"/>):
/// <c>CTFGameRules::ChangePlayerName</c>'s first line
/// (tf_gamerules.cpp:10217) — no rename once
/// <c>tf_allow_player_name_change</c> (tf_gamerules.cpp:731) is off, the
/// differ-check and the event half being the core rename's. Not ported of
/// the same function's caller: the connect-time
/// <c>engine->ServerCommand("lockplayername %d")</c>
/// (tf_client.cpp:79) — a server command this engine's binaries do not
/// define, so there is nothing to ask.
/// </summary>
[SideService(Side.Server, As = [typeof(INameChangeGate)])]
public sealed class TFNameChangeGate(TFAllowPlayerNameChangeConVar allow) : INameChangeGate
{
    public bool MayChange(int entIndex, string oldName, string newName) => allow.Value;
}
