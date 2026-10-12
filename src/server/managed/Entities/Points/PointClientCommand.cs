//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Shared;

namespace SourceSharp.Server;

/// <summary>
/// Lets a map type at one player's console.
///
/// The sibling of point_servercommand and much the smaller risk: what it can
/// reach is the one client's own settings, and a client is entitled to refuse —
/// a command it does not have simply does nothing, and nothing comes back
/// either way. So this is a way to tell a player's game something, never to ask
/// it anything.
///
/// <para>
/// What it decides is WHO, and in a multiplayer game there is only one sensible
/// answer: whoever caused the input. That is the whole of the entity, and it is
/// why the ABI call underneath takes a player rather than knowing about this
/// class.
/// </para>
/// </summary>
[ScriptedEntity(EntityNames.PointClientcommand)]
public sealed class PointClientCommand : ScriptedEntity
{
    public PointClientCommand(IEngineConsole console)
    {
        Console = console;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;
    /// <summary>
    /// InputData rather than a string, because the activator is the entity's
    /// only real decision and the binder hands it over nowhere else.
    /// </summary>
    [ScriptedEvent("Command")]
    private void OnCommand(InputData input)
    {
        if (input.Value.Length == 0)
            return;

        // Stock has two fallbacks for an activator that is not a player, and
        // neither survives into this build: one sends to slot 1 when maxplayers
        // is 1, and the other pretends the commentary system's stuffed commands
        // came from the first player. Singleplayer and commentary both left the
        // server with the rest of the SDK's singleplayer subsystems, so what is
        // left is the multiplayer rule on its own.
        //
        // Which makes silence the wrong answer where stock could afford it: map
        // logic that fires this from a timer rather than from something a player
        // did has no activator, and would look like a client ignoring the
        // command rather than like nobody having been chosen.
        if (!Entities.IsPlayer(input.Activator))
        {
            Log.Warning($"[SourceSharp] point_clientcommand '{Name}': nothing to send '{input.Value}' to; " +
                            "whatever fired this was not a player, and this entity only ever answers the activator\n");
            return;
        }

        Console.SendClientCommand(input.Activator, input.Value);
    }
}
