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
/// Lets a map type at the server console.
///
/// The most dangerous entity in the SDK and the only one with a convar built to
/// switch it off, which is why what it owns is not the command but the
/// permission: an untrusted map that can run <c>rcon_password</c> or
/// <c>exec</c> owns the server. Everything about it is that one gate, and the
/// gate is a decision — so it belongs here rather than behind a native call
/// that would have made the choice on C#'s behalf.
/// </summary>
[ScriptedEntity(EntityNames.PointServercommand)]
public sealed class PointServerCommand : ScriptedEntity
{
    public PointServerCommand(IEngineConsole console)
    {
        Console = console;
    }

    /// <summary>the console: convars, and commands run on the server or a client.</summary>
    private readonly IEngineConsole Console;
    /// <summary>
    /// The gate, read live and BY NAME — which is why Phase 1b could move the
    /// declaration from C++ (client.cpp, then shell_client_orphans.cpp) into
    /// <see cref="AllowPointServerCommandConVar"/> without touching anything
    /// here: FindConVarValue asks the engine's one registry, and the registry
    /// does not record which side put the name in it.
    /// </summary>
    internal const string AllowConVar = "sv_allow_point_servercommand";

    /// <summary>
    /// Commands taken but not yet run. See <see cref="OnThink"/> for why there is
    /// a wait at all.
    /// </summary>
    private readonly Queue<string> _pending = new();

    /// <summary>
    /// Cached so the think does not allocate a delegate every time it drains.
    ///
    /// Built on first use rather than in a field initializer: the console
    /// arrives by injection, which happens after construction, so an initializer
    /// would capture a null.
    /// </summary>
    private Action<string>? _runOnServer;

    private Action<string> RunOnServer => _runOnServer ??= Console.ExecuteServerCommand;

    [ScriptedEvent("Command")]
    private void OnCommand(string command)
    {
        if (command.Length == 0)
            return;

        if (!Allows(Console.FindConVarValue(AllowConVar)))
        {
            // Stock's exact sentence, kept verbatim rather than given this
            // file's usual [SourceSharp] prefix. It is the one warning here an
            // operator is likely to arrive at from outside — from the convar's
            // own help text, or from somebody else's server log — and a
            // rewording would break the search that got them here.
            Log.Warning("point_servercommand usage blocked by sv_allow_point_servercommand setting\n");
            return;
        }

        Take(command);
    }

    /// <summary>
    /// Accepts a command for the next think.
    ///
    /// Separate from the input handler because the gate above it reads a convar,
    /// and a convar that cannot be read is a refusal — so with no game running
    /// nothing gets past it, and the deferral would have no way to be tested.
    /// </summary>
    internal void Take(string command)
    {
        _pending.Enqueue(command);
        SetNextThink(0f);
    }

    /// <summary>
    /// Runs what was taken, one frame later, which is the whole reason this
    /// entity has a think.
    ///
    /// <para>
    /// Stock hands the text to <c>engine->ServerCommand</c> and stops, and the
    /// engine drains its buffer at the next frame boundary. That delay is not
    /// incidental: the commands a map actually fires are the drastic ones, and
    /// <c>changelevel</c> executed from inside input dispatch tears down the
    /// entity list while <c>AcceptInput</c> is still walking it — the entity that
    /// asked, and every entity after it in the same output's target list, is
    /// freed underneath the loop.
    /// </para>
    ///
    /// <para>
    /// <see cref="IEngineConsole.ExecuteServerCommand"/> deliberately does NOT wait; it
    /// calls <c>ServerExecute</c> so the effect has landed by the time it
    /// returns, which is what the development API needs of it. So the wait
    /// belongs to this entity rather than to that call, and a think is where an
    /// entity in this mod says "not now".
    /// </para>
    ///
    /// <para>
    /// A queue rather than one slot, because several commands legitimately arrive
    /// before the think: two connections on the same output, or one entity fired
    /// twice by a logic_case. Stock keeps all of them, in order, and so does
    /// this.
    /// </para>
    /// </summary>
    public override void OnThink() => Drain(RunOnServer);

    /// <summary>
    /// Empties the queue through <paramref name="run"/>, in the order the
    /// commands arrived.
    ///
    /// Takes the action rather than reaching for the engine directly so the
    /// deferral itself can be tested — that a command waits, that it is still the
    /// command that was asked for, and that two of them keep their order are the
    /// three things this entity now promises, and none of them need a game.
    /// </summary>
    internal void Drain(Action<string> run)
    {
        while (_pending.Count > 0)
            run(_pending.Dequeue());
    }

    /// <summary>How many commands are waiting. For tests.</summary>
    internal int PendingCount => _pending.Count;

    /// <summary>
    /// Whether the setting permits a map to run commands at all.
    ///
    /// Reads the convar live rather than caching a parse at load, which stock
    /// does through a change callback. The same answer either way, and one less
    /// piece of state to be stale.
    ///
    /// <para>
    /// Only "always" opens the gate. Stock has a third value, "official",
    /// allowing it in Valve's own maps — that branch is compiled only into TF,
    /// which is the only game with an <c>IsValveMap</c> to ask, and outside TF
    /// stock treats the word as a refusal exactly as this does.
    /// </para>
    ///
    /// <para>
    /// A convar that cannot be read at all is a refusal too, and that direction
    /// is deliberate: the value is a security setting, and the failure mode of
    /// guessing wrong is a map running commands nobody permitted. It cannot
    /// happen in a built game — client.cpp registers the convar whether or not
    /// this entity is C# — so failing closed costs nothing real.
    /// </para>
    ///
    /// Static and taking the value rather than reading the convar, so the gate
    /// can be tested without an engine.
    /// </summary>
    internal static bool Allows(string? setting)
        => setting is not null
        && setting.Equals("always", StringComparison.OrdinalIgnoreCase);
}
