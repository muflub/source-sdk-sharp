//========= Copyright Valve Corporation, All rights reserved. ============//
//
// Inspired by and based on the Half-Life 2 Source SDK 2013 by Valve:
// https://github.com/ValveSoftware/source-sdk-2013
//
//=============================================================================//

using SourceSharp.Abi;
using SourceSharp.Client.Interop;

namespace SourceSharp.Client.Effects;

/// <summary>
/// The client's effect dispatch: <c>c_te_effect_dispatch.cpp</c>'s
/// <c>DispatchEffectToCallback</c> in C#. A name arrives (from the networked
/// <c>CTEEffectDispatch</c>, or from native code dispatching for itself) and the
/// managed handler registered under it runs. Every <c>DECLARE_CLIENT_EFFECT</c>
/// that shipped moved with its subsystem, so no native registration is left.
/// </summary>
public sealed class ClientEffectDispatch : IClientEffectDispatch
{
    private readonly IEngineConsole _console;
    private readonly Dictionary<string, int> _runsByName = new(StringComparer.Ordinal);

    private readonly IClientEffectHandlers? _handlers;

    /// <param name="handlers">The managed client effects (a DECLARE_CLIENT_EFFECT
    /// that moved with its subsystem, such as the ropes' ShakeRopes); none in a
    /// container that registers none.</param>
    public ClientEffectDispatch(IEngineConsole console, IClientEffectHandlers? handlers = null)
    {
        _console = console;
        _handlers = handlers;
    }

    /// <summary>Dispatches that resolved and ran.</summary>
    public int Ran { get; private set; }

    /// <summary>Dispatches whose name matched no registration.</summary>
    public int Unclaimed { get; private set; }

    /// <summary>The name the last dispatch asked for, resolved or not.</summary>
    public string LastName { get; private set; } = string.Empty;

    /// <summary>Runs per registered name, for the effects route and its gate.</summary>
    public IReadOnlyDictionary<string, int> RunsByName => _runsByName;

    public void Dispatch(string name, in EffectData data)
    {
        LastName = name;

        if (_handlers is not null && _handlers.TryDispatch(name, in data))
        {
            Ran++;
            _runsByName[name] = _runsByName.TryGetValue(name, out int m) ? m + 1 : 1;
            return;
        }

        Unclaimed++;

        // Stock's DevMsg: printed at developer 1 and above, silent below.
        if (int.TryParse(_console.FindConVarValue("developer"), out int level) && level >= 1)
            _console.Msg($"DispatchEffect: effect '{name}' not found on client\n");
    }
}
