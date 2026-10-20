//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;

namespace SourceSharp.Server;

/// <summary>
/// <c>UTIL_IsCommandIssuedByServerAdmin</c>, as a rule rather than as a call.
///
/// <para>
/// Stock's body (now <c>sourcesharp/game/server/shell_commandclient.cpp:180</c>)
/// is three comparisons over two facts: the slot the engine attributes the
/// command to, and whether this is a dedicated server. Both facts are on this
/// side — <see cref="IEnginePlayers.GetCommandClient"/> and
/// <see cref="IEngineHost.IsDedicatedServer"/> — so the rule can be here, and
/// a console command declared in C# no longer has to reach into C++ to ask who
/// typed it.
/// </para>
///
/// <para>
/// <b>This contradicts a note left in <c>game/server/globalstate.cpp</c>.</b>
/// That file's header said its two ConCommands could not become <c>[ConCommand]</c>
/// classes because "<c>Engine.GetCommandClient()</c> is half of it;
/// <c>engine-&gt;IsDedicatedServer()</c> has no slot". It does:
/// <c>IEngineHost.IsDedicatedServer</c> is bound through
/// <c>IFwdEngineServer.IsDedicatedServer()</c>
/// (<c>EngineBinding.cs:51</c>, <c>EngineServer.cs:170</c>) and
/// <c>DevCommandsLive.cs:606</c> has been reading it for <c>ent_fire</c>'s own
/// issuer rule. The note was true when it was written and is not now.
/// </para>
///
/// <para>
/// <b>Not <see cref="EntFireAccess"/>, and the difference is the whole point.</b>
/// <c>ent_fire</c> refuses the DEDICATED server console outright, because the
/// question it is really asking — "does this client have autokick disabled" —
/// cannot be answered here. Stock's admin rule ADMITS that console: it is the
/// server operator, and <c>dump_globals</c> is exactly the sort of thing they
/// type. Two rules, two names, and neither is a slightly-wrong copy of the
/// other.
/// </para>
/// </summary>
public static class ServerAdmin
{
    /// <summary>The slot that means the SERVER console, not a client, issued it.</summary>
    public const int ServerConsole = 0;

    /// <summary>
    /// Stock's rule, pure: given the 1-based slot the engine attributed the
    /// command to (<see cref="ServerConsole"/> when nobody did) and whether this
    /// is a dedicated server, may it run?
    ///
    /// <para>
    /// Transliterated statement for statement from the C++ so the two can be
    /// read side by side:
    /// </para>
    /// <code>
    ///   if ( engine-&gt;IsDedicatedServer() &amp;&amp; issuingPlayerIndex &gt; 0 )  return false;
    ///   else if ( issuingPlayerIndex &gt; 1 )                            return false;
    ///   return true;
    /// </code>
    ///
    /// <para>
    /// So: the server console always; slot 1 — the listen-server host — only
    /// when this is not a dedicated server; nobody else, ever. Stock's
    /// <c>REPLAY_ENABLED</c> arm is absent for the reason
    /// <c>shell_commandclient.cpp</c> records at length: the define is on
    /// neither link line, so the arm was unreachable before the port and is
    /// unreachable after it. Since <c>plan_shim.md</c> step 5c it is also
    /// absent from the C++ — HLTV and Replay are dropped, so the native side
    /// no longer carries the <c>false</c> local either, and the two really do
    /// read statement for statement.
    /// </para>
    /// </summary>
    public static bool MayRun(int commandClientSlot, bool dedicatedServer)
    {
        if (dedicatedServer && commandClientSlot > ServerConsole)
            return false;

        if (commandClientSlot > 1)
            return false;

        return true;
    }

    /// <summary>
    /// The same rule against a live engine.
    ///
    /// <para>
    /// The slot is found the way <see cref="EntFireAccess.SlotOf"/> finds it —
    /// the engine area answers "who typed it" as a HANDLE and the rule is about
    /// a SLOT, so the occupied slots are asked which one holds it, and a handle
    /// nobody holds is <see cref="ServerConsole"/>. That is the same mapping
    /// <c>UTIL_GetCommandClientIndex</c> performs with <c>CommandClient().Slot()+1</c>.
    /// </para>
    /// </summary>
    public static bool MayRun(IEnginePlayers players, IEngineHost host)
        => MayRun(EntFireAccess.SlotOf(players, players.GetCommandClient()),
                  host.IsDedicatedServer);
}

/// <summary>
/// <c>dump_globals</c> — stock's <c>CGlobalState::DumpGlobals</c> console
/// command, declared where the table it prints lives.
///
/// <para>
/// It was the last ConCommand in <c>game/server/globalstate.cpp</c>, and that
/// declaration was the whole of what kept the file in the build once
/// <see cref="GlobalStateTable"/> had the table and
/// <see cref="GlobalStateTable.OnLevelInitPreEntity"/> had the two platform
/// globals. The name, the help text and the admin gate are stock's; the body is
/// one call, because the dump is the table's own.
/// </para>
/// </summary>
[ConCommand("dump_globals", Help = "Dump all global entities/states")]
public sealed class DumpGlobalsCommand : IConCommand
{
    private readonly GlobalStateTable _globals;
    private readonly IEnginePlayers _players;
    private readonly IEngineHost _host;

    public DumpGlobalsCommand(GlobalStateTable globals, IEnginePlayers players, IEngineHost host)
    {
        _globals = globals;
        _players = players;
        _host = host;
    }

    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(_players, _host))
            return;

        _globals.Dump();
    }
}

/// <summary>
/// <c>server_game_time</c> — stock's one-line console command, kept because it
/// is what a person debugging a timing problem types, and because it was
/// declared in the same file as <c>dump_globals</c> and would otherwise have
/// left the build with it and said nothing.
///
/// <para>
/// <c>%f</c> is stock's format and is reproduced as <c>F6</c>: the six decimals
/// are the point of the command, and a shorter answer would round away the tick
/// boundary somebody is reading it to find.
/// </para>
/// </summary>
[ConCommand("server_game_time", Help = "Gives the game time in seconds (server's curtime)")]
public sealed class ServerGameTimeCommand : IConCommand
{
    // This class's log, handed in by its composition (statics to zero).
    private readonly ILog _log;

    private readonly IGameClock _clock;
    private readonly IEnginePlayers _players;
    private readonly IEngineHost _host;

    public ServerGameTimeCommand(ILog log, IGameClock clock, IEnginePlayers players, IEngineHost host)
    {

        _log = log;
        _clock = clock;
        _players = players;
        _host = host;
    }

    public void Execute(string args)
    {
        if (!ServerAdmin.MayRun(_players, _host))
            return;

        _log.Msg($"Server game time: {_clock.Now.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)}\n");
    }
}
